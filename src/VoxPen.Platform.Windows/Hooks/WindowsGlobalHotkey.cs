using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;
using SharpHook;
using SharpHook.Data;

namespace VoxPen.Platform.Windows.Hooks;

/// <summary>
/// 基于 SharpHook 的全局键盘 + 鼠标钩子。
/// 支持一次绑定多个键（例如 Ctrl + Shift + A）；只有全部按下时才触发 <see cref="KeyPressed"/>。
///
/// 约束（来自 SharpHook 文档）：
/// - <see cref="SimpleGlobalHook"/> 才支持在事件处理器中同步设置 <c>SuppressEvent</c>
/// - 处理器必须同步返回；重活丢到 Channel/Task 里由订阅方处理
/// - 单进程只允许一个 IGlobalHook 实例（libuiohook 是进程级单例）
///
/// 自捕获防护：
/// - Windows 上 libuiohook 会通过 <c>LLKHF_INJECTED</c> 标记合成事件，SharpHook 透出为
///   <see cref="HookEventArgs.IsEventSimulated"/>。任何合成事件（含我们自己 <c>SendInput</c>
///   补发的 CapsLock、或其它进程的 SendInput）都直接放行给系统，不进入快捷键流水线，
///   也不会被抑制。这一层足以取代早期基于计数器的自捕获登记方案。
/// </summary>
public sealed class WindowsGlobalHotkey : IGlobalHotkey
{
    private readonly SimpleGlobalHook _hook;
    private readonly HashSet<string> _configuredKeys;
    private readonly ShortcutChord _chord;
    private readonly bool _suppress;
    private Thread? _runThread;
    private bool _started;
    private bool _disposed;

    public bool IsRunning => _started && !_disposed;

    public event EventHandler<HotkeyEventArgs>? KeyPressed;
    public event EventHandler<HotkeyEventArgs>? KeyReleased;

    /// <summary>所有可规范化的物理键事件；仅供设置页录制快捷键，不会改变系统输入。</summary>
    public event EventHandler<HotkeyObservedEventArgs>? KeyObserved;

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
        _configuredKeys = keyNames
            .Where(raw => !string.IsNullOrWhiteSpace(raw))
            .Select(raw => raw.Trim().ToLowerInvariant())
            .Where(KeyNameMapper.IsMapped)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_configuredKeys.Count == 0)
            throw new ArgumentException("没有任何可识别的键名", nameof(keyNames));
        _chord = new ShortcutChord(_configuredKeys);

        // 设置页录制需要观察任意键，因此统一监听键盘 + 鼠标。
        _hook = new SimpleGlobalHook(GlobalHookType.All);
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        _hook.MousePressed += OnMousePressed;
        _hook.MouseReleased += OnMouseReleased;
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
        if (e.IsEventSimulated) return;
        HandlePressed(KeyNameMapper.GetName(e.Data.KeyCode), suppress => e.SuppressEvent = suppress);
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (e.IsEventSimulated) return;
        HandleReleased(KeyNameMapper.GetName(e.Data.KeyCode), suppress => e.SuppressEvent = suppress);
    }

    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        if (e.IsEventSimulated) return;
        HandlePressed(KeyNameMapper.GetMouseName(e.Data.Button), suppress => e.SuppressEvent = suppress);
    }

    private void OnMouseReleased(object? sender, MouseHookEventArgs e)
    {
        if (e.IsEventSimulated) return;
        HandleReleased(KeyNameMapper.GetMouseName(e.Data.Button), suppress => e.SuppressEvent = suppress);
    }

    private void HandlePressed(string? name, Action<bool> suppress)
    {
        if (name is null) return;
        SafeObserve(name, true);
        if (_chord.Press(name))
        {
            if (_suppress) suppress(true);
            SafeInvoke(KeyPressed, name);
        }
    }

    private void HandleReleased(string? name, Action<bool> suppress)
    {
        if (name is null) return;
        SafeObserve(name, false);
        if (_chord.Release(name))
        {
            if (_suppress) suppress(true);
            SafeInvoke(KeyReleased, name);
        }
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

    private void SafeObserve(string keyName, bool isPressed)
    {
        try { KeyObserved?.Invoke(this, new HotkeyObservedEventArgs(keyName, isPressed)); } catch { }
    }
}

/// <summary>抽象键名 ↔ SharpHook KeyCode / MouseButton 双向映射。名称遵循原 CapsWriter 的 snake_case 风格。</summary>
public static class KeyNameMapper
{
    private static readonly Dictionary<string, KeyCode> KeyboardKeys = Enum.GetValues<KeyCode>()
        .Select(code => (Code: code, Name: GetName(code)))
        .Where(pair => pair.Name is not null)
        .ToDictionary(pair => pair.Name!, pair => pair.Code, StringComparer.OrdinalIgnoreCase);

    public static KeyCode? Map(string name) => KeyboardKeys.TryGetValue(NormalizeAlias(name), out var key) ? key : null;

    public static bool IsMapped(string name) => Map(name).HasValue || MapMouse(name).HasValue;

    public static string? GetName(KeyCode key) => NormalizeName(key.ToString());

    public static string? GetMouseName(MouseButton button) => button switch
    {
        MouseButton.Button1 => "mouse_left",
        MouseButton.Button2 => "mouse_right",
        MouseButton.Button3 => "mouse_middle",
        MouseButton.Button4 => "x1",
        MouseButton.Button5 => "x2",
        _ => null,
    };

    private static string? NormalizeName(string enumName)
    {
        if (!enumName.StartsWith("Vc", StringComparison.Ordinal) || enumName == "VcUndefined") return null;
        var name = enumName[2..];
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (var index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index]) && !char.IsUpper(name[index - 1])) builder.Append('_');
            builder.Append(char.ToLowerInvariant(name[index]));
        }
        return NormalizeAlias(builder.ToString());
    }

    private static string NormalizeAlias(string name) => name.Trim().ToLowerInvariant() switch
    {
        "capslock" => "caps_lock",
        "numlock" => "num_lock",
        "scrolllock" => "scroll_lock",
        "escape" => "esc",
        "left_control" => "left_ctrl",
        "right_control" => "right_ctrl",
        _ => name.Trim().ToLowerInvariant(),
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
