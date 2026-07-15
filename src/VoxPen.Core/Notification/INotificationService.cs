namespace VoxPen.Core.Notification;

/// <summary>通知类别；决定图标 / 声音 / 严重程度。</summary>
public enum NotificationKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// 通知服务抽象。由平台层（Windows Toast、mac NSUserNotification 等）实现，
/// 失败应静默降级到日志，不影响主流程。
/// </summary>
public interface INotificationService
{
    /// <summary>是否可用（首次调用可探测系统能力）。</summary>
    bool IsAvailable { get; }

    /// <summary>展示一条通知。<paramref name="body"/> 允许空串。</summary>
    Task ShowAsync(
        NotificationKind kind,
        string title,
        string body,
        CancellationToken cancellationToken = default);
}

/// <summary>不做任何事的空实现（Toast 关闭 / 非 Windows 平台的兜底）。</summary>
public sealed class NullNotificationService : INotificationService
{
    public static readonly NullNotificationService Instance = new();
    private NullNotificationService() { }
    public bool IsAvailable => false;
    public Task ShowAsync(NotificationKind kind, string title, string body,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
