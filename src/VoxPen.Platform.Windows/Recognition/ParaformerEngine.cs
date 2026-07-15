using System.Diagnostics;
using System.Runtime.InteropServices;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;
using VoxPen.Core.Recognition;
using SherpaOnnx;

namespace VoxPen.Platform.Windows.Recognition;

/// <summary>
/// 基于 sherpa-onnx 的 Paraformer 离线识别引擎。
///
/// 模型目录约定（<see cref="AsrConfig.ModelDir"/>）：
///   model.int8.onnx  或  model.onnx
///   tokens.txt
///
/// 线程模型：RecognizeAsync 内部串行化（同一 Recognizer 实例非线程安全）。
/// </summary>
public sealed class ParaformerEngine : IAsrEngine
{
    private const int TargetSampleRate = 16000;

    private readonly AsrConfig _config;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private OfflineRecognizer? _recognizer;
    private bool _disposed;

    public string Name => "paraformer-onnx";
    public int SampleRate => TargetSampleRate;
    public bool IsLoaded => _recognizer is not null;

    public ParaformerEngine(AsrConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // sherpa-onnx 的构造函数是同步阻塞的（读文件 + 建 session），
        // 用 Task.Run 卸载到线程池，让 UI/调用方保持响应。
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _mutex.Wait(cancellationToken);
            try
            {
                if (_recognizer is not null) return;
                ThrowIfDisposed();

                var (modelPath, tokensPath) = ResolveModelPaths(_config.ModelDir);

                var recognizerConfig = new OfflineRecognizerConfig();
                recognizerConfig.FeatConfig.SampleRate = TargetSampleRate;
                recognizerConfig.FeatConfig.FeatureDim = 80;

                recognizerConfig.ModelConfig.Tokens = tokensPath;
                recognizerConfig.ModelConfig.Paraformer.Model = modelPath;
                recognizerConfig.ModelConfig.NumThreads = Math.Max(1, _config.NumThreads);
                recognizerConfig.ModelConfig.Provider = _config.Provider ?? "cpu";
                recognizerConfig.ModelConfig.Debug = 0;
                recognizerConfig.ModelConfig.ModelType = "paraformer";

                recognizerConfig.DecodingMethod = "greedy_search";

                _recognizer = new OfflineRecognizer(recognizerConfig);
            }
            finally
            {
                _mutex.Release();
            }
        }, cancellationToken);
    }

    public async Task<RecognitionResult> RecognizeAsync(
        ReadOnlyMemory<float> samples,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (samples.Length == 0)
        {
            return RecognitionResult.Empty;
        }

        if (_recognizer is null)
        {
            await LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        // sherpa-onnx C# 绑定接收 float[]；如果传入是数组段则要拷贝
        float[] sampleArray;
        if (MemoryMarshal.TryGetArray(samples, out ArraySegment<float> seg)
            && seg.Offset == 0 && seg.Count == seg.Array!.Length)
        {
            sampleArray = seg.Array;
        }
        else
        {
            sampleArray = samples.ToArray();
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _mutex.Wait(cancellationToken);
            try
            {
                ThrowIfDisposed();
                var recognizer = _recognizer!;

                var sw = Stopwatch.StartNew();
                using var stream = recognizer.CreateStream();
                stream.AcceptWaveform(TargetSampleRate, sampleArray);
                recognizer.Decode(stream);
                var result = stream.Result;
                sw.Stop();

                return new RecognitionResult
                {
                    Text = result.Text ?? string.Empty,
                    Tokens = result.Tokens ?? Array.Empty<string>(),
                    Timestamps = result.Timestamps ?? Array.Empty<float>(),
                    Elapsed = sw.Elapsed,
                };
            }
            finally
            {
                _mutex.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _recognizer?.Dispose();
        }
        catch
        {
            // sherpa-onnx 的 Dispose 若失败，通常是双重释放；忽略
        }
        _recognizer = null;
        _mutex.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ParaformerEngine));
    }

    /// <summary>
    /// 根据配置目录解析模型和词表文件路径，接受几种常见命名。
    /// </summary>
    private static (string ModelPath, string TokensPath) ResolveModelPaths(string modelDir)
    {
        if (string.IsNullOrWhiteSpace(modelDir))
        {
            throw new DirectoryNotFoundException("AsrConfig.ModelDir is empty.");
        }

        var fullDir = Path.GetFullPath(modelDir);
        if (!Directory.Exists(fullDir))
        {
            throw new DirectoryNotFoundException(
                $"Model directory not found: {fullDir}. " +
                $"Please download the Paraformer ONNX model and extract it here.");
        }

        var candidates = new[] { "model.int8.onnx", "model.onnx" };
        string? modelPath = candidates
            .Select(n => Path.Combine(fullDir, n))
            .FirstOrDefault(File.Exists);
        if (modelPath is null)
        {
            throw new FileNotFoundException(
                $"No Paraformer model file (model.int8.onnx or model.onnx) in {fullDir}.");
        }

        var tokensPath = Path.Combine(fullDir, "tokens.txt");
        if (!File.Exists(tokensPath))
        {
            throw new FileNotFoundException($"tokens.txt not found in {fullDir}.");
        }

        return (modelPath, tokensPath);
    }
}
