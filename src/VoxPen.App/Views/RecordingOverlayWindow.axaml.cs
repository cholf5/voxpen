using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using VoxPen.App.ViewModels;

namespace VoxPen.App.Views;

/// <summary>
/// 录音状态浮窗：屏幕正中偏下的小卡片，含麦克风图标 + 5 段声波条。
///
/// 设计要点：
/// - 无边框 / 顶层 / 不进任务栏 / 不可获取焦点 / 点击穿透（WS_EX_TRANSPARENT）
/// - 出现时定位到主屏幕水平居中、垂直 82% 位置
/// - 一个 30fps 的 UI 定时器负责让条自然衰减，Push 由 pipeline 音频块触发
///
/// 生命周期由 <see cref="App"/> 管辖：仅在 Pipeline 进入 Recording 时 Show()，
/// 其它状态一律 Hide()（保持实例，避免频繁重建窗口）。
/// </summary>
public partial class RecordingOverlayWindow : Window
{
    private readonly DispatcherTimer _decayTimer;
    private RecordingOverlayViewModel? _vm;

    public RecordingOverlayWindow()
    {
        InitializeComponent();

        _decayTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33), // ~30 fps
        };
        _decayTimer.Tick += (_, _) => _vm?.Tick();

        Opened += OnOpened;
        Closed += (_, _) => _decayTimer.Stop();
    }

    /// <summary>宿主传入 VM。约定在 Show() 前调用。</summary>
    public void AttachViewModel(RecordingOverlayViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        PositionAtBottomCenter();
        MakeClickThroughIfWindows();
        _decayTimer.Start();
    }

    /// <summary>依据主屏工作区尺寸把窗口贴到"水平居中 · 垂直 82%"。</summary>
    private void PositionAtBottomCenter()
    {
        var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen is null) return;

        var work = screen.WorkingArea; // 设备像素
        var scale = screen.Scaling;    // 每逻辑像素 -> 设备像素

        // Width/Height 是逻辑像素，转换成设备像素后计算位置
        var winW = (int)Math.Round(Width * scale);
        var winH = (int)Math.Round(Height * scale);

        var x = work.X + (work.Width - winW) / 2;
        var y = work.Y + (int)(work.Height * 0.82) - winH / 2;

        Position = new PixelPoint(x, y);
    }

    // --- Windows 点击穿透：加 WS_EX_TRANSPARENT | WS_EX_NOACTIVATE ---

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private void MakeClickThroughIfWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var handle = TryGetHwnd();
            if (handle == IntPtr.Zero) return;

            const uint AddFlags = WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED;

            if (IntPtr.Size == 8)
            {
                var style = (uint)GetWindowLongPtr64(handle, GWL_EXSTYLE).ToInt64();
                style |= AddFlags;
                SetWindowLongPtr64(handle, GWL_EXSTYLE, (IntPtr)style);
            }
            else
            {
                var style = (uint)GetWindowLong32(handle, GWL_EXSTYLE);
                style |= AddFlags;
                SetWindowLong32(handle, GWL_EXSTYLE, (int)style);
            }
        }
        catch
        {
            // 非致命：即便点不穿透，浮窗仍然显示，只是可点击
        }
    }

    private IntPtr TryGetHwnd()
    {
        // Avalonia 11/12：Window.TryGetPlatformHandle() -> IPlatformHandle
        var handle = this.TryGetPlatformHandle();
        return handle?.Handle ?? IntPtr.Zero;
    }
}
