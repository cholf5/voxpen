using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
    private RecordingOverlayWindow? _overlay;
    private RecordingOverlayViewModel? _overlayVm;
    private string? _logFile;
    private readonly SemaphoreSlim _settingsApplyGate = new(1, 1);

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
            _viewModel = new MainWindowViewModel(_host, ApplySettingsAsync);

            // 可选文件日志：CAPSWRITER_LOG_FILE=path 时把 Emit 事件写入文件
            _logFile = Environment.GetEnvironmentVariable("CAPSWRITER_LOG_FILE");
            AttachFileLogger(_host);

            _mainWindow = new MainWindow { DataContext = _viewModel };
            desktop.MainWindow = _mainWindow;

            // 录音状态浮窗：屏幕正中偏下的小卡片，随音量波动。
            // 只在 Pipeline 进入 Recording 时显示，避免用户最小化主窗后不知道是否正在录音。
            _overlayVm = new RecordingOverlayViewModel();
            _overlay = new RecordingOverlayWindow();
            _overlay.AttachViewModel(_overlayVm);
            AttachOverlayEvents(_host);

            // 首次启动隐藏窗口，只留托盘图标
            _mainWindow.Show();
            _mainWindow.Hide();

            desktop.Exit += (_, _) =>
            {
                try { _overlay?.Close(); } catch { }
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

            // 启动前先做一次模型预校验：目录 / model.onnx / tokens.txt 缺任何一样，
            // 都立刻显示主窗 + 红色横幅 + Toast + 日志，指引下载。
            // 通过后再走后台加载模型 + 启动 hook 流程。
            var validation = _host.ValidateModelDirectory(_host.Config.Asr.Engine);
            if (!validation.IsValid)
            {
                var msg = ModelDownloadInfo.FormatMissingModelHint(_host.Config.Asr.ModelDir, validation.Message);
                _host.Emit(msg);
                _viewModel.SetStartupError(msg);
                _mainWindow.Show();
                _mainWindow.Activate();
                _ = _host.NotifyModelMissingAsync(_host.Config.Asr.ModelDir);
            }
            else
            {
                // 后台加载模型 + 启动 hook；失败弹提示，不阻塞 UI
                _ = LoadHostAsync();
            }
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

    private async Task ApplySettingsAsync(IReadOnlyList<string> shortcutKeys, AsrEngineKind asrEngine)
    {
        await _settingsApplyGate.WaitAsync();
        try
        {
            if (_host is null) throw new InvalidOperationException("应用尚未初始化。");

            var previous = _host;
            var previousShortcutKeys = previous.Config.Shortcut.Keys.Count > 0
                ? previous.Config.Shortcut.Keys.ToArray()
                : new[] { previous.Config.Shortcut.Key };
            var previousAsrEngine = previous.Config.Asr.Engine;
            previous.SaveSettings(shortcutKeys, asrEngine);
            var candidate = AppHost.Create(AppContext.BaseDirectory);
            var previousDisposed = false;
            try
            {
                await candidate.LoadModelsAsync();
                previous.Dispose();
                previousDisposed = true;
                candidate.StartListening();
                _host = candidate;
                _viewModel?.ReplaceHost(candidate);
                AttachFileLogger(candidate);
                AttachOverlayEvents(candidate);
            }
            catch
            {
                candidate.Dispose();
                if (previousDisposed)
                {
                    _host = null;
                }
                else
                {
                    previous.SaveSettings(previousShortcutKeys, previousAsrEngine);
                }
                throw;
            }
        }
        finally
        {
            _settingsApplyGate.Release();
        }
    }

    private void AttachFileLogger(AppHost host)
    {
        if (string.IsNullOrEmpty(_logFile)) return;
        host.LogEmitted += (_, line) =>
        {
            try { File.AppendAllText(_logFile, $"[{DateTime.Now:HH:mm:ss}] {line}\n"); } catch { }
        };
    }

    private void AttachOverlayEvents(AppHost host)
    {
        host.Pipeline.StateChanged += (_, state) => Dispatcher.UIThread.Post(() => ApplyOverlayState(state));
        host.Pipeline.AudioLevelSampled += (_, level) => Dispatcher.UIThread.Post(() => _overlayVm?.Push(level));
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

    /// <summary>
    /// Pipeline 状态变化时切换浮窗显隐：只在 Recording 时可见。
    /// 保持窗口实例存活，仅 Show/Hide，避免频繁重建带来的闪烁。
    /// </summary>
    private void ApplyOverlayState(PipelineState state)
    {
        if (_overlay is null) return;
        if (state == PipelineState.Recording)
        {
            if (!_overlay.IsVisible) _overlay.Show();
        }
        else
        {
            if (_overlay.IsVisible) _overlay.Hide();
            _overlayVm?.Reset();
        }
    }
}
