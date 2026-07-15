using VoxPen.Core.Notification;
using Microsoft.Toolkit.Uwp.Notifications;

namespace VoxPen.Platform.Windows.Notifications;

/// <summary>
/// Windows 10/11 原生 Toast 通知实现。
/// 用 <see cref="ToastContentBuilder"/> 构建；首次调用 <see cref="ToastNotificationManagerCompat"/>
/// 会自动写入 Start Menu 快捷方式 + AUMID，无需手工注册。
/// 任何失败静默降级到调试输出，避免影响主流程。
/// </summary>
public sealed class WindowsToastNotifier : INotificationService
{
    private int _available = -1;   // -1=未探测, 0=不可用, 1=可用

    public bool IsAvailable
    {
        get
        {
            if (_available >= 0) return _available == 1;
            try
            {
                // 只要能拿到 Notifier 就视为可用（COM 已注册）
                _ = ToastNotificationManagerCompat.CreateToastNotifier();
                _available = 1;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[WindowsToastNotifier] Toast 不可用: {ex.GetType().Name}: {ex.Message}");
                _available = 0;
                return false;
            }
        }
    }

    public Task ShowAsync(
        NotificationKind kind,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable) return Task.CompletedTask;

        try
        {
            var builder = new ToastContentBuilder()
                .AddText(title ?? string.Empty);
            if (!string.IsNullOrEmpty(body))
            {
                builder.AddText(body);
            }
            // ToastContentBuilder 无“级别”原语，用 attribution 携带类型信息
            builder.AddAttributionText(FormatKind(kind));
            builder.Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[WindowsToastNotifier] Show 失败: {ex.GetType().Name}: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static string FormatKind(NotificationKind kind) => kind switch
    {
        NotificationKind.Success => "声写 · Success",
        NotificationKind.Warning => "声写 · Warning",
        NotificationKind.Error => "声写 · Error",
        _ => "声写",
    };
}
