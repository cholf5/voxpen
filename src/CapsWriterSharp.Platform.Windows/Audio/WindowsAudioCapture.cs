using System.Runtime.InteropServices;
using CapsWriterSharp.Core.Abstractions;
using PortAudioSharp;
using PaStream = PortAudioSharp.Stream;

namespace CapsWriterSharp.Platform.Windows.Audio;

/// <summary>
/// 基于 PortAudioSharp2 的麦克风采集。输出固定为 16 kHz float32 单声道。
///
/// 生命周期约定：
/// - 首次实例化触发 PortAudio.Initialize（进程级），最后一个实例 Dispose 时 Terminate。
/// - 回调运行在 PortAudio 的原生高优先级线程上；本类在回调内只做 memcpy + raise 事件，
///   订阅方必须尽快返回（推荐 fire-and-forget 到 Channel / 队列）。
/// </summary>
public sealed class WindowsAudioCapture : IAudioCapture
{
    private const int TargetSampleRate = 16000;
    private const uint FramesPerBuffer = 320;   // 20 ms @ 16 kHz

    // 进程级 PortAudio 引用计数
    private static readonly object HostLock = new();
    private static int _hostRefCount;

    private readonly int _deviceIndex;
    private readonly object _stateLock = new();

    // 保留字段防止 delegate 被 GC
    private PaStream.Callback? _callback;
    private PaStream? _stream;
    private bool _hostInitialized;
    private bool _disposed;

    public int SampleRate => TargetSampleRate;

    public bool IsRecording
    {
        get
        {
            lock (_stateLock)
            {
                return _stream is { IsActive: true };
            }
        }
    }

    public event EventHandler<AudioChunkEventArgs>? ChunkAvailable;

    /// <param name="preferredDeviceName">
    /// null 或匹配失败时使用系统默认输入设备。匹配采用大小写不敏感的 Contains。
    /// </param>
    public WindowsAudioCapture(string? preferredDeviceName = null)
    {
        EnsureHostInitialized();
        _hostInitialized = true;
        _deviceIndex = ResolveDeviceIndex(preferredDeviceName);
        if (_deviceIndex < 0)
        {
            throw new InvalidOperationException(
                "No input device available. Check microphone permission and drivers.");
        }
    }

    public void Start()
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();
            if (_stream is { IsActive: true }) return;

            // 每次 Start 都新建一个 stream；PortAudio 的 stream 不设计为反复 Start/Stop
            DisposeStreamNoLock();

            var deviceInfo = PortAudio.GetDeviceInfo(_deviceIndex);
            var parameters = new StreamParameters
            {
                device = _deviceIndex,
                channelCount = 1,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = deviceInfo.defaultLowInputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero,
            };

            // 保存 delegate 引用防止 GC（PortAudio 回调从原生代码触发）
            _callback = OnAudioCallback;

            _stream = new PaStream(
                inParams: parameters,
                outParams: null,
                sampleRate: TargetSampleRate,
                framesPerBuffer: FramesPerBuffer,
                streamFlags: StreamFlags.ClipOff,
                callback: _callback,
                userData: IntPtr.Zero);

            _stream.Start();
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (_stream is null) return;
            try
            {
                if (_stream.IsActive) _stream.Stop();
            }
            catch (PortAudioException)
            {
                // 若设备已被拔出等原因 Stop 失败，忽略；下面会做清理
            }
            DisposeStreamNoLock();
        }
    }

    public IReadOnlyList<string> ListInputDevices()
    {
        EnsureHostInitialized();
        var list = new List<string>();
        var count = PortAudio.DeviceCount;
        for (int i = 0; i < count; i++)
        {
            var info = PortAudio.GetDeviceInfo(i);
            if (info.maxInputChannels > 0)
            {
                list.Add(info.name);
            }
        }
        return list;
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
            DisposeStreamNoLock();
            _callback = null;
        }

        if (_hostInitialized)
        {
            _hostInitialized = false;
            ReleaseHost();
        }
    }

    // ---------- 内部实现 ----------

    private StreamCallbackResult OnAudioCallback(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData)
    {
        if (input == IntPtr.Zero || frameCount == 0)
        {
            return StreamCallbackResult.Continue;
        }

        var samples = new float[frameCount];
        Marshal.Copy(input, samples, 0, (int)frameCount);

        try
        {
            ChunkAvailable?.Invoke(this, new AudioChunkEventArgs { Samples = samples });
        }
        catch
        {
            // 订阅方不应抛异常；即使抛了也不能让原生回调栈上冒泡
        }

        return StreamCallbackResult.Continue;
    }

    private void DisposeStreamNoLock()
    {
        if (_stream is null) return;
        try { _stream.Close(); } catch (PortAudioException) { }
        try { _stream.Dispose(); } catch (PortAudioException) { }
        _stream = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsAudioCapture));
    }

    private static int ResolveDeviceIndex(string? preferredName)
    {
        var count = PortAudio.DeviceCount;
        if (count <= 0) return -1;

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            for (int i = 0; i < count; i++)
            {
                var info = PortAudio.GetDeviceInfo(i);
                if (info.maxInputChannels > 0 &&
                    info.name.Contains(preferredName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        var def = PortAudio.DefaultInputDevice;
        if (def >= 0 && PortAudio.GetDeviceInfo(def).maxInputChannels > 0)
        {
            return def;
        }

        for (int i = 0; i < count; i++)
        {
            if (PortAudio.GetDeviceInfo(i).maxInputChannels > 0) return i;
        }
        return -1;
    }

    private static void EnsureHostInitialized()
    {
        lock (HostLock)
        {
            if (_hostRefCount == 0)
            {
                PortAudio.LoadNativeLibrary();
                PortAudio.Initialize();
            }
            _hostRefCount++;
        }
    }

    private static void ReleaseHost()
    {
        lock (HostLock)
        {
            if (_hostRefCount == 0) return;
            _hostRefCount--;
            if (_hostRefCount == 0)
            {
                try { PortAudio.Terminate(); } catch { }
            }
        }
    }
}
