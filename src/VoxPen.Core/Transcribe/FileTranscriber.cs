using System.Diagnostics;
using System.Text;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;
using VoxPen.Core.Hotword;
using VoxPen.Core.Hotword.Phoneme;

namespace VoxPen.Core.Transcribe;

/// <summary>
/// 文件批量转录编排器：
/// 解码 → 分段（<see cref="AudioSegmenter"/>）→ 逐段 <see cref="IAsrEngine.RecognizeAsync"/>
/// → 文本/Token 拼接（<see cref="SegmentMerger"/>）→ 后处理（HotRule → PhonemeRag）→ 分行（<see cref="SmartSplit"/>）
/// → 落盘（.txt / .srt / .json / .merge.txt）。
///
/// 与 Python 客户端 <c>File_Transcriber</c> 保持一致的四文件输出策略。
/// </summary>
public sealed class FileTranscriber
{
    private readonly IAsrEngine _asr;
    private readonly IAudioDecoder _decoder;
    private readonly TranscribeConfig _config;
    private readonly HotRuleReplacer _hotRule;
    private readonly PhonemeCorrector? _phonemeCorrector;
    private readonly ILogSink? _log;

    /// <summary>单条转录结果。</summary>
    public sealed record TranscribeResult(
        string SourcePath,
        string Text,
        string SplitText,
        string[] Tokens,
        float[] Timestamps,
        IReadOnlyList<string> WrittenFiles,
        TimeSpan Elapsed);

    /// <summary>日志回调。</summary>
    public interface ILogSink
    {
        void OnInfo(string message);
        void OnWarn(string message);
    }

    /// <summary>构造。<paramref name="phonemeCorrector"/> 为 null 时跳过音素 RAG。</summary>
    public FileTranscriber(
        IAsrEngine asr,
        IAudioDecoder decoder,
        TranscribeConfig config,
        HotRuleReplacer? hotRule = null,
        PhonemeCorrector? phonemeCorrector = null,
        ILogSink? log = null)
    {
        _asr = asr ?? throw new ArgumentNullException(nameof(asr));
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _hotRule = hotRule ?? HotRuleReplacer.Empty;
        _phonemeCorrector = phonemeCorrector;
        _log = log;
    }

