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
using VoxPen.Core.Abstractions;
using VoxPen.Core.Models;
using VoxPen.Core.Pipeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VoxPen.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private const int MaxHistory = 50;
    private const int MaxLogChars = 20_000;

    private AppHost? _host;
    private AppConfig _config = new();
    private readonly Func<IReadOnlyList<string>, AsrEngineKind, Task>? _applySettingsAsync;
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _modelCheckCancellation;
    private CancellationTokenSource? _modelDownloadCancellation;
    private CancellationTokenSource? _punctuationDownloadCancellation;

    [ObservableProperty] private string _stateLabel = "就绪";
    [ObservableProperty] private IBrush _stateBrush = Brushes.Gray;
    [ObservableProperty] private string _shortcutHint = "长按 CapsLock 说话，松开上屏";
    [ObservableProperty] private string _modelStatusIcon = "❌";
    [ObservableProperty] private string _modelStatusText = "正在检测模型…";
    [ObservableProperty] private AsrModelDefinition? _selectedAsrModel;
    [ObservableProperty] private double _modelDownloadPercent;
    [ObservableProperty] private string _modelDownloadStatus = "";
    [ObservableProperty] private bool _isModelDownloading;
    [ObservableProperty] private string _punctuationModelStatusIcon = "…";
    [ObservableProperty] private string _punctuationModelStatusText = "正在检测标点模型…";
    [ObservableProperty] private double _punctuationDownloadPercent;
    [ObservableProperty] private string _punctuationDownloadStatus = "";
    [ObservableProperty] private bool _isPunctuationModelDownloading;
    [ObservableProperty] private string _outputModeLabel = "模拟打字";
    [ObservableProperty] private string _pasteAppsLabel = "";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _startupError = "";
    [ObservableProperty] private string _shortcutDisplay = "Caps Lock";
    [ObservableProperty] private string _shortcutRecordingStatus = "";
    [ObservableProperty] private bool _isRecordingShortcut;
    [ObservableProperty] private string _settingsSaveStatus = "";
    [ObservableProperty] private bool _isSavingSettings;
    [ObservableProperty] private bool _isPaused;

    public bool HasStartupError => !string.IsNullOrEmpty(StartupError);

    public bool IsExiting { get; private set; }

    public ObservableCollection<HistoryEntry> History { get; } = new();
    public IReadOnlyList<AsrModelDefinition> AsrModels => AsrModelCatalog.All;
    private List<string> RecordedShortcutKeys { get; } = [ShortcutSettings.DefaultKey];
    private HashSet<string> PressedRecordedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>设计时无参构造，供 Avalonia XAML 预览器使用。</summary>
    public MainWindowViewModel()
    {
        _config = new AppConfig();
        SelectedAsrModel = AsrModelCatalog.Get(_config.Asr.Engine);
        RefreshConfigLabels();
    }

    public MainWindowViewModel(AppHost host, Func<IReadOnlyList<string>, AsrEngineKind, Task> applySettingsAsync)
    {
        _applySettingsAsync = applySettingsAsync;
        AttachHost(host);
    }

    public bool CanEditSettings => !IsSavingSettings && !IsModelDownloading && !IsPunctuationModelDownloading && !IsRecordingShortcut;
    public bool CanSaveSettings => CanEditSettings && SelectedAsrModel is not null;
    public bool ShouldShowPunctuationDownload => SelectedAsrModel is not null &&
        (SelectedAsrModel.Capabilities & EngineCapabilities.Punctuation) == 0;

    public void ReplaceHost(AppHost host) => AttachHost(host);

    private void AttachHost(AppHost host)
    {
        _host = host;
        _config = host.Config;
        SelectedAsrModel = AsrModelCatalog.Get(_config.Asr.Engine);
        RefreshConfigLabels();
        _ = DetectPunctuationModelAsync();

        host.Pipeline.StateChanged += (_, s) => Dispatcher.UIThread.Post(() => ApplyState(s));
        host.Pipeline.TextRecognized += (_, text) => Dispatcher.UIThread.Post(() => AppendHistory(text));
        host.LogEmitted += (_, line) => Dispatcher.UIThread.Post(() => AppendLog(line));
        host.ShortcutKeyObserved += (_, args) => Dispatcher.UIThread.Post(() => ObserveShortcutKey(args));
        ApplyState(host.Pipeline.State);
    }

    private void RefreshConfigLabels()
    {
        var configuredKeys = _config.Shortcut.Keys.Count > 0 ? _config.Shortcut.Keys : [_config.Shortcut.Key];
        RecordedShortcutKeys.Clear();
        RecordedShortcutKeys.AddRange(ShortcutSettings.NormalizeKeys(configuredKeys));
        ShortcutDisplay = ShortcutSettings.GetDisplayName(RecordedShortcutKeys);
        ShortcutHint = $"长按 {ShortcutDisplay} 说话，松开上屏";
        OutputModeLabel = _config.Output.Mode == OutputMode.Paste ? "剪贴板粘贴" : "模拟打字";
        PasteAppsLabel = _config.Output.PasteApps.Count == 0
            ? "（无）"
            : string.Join(", ", _config.Output.PasteApps);
    }

    partial void OnSelectedAsrModelChanged(AsrModelDefinition? value)
    {
        OnPropertyChanged(nameof(CanSaveSettings));
        OnPropertyChanged(nameof(ShouldShowPunctuationDownload));
        if (value is null) return;
        _ = DetectModelAsync(value);
        _ = DetectPunctuationModelAsync();
    }

    partial void OnIsSavingSettingsChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(CanSaveSettings));
    }

    partial void OnIsModelDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(CanSaveSettings));
    }

    partial void OnIsPunctuationModelDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(CanSaveSettings));
    }

    partial void OnIsRecordingShortcutChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditSettings));
    }

    private async Task DetectModelAsync(AsrModelDefinition definition)
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
                ? AsrModelValidator.Validate(definition, definition.DefaultModelDir)
                : _host.ValidateModelDirectory(definition.Kind);
            if (cancellationToken.IsCancellationRequested) return;

            ModelStatusIcon = result.IsValid ? "✅" : "❌";
            ModelStatusText = result.IsValid
                ? (_host?.IsModelLoadedFor(definition.Kind) == true ? "模型已加载" : "模型文件完整，保存后立即应用")
                : result.Message;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ModelStatusIcon = "❌";
            ModelStatusText = $"检测失败：{ex.Message}";
        }
    }

    private Task DetectPunctuationModelAsync()
    {
        if (!ShouldShowPunctuationDownload)
        {
            PunctuationModelStatusIcon = "✅";
            PunctuationModelStatusText = "此模型自带标点";
            return Task.CompletedTask;
        }

        var result = _host is null
            ? PunctuationModelValidator.Validate(ModelDirectoryConvention.PunctuationModelDirectory)
            : _host.ValidatePunctuationModelDirectory();
        PunctuationModelStatusIcon = result.IsValid ? "✅" : "⚪";
        PunctuationModelStatusText = result.IsValid
            ? "标点模型文件完整，保存后立即应用"
            : "未安装，将输出无标点";
        return Task.CompletedTask;
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

    [RelayCommand]
    private async Task DownloadSelectedModelAsync()
    {
        if (_host is null || SelectedAsrModel is null || IsModelDownloading || IsSavingSettings) return;
        _modelDownloadCancellation = new CancellationTokenSource();
        IsModelDownloading = true;
        ModelDownloadStatus = "准备下载…";
        try
        {
            var progress = new Progress<ModelDownloadProgress>(value =>
            {
                ModelDownloadPercent = value.Percent ?? 0;
                ModelDownloadStatus = value.State == ModelDownloadState.Downloading
                    ? $"下载中 {value.Percent:0.0}%  {value.BytesPerSecond / 1024d / 1024d:0.0} MB/s"
                    : value.Message ?? value.State.ToString();
            });
            await _host.DownloadModelAsync(SelectedAsrModel.Kind, progress, _modelDownloadCancellation.Token);
            await DetectModelAsync(SelectedAsrModel);
            ModelDownloadStatus = "模型已安装，点击“保存并应用”立即切换";
        }
        catch (OperationCanceledException) { ModelDownloadStatus = "下载已取消，可稍后继续"; }
        catch (Exception ex) { ModelDownloadStatus = $"下载失败：{ex.Message}"; }
        finally
        {
            IsModelDownloading = false;
            _modelDownloadCancellation?.Dispose();
            _modelDownloadCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelModelDownload() => _modelDownloadCancellation?.Cancel();

    [RelayCommand]
    private async Task DownloadPunctuationModelAsync()
    {
        if (_host is null || !ShouldShowPunctuationDownload || IsPunctuationModelDownloading || IsSavingSettings) return;
        _punctuationDownloadCancellation = new CancellationTokenSource();
        IsPunctuationModelDownloading = true;
        PunctuationDownloadStatus = "准备下载标点模型…";
        try
        {
            var progress = new Progress<ModelDownloadProgress>(value =>
            {
                PunctuationDownloadPercent = value.Percent ?? 0;
                PunctuationDownloadStatus = value.State == ModelDownloadState.Downloading
                    ? $"下载中 {value.Percent:0.0}%  {value.BytesPerSecond / 1024d / 1024d:0.0} MB/s"
                    : value.Message ?? value.State.ToString();
            });
            await _host.DownloadPunctuationModelAsync(progress, _punctuationDownloadCancellation.Token);
            await DetectPunctuationModelAsync();
            PunctuationDownloadStatus = "标点模型已安装，点击“保存并应用”立即生效";
        }
        catch (OperationCanceledException) { PunctuationDownloadStatus = "下载已取消，可稍后继续"; }
        catch (Exception ex) { PunctuationDownloadStatus = $"下载失败：{ex.Message}"; }
        finally
        {
            IsPunctuationModelDownloading = false;
            _punctuationDownloadCancellation?.Dispose();
            _punctuationDownloadCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelPunctuationDownload() => _punctuationDownloadCancellation?.Cancel();

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
    private async Task SaveSettingsAsync()
    {
        if (_host is null || _applySettingsAsync is null || SelectedAsrModel is null)
        {
            SettingsSaveStatus = "设计预览模式不可保存";
            return;
        }

        IsSavingSettings = true;
        SettingsSaveStatus = "正在保存并应用设置…";
        try
        {
            await _applySettingsAsync(RecordedShortcutKeys, SelectedAsrModel.Kind);
            await DetectModelAsync(SelectedAsrModel);
            SettingsSaveStatus = "已保存并立即生效";
        }
        catch (Exception ex)
        {
            SettingsSaveStatus = $"保存失败：{ex.Message}";
        }
        finally
        {
            IsSavingSettings = false;
        }
    }

    [RelayCommand]
    private void StartShortcutRecording()
    {
        if (!CanEditSettings) return;
        PressedRecordedKeys.Clear();
        _recordedDuringCapture.Clear();
        _host?.BeginShortcutRecording();
        IsRecordingShortcut = true;
        ShortcutRecordingStatus = "请同时按下所需组合键，然后全部松开。单独字母键不会被接受。";
    }

    [RelayCommand]
    private void CancelShortcutRecording()
    {
        IsRecordingShortcut = false;
        PressedRecordedKeys.Clear();
        _recordedDuringCapture.Clear();
        _host?.EndShortcutRecording();
        ShortcutRecordingStatus = "已取消快捷键录制。";
    }

    private void ObserveShortcutKey(HotkeyObservedEventArgs args)
    {
        if (!IsRecordingShortcut) return;
        if (args.IsPressed)
        {
            PressedRecordedKeys.Add(args.Key);
            if (!_recordedDuringCapture.Contains(args.Key, StringComparer.OrdinalIgnoreCase))
            {
                _recordedDuringCapture.Add(args.Key);
            }
            return;
        }

        PressedRecordedKeys.Remove(args.Key);
        if (PressedRecordedKeys.Count != 0) return;

        try
        {
            var keys = ShortcutSettings.NormalizeKeys(PressedRecordedKeys.Count == 0
                ? _recordedDuringCapture
                : PressedRecordedKeys);
            RecordedShortcutKeys.Clear();
            RecordedShortcutKeys.AddRange(keys);
            ShortcutDisplay = ShortcutSettings.GetDisplayName(keys);
            ShortcutHint = $"长按 {ShortcutDisplay} 说话，松开上屏";
            ShortcutRecordingStatus = "快捷键已录制，点击“保存并应用”生效。";
        }
        catch (ArgumentException ex)
        {
            ShortcutRecordingStatus = ex.Message;
        }
        finally
        {
            _recordedDuringCapture.Clear();
            IsRecordingShortcut = false;
            _host?.EndShortcutRecording();
        }
    }

    private readonly List<string> _recordedDuringCapture = [];

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
