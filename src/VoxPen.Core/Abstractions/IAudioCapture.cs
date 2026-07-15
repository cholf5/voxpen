namespace VoxPen.Core.Abstractions;

/// <summary>
/// 麦克风采集抽象。规定输出为 16kHz float32 mono，值域 [-1, 1]。
/// 实现方通过原生库回调塞入音频块；订阅方尽快消费，避免阻塞采集线程。
/// </summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>期望的采样率（Hz）。当前 pipeline 固定 16000。</summary>
    int SampleRate { get; }

    /// <summary>是否正在录音。</summary>
    bool IsRecording { get; }

    /// <summary>音频块到达。data 长度不固定，取决于底层缓冲区大小。</summary>
    event EventHandler<AudioChunkEventArgs>? ChunkAvailable;

    /// <summary>开始采集。可多次调用（幂等）。</summary>
    void Start();

    /// <summary>停止采集。</summary>
    void Stop();

    /// <summary>列出可用的输入设备名称，用于设置界面选择。</summary>
    IReadOnlyList<string> ListInputDevices();
}

/// <summary>音频块事件负载。</summary>
public sealed class AudioChunkEventArgs : EventArgs
{
    public required float[] Samples { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
