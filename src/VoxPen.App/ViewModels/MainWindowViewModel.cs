using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using VoxPen.App.Services;
using VoxPen.Core.Config;
using VoxPen.Core.Pipeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VoxPen.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private const int MaxHistory = 50;
    private const int MaxLogChars = 20_000;

    private readonly AppHost? _host;
    private readonly AppConfig _config;
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _modelCheckCancellation;
    private CancellationTokenSource? _punctModelCheckCancellation;

    [ObservableProperty] private string _stateLabel = "就绪";
    [ObservableProperty] private IBrush _stateBrush = Brushes.Gray;
    [ObservableProperty] private string _shortcutHint = "长按 CapsLock 说话，松开上屏";
    [ObservableProperty] private string _modelDir = "models/paraformer";
    [ObservableProperty] private string _modelStatusIcon = "❌";
    [ObservableProperty] private string _modelStatusText = "正在检测模型…";
    [ObservableProperty] private string _modelDownloadHint = "";
    [ObservableProperty] private string _modelSaveStatus = "";
    [ObservableProperty] private string _punctModelDir =
        "models/Punct-CT-Transformer/sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12";
    [ObservableProperty] private string _punctModelStatusIcon = "…";
    [ObservableProperty] private string _punctModelStatusText = "正在检测标点模型…";
    [ObservableProperty] private string _punctModelDownloadHint = "";
    [ObservableProperty] private string _punctModelSaveStatus = "";
    [ObservableProperty] private string _outputModeLabel = "模拟打字";
    [ObservableProperty] private string _pasteAppsLabel = "";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _startupError = "";
    [ObservableProperty] private ShortcutOption _selectedShortcut = ShortcutSettings.Options[0];
    [ObservableProperty] private string _shortcutSaveStatus = "";
    [ObservableProperty] private bool _isPaused;

    public bool HasStartupError => !string.IsNullOrEmpty(StartupError);

    public bool HasModelDownloadHint => !string.IsNullOrEmpty(ModelDownloadHint);

    public bool HasPunctModelDownloadHint => !string.IsNullOrEmpty(PunctModelDownloadHint);

    public bool IsExiting { get; private set; }

    public ObservableCollection<HistoryEntry> History { get; } = new();
    public IReadOnlyList<ShortcutOption> ShortcutOptions => ShortcutSettings.Options;

    /// <summary>设计时无参构造，供 Avalonia XAML 预览器使用。</summary>
    public MainWindowViewModel()
    {
        _config = new AppConfig();
        RefreshConfigLabels();
    }

    public MainWindowViewModel(AppHost host)
    {
        _host = host;
        _config = host.Config;
        RefreshConfigLabels();

        host.Pipeline.StateChanged += (_, s) => Dispatcher.UIThread.Post(() => ApplyState(s));
        host.Pipeline.TextRecognized += (_, text) => Dispatcher.UIThread.Post(() => AppendHistory(text));
        host.LogEmitted += (_, line) => Dispatcher.UIThread.Post(() => AppendLog(line));

        ApplyState(host.Pipeline.State);
    }

    private void RefreshConfigLabels()
    {
        ModelDir = _config.Asr.ModelDir;
        PunctModelDir = _config.Punctuation.ModelDir;
        var configuredKey = _config.Shortcut.Keys.FirstOrDefault() ?? _config.Shortcut.Key;
        SelectedShortcut = ShortcutSettings.Options.FirstOrDefault(option =>
            string.Equals(option.Key, configuredKey, StringComparison.OrdinalIgnoreCase))
            ?? ShortcutSettings.Options[0];
        ShortcutHint = $"长按 {SelectedShortcut.DisplayName} 说话，松开上屏";
        OutputModeLabel = _config.Output.Mode == OutputMode.Paste ? "剪贴板粘贴" : "模拟打字";
        PasteAppsLabel = _config.Output.PasteApps.Count == 0
            ? "（无）"
            : string.Join(", ", _config.Output.PasteApps);
    }

    partial void OnModelDirChanged(string value) => _ = DetectModelAsync(value);

    partial void OnPunctModelDirChanged(string value) => _ = DetectPunctModelAsync(value);

    private async Task DetectModelAsync(string modelDir)
    {
        _modelCheckCancellation?.Cancel();
        _modelCheckCancellation?.Dispose();
        _modelCheckCancellation = new CancellationTokenSource();
        var cancellationToken = _modelCheckCancellation.Token;

        ModelStatusIcon = "…";
        ModelStatusText = "正在检测模型…";
        try
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            var result = _host is null
                ? ModelDirectoryValidator.Validate(modelDir)
                : _host.ValidateModelDirectory(modelDir);
            if (cancellationToken.IsCancellationRequested) return;

            ModelStatusIcon = result.IsValid ? "✅" : "❌";
            ModelStatusText = result.IsValid
                ? (_host?.IsModelLoadedFor(modelDir) == true ? "模型已加载" : "模型文件完整，重启后生效")
                : result.Message;
            ModelDownloadHint = result.IsValid
                ? ""
                : $"请下载 {ModelDownloadInfo.PackageName}：{ModelDownloadInfo.DownloadUrl}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ModelStatusIcon = "❌";
            ModelStatusText = $"检测失败：{ex.Message}";
            ModelDownloadHint = $"请下载 {ModelDownloadInfo.PackageName}：{ModelDownloadInfo.DownloadUrl}";
        }
    }

    private async Task DetectPunctModelAsync(string modelDir)
    {
        _punctModelCheckCancellation?.Cancel();
        _punctModelCheckCancellation?.Dispose();
        _punctModelCheckCancellation = new CancellationTokenSource();
        var cancellationToken = _punctModelCheckCancellation.Token;

        PunctModelStatusIcon = "…";
        PunctModelStatusText = "正在检测标点模型…";
        try
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            var result = _host is null
                ? PunctuationModelValidator.Validate(modelDir)
                : _host.ValidatePunctuationDirectory(modelDir);
            if (cancellationToken.IsCancellationRequested) return;

            PunctModelStatusIcon = result.IsValid ? "✅" : "❌";
            PunctModelStatusText = result.IsValid
                ? (_host?.IsPunctuationLoadedFor(modelDir) == true
                    ? "标点模型已加载"
                    : "标点模型文件完整，重启后生效")
                : result.Message;
            PunctModelDownloadHint = result.IsValid
                ? ""
                : $"请下载 {PunctuationModelDownloadInfo.PackageName}：{PunctuationModelDownloadInfo.DownloadUrl}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            PunctModelStatusIcon = "❌";
            PunctModelStatusText = $"检测失败：{ex.Message}";
            PunctModelDownloadHint =
                $"请下载 {PunctuationModelDownloadInfo.PackageName}：{PunctuationModelDownloadInfo.DownloadUrl}";
        }
    }

    private void ApplyState(PipelineState state)
    {
        (StateLabel, StateBrush) = state switch
        {
            PipelineState.Idle => ("就绪", (IBrush)Brushes.Gray),
            PipelineState.Recording => ("录音中", Brushes.OrangeRed),
            PipelineState.Recognizing => ("识别中", Brushes.Goldenrod),
            PipelineState.Outputting => ("上屏中", Brushes.DodgerBlue),
            PipelineState.Paused => ("已暂停", Brushes.DimGray),
            PipelineState.Error => ("错误", Brushes.Crimson),
            _ => ("未知", Brushes.Gray),
        };
        IsPaused = state == PipelineState.Paused;
    }

    private void AppendHistory(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        History.Insert(0, new HistoryEntry(DateTime.Now, text));
        while (History.Count > MaxHistory) History.RemoveAt(History.Count - 1);
    }

    private void AppendLog(string line)
    {
        _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {line}");
        if (_log.Length > MaxLogChars)
        {
            _log.Remove(0, _log.Length - MaxLogChars);
        }
        LogText = _log.ToString();
    }

    public void SetStartupError(string message) => StartupError = message;

    partial void OnStartupErrorChanged(string value) => OnPropertyChanged(nameof(HasStartupError));

    partial void OnModelDownloadHintChanged(string value) => OnPropertyChanged(nameof(HasModelDownloadHint));

    partial void OnPunctModelDownloadHintChanged(string value) => OnPropertyChanged(nameof(HasPunctModelDownloadHint));

    [RelayCommand]
    private async Task CopyLatestAsync()
    {
        var latest = History.FirstOrDefault();
        if (latest is null) return;

        var clipboard = TryGetClipboard();
        if (clipboard is null) return;
        try { await clipboard.SetTextAsync(latest.Text); } catch { }
    }

    [RelayCommand]
    private void ClearHistory() => History.Clear();

    [RelayCommand]
    private void SaveShortcut()
    {
        if (_host is null)
        {
            ShortcutSaveStatus = "设计预览模式不可保存";
            return;
        }

        try
        {
            _host.SaveShortcut(SelectedShortcut.Key);
            ShortcutSaveStatus = "已保存，重启应用后生效";
        }
        catch (Exception ex)
        {
            ShortcutSaveStatus = $"保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void SaveModelDirectory()
    {
        if (_host is null)
        {
            ModelSaveStatus = "设计预览模式不可保存";
            return;
        }

        try
        {
            _host.SaveModelDirectory(ModelDir);
            ModelSaveStatus = "已保存，重启应用后生效";
            _ = DetectModelAsync(ModelDir);
        }
        catch (Exception ex)
        {
            ModelSaveStatus = $"保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void SavePunctuationDirectory()
    {
        if (_host is null)
        {
            PunctModelSaveStatus = "设计预览模式不可保存";
            return;
        }

        try
        {
            _host.SavePunctuationDirectory(PunctModelDir);
            PunctModelSaveStatus = "已保存，重启应用后生效";
            _ = DetectPunctModelAsync(PunctModelDir);
        }
        catch (Exception ex)
        {
            PunctModelSaveStatus = $"保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (_host is null) return;
        if (_host.Pipeline.State == PipelineState.Paused) _host.Pipeline.Resume();
        else _host.Pipeline.Pause();
    }

    [RelayCommand]
    private void HideToTray()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow?.Hide();
        }
    }

    [RelayCommand]
    private void Exit()
    {
        IsExiting = true;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private static IClipboard? TryGetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }
        return null;
    }
}

public sealed record HistoryEntry(DateTime Timestamp, string Text);
