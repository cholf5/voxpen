namespace CapsWriterSharp.Core.Transcribe;

/// <summary>解码结果：float32 单声道 PCM（<c>[-1, 1]</c>）+ 采样率。</summary>
public readonly record struct DecodedAudio(float[] Samples, int SampleRate)
{
    /// <summary>音频总时长（秒）。</summary>
    public double DurationSeconds =>
        SampleRate > 0 ? (double)Samples.Length / SampleRate : 0.0;
}

/// <summary>
/// 音频解码器抽象：把任意文件（WAV / MP3 / M4A ...）读成 float32 mono PCM。
/// 由平台层实现（Windows 用 MediaFoundation，Core 内置 WAV 兜底）。
/// </summary>
public interface IAudioDecoder
{
    /// <summary>是否能解码 <paramref name="filePath"/>（按扩展名/魔数快速判断）。</summary>
    bool CanDecode(string filePath);

    /// <summary>把音频解码为 float32 mono PCM；失败抛异常。</summary>
    ValueTask<DecodedAudio> DecodeAsync(string filePath, CancellationToken cancellationToken = default);
}
