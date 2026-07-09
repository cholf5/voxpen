namespace CapsWriterSharp.Core.Abstractions;

/// <summary>
/// 全局快捷键抽象。实现方负责挂钩系统级键盘/鼠标事件。
/// 事件在 hook 线程上触发，订阅方应尽快返回（可 fire-and-forget 到后台任务）。
/// </summary>
public interface IGlobalHotkey : IDisposable
{
    /// <summary>物理按键按下（不是操作系统的 toggle 状态）。</summary>
    event EventHandler<HotkeyEventArgs>? KeyPressed;

    /// <summary>物理按键松开。</summary>
    event EventHandler<HotkeyEventArgs>? KeyReleased;

    /// <summary>开始监听。可多次调用以更新绑定。</summary>
    void Start();

    /// <summary>停止监听（保留可 Start 恢复的能力）。</summary>
    void Stop();

    /// <summary>是否已启动。</summary>
    bool IsRunning { get; }
}

/// <summary>快捷键事件负载。</summary>
public sealed class HotkeyEventArgs : EventArgs
{
    /// <summary>抽象键名，例如 "caps_lock"、"f13"、"mouse_x2"。</summary>
    public required string Key { get; init; }

    /// <summary>事件发生的 UTC 时间戳（用于计算按住时长）。</summary>
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
