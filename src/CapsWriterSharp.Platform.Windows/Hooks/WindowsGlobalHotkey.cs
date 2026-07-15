using CapsWriterSharp.Core.Abstractions;
using SharpHook;
using SharpHook.Data;

namespace CapsWriterSharp.Platform.Windows.Hooks;

/// <summary>
/// 基于 SharpHook 的全局键盘 + 鼠标钩子。
/// 支持一次绑定多个键（例如 CapsLock 与 侧键 X2 并存），任一命中即触发 <see cref="KeyPressed"/> / <see cref="KeyReleased"/>。
///
/// 约束（来自 SharpHook 文档）：
/// - <see cref="SimpleGlobalHook"/> 才支持在事件处理器中同步设置 <c>SuppressEvent</c>
/// - 处理器必须同步返回；重活丢到 Channel/Task 里由订阅方处理
/// - 单进程只允许一个 IGlobalHook 实例（libuiohook 是进程级单例）
/// </summary>
public sealed class WindowsGlobalHotkey : IGlobalHotkey
{
    private readonly SimpleGlobalHook _hook;
    private readonly Dictionary<KeyCode, string> _keyMap;
    private readonly Dictionary<MouseButton, string> _mouseMap;
    private readonly bool _suppress;
    private Thread? _runThread;
    private bool _started;
    private bool _disposed;

    public bool IsRunning => _started && !_disposed;

    public event EventHandler<HotkeyEventArgs>? KeyPressed;
    public event EventHandler<HotkeyEventArgs>? KeyReleased;

    /// <summary>单键构造（向后兼容）。</summary>
    public WindowsGlobalHotkey(string keyName, bool suppress = true)
        : this(new[] { keyName }, suppress) { }

    /// <param name="keyNames">
    /// 抽象键名列表，例如 <c>["caps_lock", "x2"]</c>。
    /// 支持 <c>caps_lock/f1..f24/num_lock/scroll_lock/space/enter/tab/esc</c>
    /// 以及鼠标侧键 <c>x1/x2</c>（Windows XButton1/XButton2）。
    /// 未识别的名称直接跳过并记录到调试输出。
    /// </param>
    /// <param name="suppress">是否抑制该键的系统默认行为。</param>
    public WindowsGlobalHotkey(IReadOnlyList<string> keyNames, bool suppress = true)
    {
        if (keyNames is null || keyNames.Count == 0)
            throw new ArgumentException("至少需要一个键名", nameof(keyNames));

        _suppress = suppress;
        _keyMap = new Dictionary<KeyCode, string>();
        _mouseMap = new Dictionary<MouseButton, string>();

        foreach (var raw in keyNames)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var name = raw.Trim().ToLowerInvariant();

            var mouse = KeyNameMapper.MapMouse(name);
            if (mouse.HasValue)
            {
                _mouseMap[mouse.Value] = name;
                continue;
            }

            var kb = KeyNameMapper.Map(name);
            if (kb.HasValue)
            {
                _keyMap[kb.Value] = name;
                continue;
            }

            System.Diagnostics.Debug.WriteLine($"[WindowsGlobalHotkey] 无法识别的键名: '{raw}'，已跳过");
        }

        if (_keyMap.Count == 0 && _mouseMap.Count == 0)
            throw new ArgumentException("没有任何可识别的键名", nameof(keyNames));

        // 需要同时监听键盘 + 鼠标时用 All；只有键盘/鼠标时按需精简，减小 hook 负担
        var hookType = (_keyMap.Count > 0, _mouseMap.Count > 0) switch
        {
            (true, true) => GlobalHookType.All,
            (true, false) => GlobalHookType.Keyboard,
            (false, true) => GlobalHookType.Mouse,
            _ => GlobalHookType.All,
        };
        _hook = new SimpleGlobalHook(hookType);

        if (_keyMap.Count > 0)
        {
            _hook.KeyPressed += OnKeyPressed;
            _hook.KeyReleased += OnKeyReleased;
        }
        if (_mouseMap.Count > 0)
        {
            _hook.MousePressed += OnMousePressed;
            _hook.MouseReleased += OnMouseReleased;
        }
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsGlobalHotkey));
        if (_started) return;

        // SimpleGlobalHook.Run() 是阻塞的（内部跑消息循环）。放到一个后台线程里跑。
        _runThread = new Thread(() =>
        {
            try { _hook.Run(); }
            catch { /* 停止或释放时 Run 会抛，忽略 */ }
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
        if (!_keyMap.TryGetValue(e.Data.KeyCode, out var name)) return;
        if (_suppress) e.SuppressEvent = true;
        SafeInvoke(KeyPressed, name);
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (!_keyMap.TryGetValue(e.Data.KeyCode, out var name)) return;
        if (_suppress) e.SuppressEvent = true;
        SafeInvoke(KeyReleased, name);
    }

    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        if (!_mouseMap.TryGetValue(e.Data.Button, out var name)) return;
        if (_suppress) e.SuppressEvent = true;
        SafeInvoke(KeyPressed, name);
    }

    private void OnMouseReleased(object? sender, MouseHookEventArgs e)
    {
        if (!_mouseMap.TryGetValue(e.Data.Button, out var name)) return;
        if (_suppress) e.SuppressEvent = true;
        SafeInvoke(KeyReleased, name);
    }

    private void SafeInvoke(EventHandler<HotkeyEventArgs>? handler, string keyName)
    {
        if (handler is null) return;
        try
        {
            handler.Invoke(this, new HotkeyEventArgs { Key = keyName });
        }
        catch
        {
            // 不能让异常穿透到原生回调
        }
    }
}

/// <summary>抽象键名 ↔ SharpHook KeyCode / MouseButton 双向映射。名称遵循原 CapsWriter 的 snake_case 风格。</summary>
internal static class KeyNameMapper
{
    public static KeyCode? Map(string name) => name switch
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

    /// <summary>鼠标侧键映射：Windows 上 XButton1 = SharpHook Button4，XButton2 = Button5。</summary>
    public static MouseButton? MapMouse(string name) => name switch
    {
        "x1" or "xbutton1" or "mouse_x1" => MouseButton.Button4,
        "x2" or "xbutton2" or "mouse_x2" => MouseButton.Button5,
        // 常规左中右也支持，方便调试；实际不建议用左键当快捷键
        "mouse_left" or "left_button" => MouseButton.Button1,
        "mouse_right" or "right_button" => MouseButton.Button2,
        "mouse_middle" or "middle_button" => MouseButton.Button3,
        _ => null,
    };
}
