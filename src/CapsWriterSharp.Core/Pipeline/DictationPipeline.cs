using System.Diagnostics;
using CapsWriterSharp.Core.Abstractions;
using CapsWriterSharp.Core.Config;
using CapsWriterSharp.Core.Notification;

namespace CapsWriterSharp.Core.Pipeline;

/// <summary>
/// 听写流水线：把全局快捷键、麦克风采集、ASR、上屏组合起来的状态机。
///
/// 状态迁移：
///   Idle ──KeyPressed──▶ Recording ──KeyReleased──▶ Recognizing ──▶ Outputting ──▶ Idle
///
/// 短按补发：
///   若 KeyReleased 距 KeyPressed 少于 <see cref="ShortcutConfig.ShortPressThresholdSeconds"/>，
///   视为误触；不做识别，并通过 <see cref="ShortPressDetected"/> 事件让平台层补发原按键。
///
/// 线程模型：
///   - hotkey / audio 回调都在原生线程上；本类内部所有状态变更都通过 <see cref="_stateLock"/> 串行
///   - ASR / 上屏由 Task 完成，避免阻塞 hook
/// </summary>
public sealed class DictationPipeline : IDisposable
{
    private readonly IGlobalHotkey _hotkey;
    private readonly IAudioCapture _capture;
    private readonly IAsrEngine _asr;
    private readonly ITextOutput _output;
    private readonly IForegroundApp? _foregroundApp;
    private readonly AppConfig _config;

    private readonly object _stateLock = new();
    private readonly List<float> _buffer = new(capacity: 16000 * 30);

    private PipelineState _state = PipelineState.Idle;
    private DateTime _pressedAtUtc;
    private bool _paused;
    private bool _disposed;

    public PipelineState State
    {
        get { lock (_stateLock) { return _state; } }
    }

    /// <summary>状态变更（可能在任意线程触发；UI 层订阅时请 marshal 到 UI 线程）。</summary>
    public event EventHandler<PipelineState>? StateChanged;

    /// <summary>短按误触发生。平台层应据此补发原按键（例如 CapsLock 用于切换大小写）。</summary>
    public event EventHandler? ShortPressDetected;

    /// <summary>识别产出（未经过后处理管线之前）。</summary>
    public event EventHandler<string>? TextRecognized;

    /// <summary>后处理管线钩子。默认恒等；P5 阶段接入 hot-rule / trash-punc / 前台应用策略。</summary>
    public Func<string, string> PostProcess { get; set; } = static s => s;

    /// <summary>上屏策略钩子；默认按配置执行。允许 P5 覆盖以按前台进程切换 Type/Paste。</summary>
    public Func<string, ITextOutput, Task>? OutputStrategy { get; set; }

    /// <summary>录音归档钩子；P5 阶段可挂载 AudioArchive.SaveAsync。异常会被吞掉。</summary>
    public Func<float[], string, Task>? ArchiveHandler { get; set; }

    /// <summary>
    /// 通知钩子；P7 阶段可挂载 <see cref="INotificationService"/> 或自定义回调。
    /// 触发时机由 <see cref="NotificationConfig"/> 控制：
    ///   录音开始（<c>Info</c>，可选）／识别完成（<c>Success</c>）／错误（<c>Error</c>）。
    /// 异常会被吞掉，不影响主流程。
    /// </summary>
    public Func<NotificationKind, string, string, Task>? NotificationHandler { get; set; }

