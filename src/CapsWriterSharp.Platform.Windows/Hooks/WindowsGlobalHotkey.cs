using CapsWriterSharp.Core.Abstractions;
using SharpHook;
using SharpHook.Data;

namespace CapsWriterSharp.Platform.Windows.Hooks;

/// <summary>
/// 基于 SharpHook 的全局键盘钩子，Windows 上直接抑制目标键的默认行为（例如 CapsLock 不再切换大小写）。
///
/// 约束（来自 SharpHook 文档）：
/// - <see cref="SimpleGlobalHook"/> 才支持在事件处理器中同步设置 SuppressEvent
/// - 处理器必须同步返回；重活丢到 Channel/Task 里由订阅方处理
/// - 单进程只允许一个 IGlobalHook 实例（libuiohook 是进程级单例）
/// </summary>
public sealed class WindowsGlobalHotkey : IGlobalHotkey
{
    private readonly SimpleGlobalHook _hook;
    private readonly HashSet<KeyCode> _watched;
    private readonly bool _suppress;
    private Thread? _runThread;
    private bool _started;
    private bool _disposed;

    public bool IsRunning => _started && !_disposed;

    public event EventHandler<HotkeyEventArgs>? KeyPressed;
    public event EventHandler<HotkeyEventArgs>? KeyReleased;

    /// <param name="keyName">抽象键名（例如 "caps_lock"、"f13"）。</param>
    /// <param name="suppress">是否抑制该键的系统默认行为。</param>
    public WindowsGlobalHotkey(string keyName, bool suppress = true)
    {
        _suppress = suppress;

        // SimpleGlobalHook 才能同步抑制事件
        _hook = new SimpleGlobalHook(GlobalHookType.Keyboard);

        _watched = new HashSet<KeyCode>();
        var mapped = KeyNameMapper.Map(keyName);
        if (mapped is null)
        {
            throw new ArgumentException($"Unsupported key name: {keyName}", nameof(keyName));
        }
        _watched.Add(mapped.Value);

        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsGlobalHotkey));
        if (_started) return;

        // SimpleGlobalHook.Run() 是阻塞的（内部跑消息循环）。放到一个后台线程里跑。
        _runThread = new Thread(() =>
        {
            try
            {
                _hook.Run();
            }
            catch
            {
                // 停止或释放时 Run 会抛，忽略
            }
        })
        {
            IsBackground = true,
            Name = "SharpHook-GlobalHook",
        };
        _runThread.Start();
        _started = true;
    }

    public void Stop()
    {
        if (!_started) return;
        try { _hook.Dispose(); } catch { }
        _started = false;
        _runThread = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (!_watched.Contains(e.Data.KeyCode)) return;

        if (_suppress) e.SuppressEvent = true;

        try
        {
            KeyPressed?.Invoke(this, new HotkeyEventArgs { Key = KeyCodeToName(e.Data.KeyCode) });
        }
        catch
        {
            // 不能让异常穿透到原生回调
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (!_watched.Contains(e.Data.KeyCode)) return;

        if (_suppress) e.SuppressEvent = true;

        try
        {
            KeyReleased?.Invoke(this, new HotkeyEventArgs { Key = KeyCodeToName(e.Data.KeyCode) });
        }
        catch
        {
        }
    }

    private static string KeyCodeToName(KeyCode code) => code.ToString();
}

/// <summary>
/// 抽象键名 → SharpHook KeyCode。名称遵循原 CapsWriter 的字符串风格（snake_case）。
/// </summary>
internal static class KeyNameMapper
{
    public static KeyCode? Map(string name) => name.ToLowerInvariant() switch
    {
        "caps_lock" or "capslock" => KeyCode.VcCapsLock,
        "num_lock" or "numlock" => KeyCode.VcNumLock,
        "scroll_lock" or "scrolllock" => KeyCode.VcScrollLock,
        "f1" => KeyCode.VcF1,
        "f2" => KeyCode.VcF2,
        "f3" => KeyCode.VcF3,
        "f4" => KeyCode.VcF4,
        "f5" => KeyCode.VcF5,
        "f6" => KeyCode.VcF6,
        "f7" => KeyCode.VcF7,
        "f8" => KeyCode.VcF8,
        "f9" => KeyCode.VcF9,
        "f10" => KeyCode.VcF10,
        "f11" => KeyCode.VcF11,
        "f12" => KeyCode.VcF12,
        "f13" => KeyCode.VcF13,
        "f14" => KeyCode.VcF14,
        "f15" => KeyCode.VcF15,
        "space" => KeyCode.VcSpace,
        "enter" => KeyCode.VcEnter,
        "tab" => KeyCode.VcTab,
        "esc" or "escape" => KeyCode.VcEscape,
        _ => null,
    };
}
