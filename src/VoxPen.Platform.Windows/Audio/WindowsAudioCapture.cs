using System.Runtime.InteropServices;
using VoxPen.Core.Abstractions;
using PortAudioSharp;
using PaStream = PortAudioSharp.Stream;

namespace VoxPen.Platform.Windows.Audio;

/// <summary>
/// ���� PortAudioSharp2 ����˷�ɼ�������̶�Ϊ 16 kHz float32 ��������
///
/// ��������Լ����
/// - �״�ʵ�������� PortAudio.Initialize�����̼��������һ��ʵ�� Dispose ʱ Terminate��
/// - �ص������� PortAudio ��ԭ�������ȼ��߳��ϣ������ڻص���ֻ�� memcpy + raise �¼���
///   ���ķ����뾡�췵�أ��Ƽ� fire-and-forget �� Channel / ���У���
/// </summary>
public sealed class WindowsAudioCapture : IAudioCapture
{
    private const int TargetSampleRate = 16000;
    private const uint FramesPerBuffer = 320;   // 20 ms @ 16 kHz

    // ���̼� PortAudio ���ü���
    private static readonly object HostLock = new();
    private static int _hostRefCount;

    private readonly int _deviceIndex;
    private readonly object _stateLock = new();

    // �����ֶη�ֹ delegate �� GC
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
    /// null ��ƥ��ʧ��ʱʹ��ϵͳĬ�������豸��ƥ����ô�Сд�����е� Contains��
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

            // ÿ�� Start ���½�һ�� stream��PortAudio �� stream �����Ϊ���� Start/Stop
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

            // ���� delegate ���÷�ֹ GC��PortAudio �ص���ԭ�����봥����
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
                // ���豸�ѱ��γ���ԭ�� Stop ʧ�ܣ����ԣ������������
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

    // ---------- �ڲ�ʵ�� ----------

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
            // ���ķ���Ӧ���쳣����ʹ����Ҳ������ԭ���ص�ջ��ð��
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