    public DictationPipeline(
        IGlobalHotkey hotkey,
        IAudioCapture capture,
        IAsrEngine asr,
        ITextOutput output,
        AppConfig config,
        IForegroundApp? foregroundApp = null)
    {
        _hotkey = hotkey;
        _capture = capture;
        _asr = asr;
        _output = output;
        _config = config;
        _foregroundApp = foregroundApp;

        _hotkey.KeyPressed += OnKeyPressed;
        _hotkey.KeyReleased += OnKeyReleased;
        _capture.ChunkAvailable += OnAudioChunk;
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DictationPipeline));
        _hotkey.Start();
    }

    public void Pause()
    {
        lock (_stateLock)
        {
            _paused = true;
            if (_state == PipelineState.Recording)
            {
                _capture.Stop();
                _buffer.Clear();
            }
            TransitionNoLock(PipelineState.Paused);
        }
    }

    public void Resume()
    {
        lock (_stateLock)
        {
            _paused = false;
            TransitionNoLock(PipelineState.Idle);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _hotkey.KeyPressed -= OnKeyPressed;
        _hotkey.KeyReleased -= OnKeyReleased;
        _capture.ChunkAvailable -= OnAudioChunk;

        try { _capture.Stop(); } catch { }
    }

    // ---------- 事件处理 ----------

    private void OnKeyPressed(object? sender, HotkeyEventArgs e)
    {
        bool startedRecording = false;
        lock (_stateLock)
        {
            if (_disposed || _paused) return;
            if (_state != PipelineState.Idle && _state != PipelineState.Error) return;

            _pressedAtUtc = e.TimestampUtc;
            _buffer.Clear();
            try
            {
                _capture.Start();
                TransitionNoLock(PipelineState.Recording);
                startedRecording = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Pipeline] capture Start failed: {ex.Message}");
                TransitionNoLock(PipelineState.Error);
                _ = FireNotificationAsync(NotificationKind.Error, "CapsWriter", $"录音启动失败：{ex.Message}");
            }
        }

        if (startedRecording && _config.Notification.Enabled && _config.Notification.ShowOnRecordingStart)
        {
            _ = FireNotificationAsync(NotificationKind.Info, "CapsWriter", "开始录音…");
        }
    }

    private void OnKeyReleased(object? sender, HotkeyEventArgs e)
    {
        float[] snapshot;
        bool isShortPress;

        lock (_stateLock)
        {
            if (_state != PipelineState.Recording) return;

            _capture.Stop();
            var held = e.TimestampUtc - _pressedAtUtc;
            isShortPress = held.TotalSeconds < _config.Shortcut.ShortPressThresholdSeconds;

            if (isShortPress)
            {
                _buffer.Clear();
                TransitionNoLock(PipelineState.Idle);
                snapshot = Array.Empty<float>();
            }
            else
            {
                snapshot = _buffer.ToArray();
                _buffer.Clear();
                TransitionNoLock(PipelineState.Recognizing);
            }
        }

        if (isShortPress)
        {
            try { ShortPressDetected?.Invoke(this, EventArgs.Empty); } catch { }
            return;
        }

        // 音频过短保护（例如按键抖动，实际采集不足 0.25s）
        if (snapshot.Length < _capture.SampleRate / 4)
        {
            lock (_stateLock) { TransitionNoLock(PipelineState.Idle); }
            return;
        }

        // 异步跑推理 + 上屏，不阻塞 hook 线程
        _ = Task.Run(() => RecognizeAndOutputAsync(snapshot));
    }

    private void OnAudioChunk(object? sender, AudioChunkEventArgs e)
    {
        // 采集回调可能在原生线程；这里 lock 拷贝到 buffer，避免竞态
        lock (_stateLock)
        {
            if (_state == PipelineState.Recording)
            {
                _buffer.AddRange(e.Samples);
            }
        }
    }

    // ---------- 推理 + 上屏 ----------

    private async Task RecognizeAndOutputAsync(float[] samples)
    {
        try
        {
            var result = await _asr.RecognizeAsync(samples).ConfigureAwait(false);
            var raw = result.Text ?? string.Empty;

            string finalText;
            try
            {
                finalText = PostProcess(raw);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Pipeline] PostProcess threw: {ex.Message}; using raw text.");
                finalText = raw;
            }

            try { TextRecognized?.Invoke(this, finalText); } catch { }

            if (!string.IsNullOrEmpty(finalText))
            {
                lock (_stateLock) { TransitionNoLock(PipelineState.Outputting); }
                await ExecuteOutputAsync(finalText).ConfigureAwait(false);
            }

            var archive = ArchiveHandler;
            if (archive is not null)
            {
                try { await archive(samples, finalText).ConfigureAwait(false); }
                catch (Exception ex) { Debug.WriteLine($"[Pipeline] archive failed: {ex.Message}"); }
            }

            lock (_stateLock) { TransitionNoLock(PipelineState.Idle); }

            if (_config.Notification.Enabled && _config.Notification.ShowOnResult
                && !string.IsNullOrEmpty(finalText))
            {
                await FireNotificationAsync(NotificationKind.Success, "识别完成", finalText)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Pipeline] recognize/output failed: {ex}");
            lock (_stateLock) { TransitionNoLock(PipelineState.Error); }

            if (_config.Notification.Enabled && _config.Notification.ShowOnError)
            {
                await FireNotificationAsync(NotificationKind.Error, "识别失败", ex.Message)
                    .ConfigureAwait(false);
            }
        }
    }

    private Task FireNotificationAsync(NotificationKind kind, string title, string body)
    {
        var handler = NotificationHandler;
        if (handler is null) return Task.CompletedTask;
        try
        {
            return handler(kind, title, body);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Pipeline] notification handler threw: {ex.Message}");
            return Task.CompletedTask;
        }
    }

    private Task ExecuteOutputAsync(string text)
    {
        if (OutputStrategy is not null)
        {
            return OutputStrategy(text, _output);
        }

        // 默认策略：按配置 Mode + PasteApps 列表决定
        var mode = _config.Output.Mode;
        var exe = _foregroundApp?.GetForegroundExeName();
        if (exe is not null &&
            _config.Output.PasteApps.Any(name => string.Equals(name, exe, StringComparison.OrdinalIgnoreCase)))
        {
            mode = OutputMode.Paste;
        }

        return mode == OutputMode.Paste
            ? _output.PasteAsync(text, _config.Output.RestoreClipboard)
            : _output.TypeAsync(text);
    }

    // ---------- 状态迁移（必须在 _stateLock 内调用） ----------

    private void TransitionNoLock(PipelineState next)
    {
        if (_state == next) return;
        _state = next;
        // 事件发射放到锁外，避免死锁；但这里为简单起见先同步发；订阅方需自律。
        try { StateChanged?.Invoke(this, next); } catch { }
    }
}