    /// <summary>
    /// 转录单个音频文件。<paramref name="outputPath"/> 为 null 时输出到源文件同目录同名不同后缀。
    /// </summary>
    public async Task<TranscribeResult> TranscribeAsync(
        string filePath,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath is empty", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("audio file not found", filePath);
        if (!_decoder.CanDecode(filePath))
            throw new NotSupportedException($"Decoder cannot handle '{Path.GetExtension(filePath)}'.");

        var sw = Stopwatch.StartNew();
        _log?.OnInfo($"[transcribe] decode: {filePath}");

        var decoded = await _decoder.DecodeAsync(filePath, cancellationToken).ConfigureAwait(false);
        var samples = decoded.Samples;
        int sampleRate = decoded.SampleRate;

        if (samples.Length == 0)
        {
            _log?.OnWarn("[transcribe] empty audio, skipping");
            return new TranscribeResult(filePath, string.Empty, string.Empty,
                Array.Empty<string>(), Array.Empty<float>(), Array.Empty<string>(), sw.Elapsed);
        }

        // 归一化到 ASR 期望的采样率（sherpa-onnx AcceptWaveform 参数固定 16k）
        if (sampleRate != _asr.SampleRate)
        {
            _log?.OnInfo($"[transcribe] resample {sampleRate} -> {_asr.SampleRate}");
            samples = LinearResample(samples, sampleRate, _asr.SampleRate);
            sampleRate = _asr.SampleRate;
        }

        // 分段识别 + Token 拼接
        var mergedTokens = new List<string>();
        var mergedTs = new List<float>();
        string mergedText = string.Empty;
        bool first = true;

        var segments = AudioSegmenter.Segment(
            samples, sampleRate, _config.SegDurationSeconds, _config.SegOverlapSeconds).ToList();

        _log?.OnInfo($"[transcribe] {segments.Count} segments " +
            $"(duration={_config.SegDurationSeconds}s overlap={_config.SegOverlapSeconds}s)");

        for (int i = 0; i < segments.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seg = segments[i];

            var recog = await _asr.RecognizeAsync(seg.Samples.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            _log?.OnInfo(
                $"[transcribe] seg {i + 1}/{segments.Count} " +
                $"@ {seg.OffsetSeconds:F1}s tokens={recog.Tokens.Count} rt={recog.Elapsed.TotalMilliseconds:F0}ms");

            // 文本合并
            if (first)
            {
                mergedText = recog.Text ?? string.Empty;
            }
            else
            {
                mergedText = SegmentMerger.MergeText(mergedText, recog.Text ?? string.Empty);
            }

            // Token + 时间戳合并
            var tokenMerge = SegmentMerger.MergeTokens(
                mergedTokens, mergedTs,
                recog.Tokens, recog.Timestamps.Select(t => (float)t).ToArray(),
                offsetSeconds: seg.OffsetSeconds,
                overlapSeconds: _config.SegOverlapSeconds,
                isFirstSegment: first);
            mergedTokens = tokenMerge.Tokens.ToList();
            mergedTs = tokenMerge.Timestamps.ToList();

            first = false;
        }

        // 后处理：HotRule → PhonemeRag
        var processedText = _hotRule.Apply(mergedText);
        if (_phonemeCorrector is not null)
        {
            var correction = _phonemeCorrector.Correct(processedText);
            processedText = correction.Text;
            if (correction.Matches.Count > 0)
            {
                _log?.OnInfo(
                    $"[transcribe] phoneme-rag applied {correction.Matches.Count} replacements");
            }
        }

        var splitText = SmartSplit.Split(processedText);

        // 落盘
        var written = await WriteOutputsAsync(
            filePath, outputPath, processedText, splitText, mergedTokens, mergedTs, cancellationToken)
            .ConfigureAwait(false);

        sw.Stop();
        _log?.OnInfo($"[transcribe] done in {sw.Elapsed.TotalSeconds:F1}s → {written.Count} file(s)");

        return new TranscribeResult(
            filePath, processedText, splitText,
            mergedTokens.ToArray(), mergedTs.ToArray(), written, sw.Elapsed);
    }

    private async Task<IReadOnlyList<string>> WriteOutputsAsync(
        string srcPath,
        string? outputBase,
        string mergedText,
        string splitText,
        IReadOnlyList<string> tokens,
        IReadOnlyList<float> timestamps,
        CancellationToken ct)
    {
        // 基准输出路径：无扩展名的完整前缀
        var baseName = outputBase is not null
            ? Path.Combine(
                Path.GetDirectoryName(outputBase) ?? string.Empty,
                Path.GetFileNameWithoutExtension(outputBase))
            : Path.Combine(
                Path.GetDirectoryName(srcPath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(srcPath));

        var written = new List<string>(4);
        var enc = new UTF8Encoding(false);

        if (_config.SaveMerge)
        {
            var p = baseName + ".merge.txt";
            await File.WriteAllTextAsync(p, mergedText, enc, ct).ConfigureAwait(false);
            written.Add(p);
        }

        if (_config.SaveTxt)
        {
            var p = baseName + ".txt";
            await File.WriteAllTextAsync(p, splitText, enc, ct).ConfigureAwait(false);
            written.Add(p);
        }

        if (_config.SaveJson)
        {
            var p = baseName + ".json";
            var json = TranscriptJsonWriter.Compose(
                timestamps.Select(t => (double)t).ToList(),
                tokens);
            await File.WriteAllTextAsync(p, json, enc, ct).ConfigureAwait(false);
            written.Add(p);
        }

        if (_config.SaveSrt)
        {
            if (tokens.Count == 0 || timestamps.Count == 0)
            {
                _log?.OnWarn("[transcribe] SRT skipped: no timestamps from engine");
            }
            else
            {
                var p = baseName + ".srt";
                var words = SubtitleAligner.BuildWordsFromTokens(
                    tokens, timestamps.Select(t => (double)t).ToList());
                var lines = splitText.Split('\n');
                var entries = SubtitleAligner.Align(lines, words);
                await SrtWriter.WriteAsync(p, entries, ct).ConfigureAwait(false);
                written.Add(p);
            }
        }

        return written;
    }

    /// <summary>简单线性插值重采样。仅用于兜底；生产路径应由 MediaFoundation 直接输出 16k。</summary>
    internal static float[] LinearResample(float[] src, int srcRate, int dstRate)
    {
        if (srcRate == dstRate || src.Length == 0) return src;
        long dstLen = (long)src.Length * dstRate / srcRate;
        if (dstLen <= 0) return Array.Empty<float>();

        var dst = new float[dstLen];
        double ratio = (double)srcRate / dstRate;
        for (long i = 0; i < dstLen; i++)
        {
            double pos = i * ratio;
            int i0 = (int)pos;
            int i1 = Math.Min(i0 + 1, src.Length - 1);
            double frac = pos - i0;
            dst[i] = (float)(src[i0] * (1.0 - frac) + src[i1] * frac);
        }
        return dst;
    }
}
