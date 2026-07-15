using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using VoxPen.App.Services;
using VoxPen.App.ViewModels;
using VoxPen.App.Views;
using VoxPen.Core.Pipeline;
using VoxPen.Core.Config;

namespace VoxPen.App;

public partial class App : Application
{
    private AppHost? _host;
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 托盘常驻：即使没有可见窗口也不退出应用
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _host = AppHost.Create(AppContext.BaseDirectory);
            _viewModel = new MainWindowViewModel(_host);

            // 可选文件日志：CAPSWRITER_LOG_FILE=path 时把 Emit 事件写入文件
            var logFile = Environment.GetEnvironmentVariable("CAPSWRITER_LOG_FILE");
            if (!string.IsNullOrEmpty(logFile))
            {
                _host.LogEmitted += (_, line) =>
                {
                    try { File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {line}\n"); } catch { }
                };
            }

            _mainWindow = new MainWindow { DataContext = _viewModel };
            desktop.MainWindow = _mainWindow;

            // 首次启动隐藏窗口，只留托盘图标
            _mainWindow.Show();
            _mainWindow.Hide();

            desktop.Exit += (_, _) =>
            {
                try { _host?.Dispose(); } catch { }
            };

            // 冒烟测试用：CAPSWRITER_AUTO_EXIT_SECS=N 时 N 秒后自动退出
            var autoExit = Environment.GetEnvironmentVariable("CAPSWRITER_AUTO_EXIT_SECS");
            if (!string.IsNullOrEmpty(autoExit) && double.TryParse(autoExit, out var secs) && secs > 0)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(secs));
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
                            d.Shutdown();
                    });
                });
            }

            // 后台加载模型 + 启动 hook；失败弹提示，不阻塞 UI
            _ = LoadHostAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task LoadHostAsync()
    {
        if (_host is null) return;
        try
        {
            await _host.LoadAndStartAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] LoadAndStartAsync failed: {ex}");
            var message = StartupErrorFormatter.Format(ex);
            _host.Emit(message);
            _viewModel?.SetStartupError(message);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _mainWindow?.Show();
                _mainWindow?.Activate();
            });
        }
    }

    // ---------- TrayIcon 菜单事件 ----------

    private void OnTrayClicked(object? sender, EventArgs e) => ShowMainWindow();

    private void OnShowMainWindow(object? sender, EventArgs e) => ShowMainWindow();

    private void OnTogglePause(object? sender, EventArgs e)
    {
        if (_host is null) return;
        if (_host.Pipeline.State == PipelineState.Paused) _host.Pipeline.Resume();
        else _host.Pipeline.Pause();
    }

    private void OnOpenConfigFolder(object? sender, EventArgs e)
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] open config folder failed: {ex.Message}");
        }
    }

    private void OnAbout(object? sender, EventArgs e)
    {
        ShowMainWindow();
        // 未来可以打开独立的关于对话框；MVP 先高亮主窗口
    }

    private void OnExit(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ExitCommand.Execute(null);
            return;
        }
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        if (!_mainWindow.IsVisible) _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Activate();
    }
}
