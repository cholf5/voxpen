using VoxPen.Core.Transcribe;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoxPen.Platform.Windows.Audio;

/// <summary>
/// 基于 Windows Media Foundation 的通用音频解码器：
/// 支持 MP3 / M4A / AAC / WMA / MP4 等；输出 float32 mono @ <see cref="TargetSampleRate"/>。
/// WAV 文件走 <see cref="WavAudioDecoder"/> 兜底以规避 MF 依赖。
/// </summary>
public sealed class MediaFoundationAudioDecoder : IAudioDecoder
{
    /// <summary>转码后统一采样率（Paraformer 期望 16 kHz）。</summary>
    public const int TargetSampleRate = 16000;

    private static int _mfStarted;
    private static readonly HashSet<string> SupportedExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".m4a", ".aac", ".wma", ".mp4", ".flac", ".ogg", ".opus",
    };

    /// <summary>共享单例。</summary>
    public static readonly MediaFoundationAudioDecoder Instance = new();

    public bool CanDecode(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        return SupportedExts.Contains(Path.GetExtension(filePath));
    }

    public ValueTask<DecodedAudio> DecodeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(Path.GetExtension(filePath), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            // 直接走 WAV 解码（避免 MF 对某些非标准 WAV 的兼容性问题）
            return WavAudioDecoder.Instance.DecodeAsync(filePath, cancellationToken);
        }

        EnsureMediaFoundationStarted();
        var samples = DecodeToMonoFloat(filePath, TargetSampleRate, cancellationToken);
        return ValueTask.FromResult(new DecodedAudio(samples, TargetSampleRate));
    }

    private static void EnsureMediaFoundationStarted()
    {
        if (Interlocked.CompareExchange(ref _mfStarted, 1, 0) == 0)
        {
            MediaFoundationApi.Startup();
        }
    }

    private static float[] DecodeToMonoFloat(string filePath, int targetSampleRate, CancellationToken ct)
    {
        using var reader = new MediaFoundationReader(filePath);

        // 统一到 mono float32 @ targetSampleRate
        var monoSource = reader.WaveFormat.Channels == 1
            ? (IWaveProvider)reader
            : new StereoToMonoSampleProvider(reader.ToSampleProvider()).ToWaveProvider();

        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(targetSampleRate, 1);
        using var resampler = new MediaFoundationResampler(monoSource, targetFormat)
        {
            ResamplerQuality = 60,
        };

        var buffer = new byte[4 * targetSampleRate];   // 1 秒 float32
        using var ms = new MemoryStream();
        int read;
        while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            ms.Write(buffer, 0, read);
        }

        var bytes = ms.GetBuffer();
        var totalBytes = (int)ms.Length;
        var floatCount = totalBytes / 4;
        var floats = new float[floatCount];
        Buffer.BlockCopy(bytes, 0, floats, 0, floatCount * 4);
        return floats;
    }
}
