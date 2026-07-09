using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using CapsWriterSharp.App.Services;
using CapsWriterSharp.Core.Config;
using CapsWriterSharp.Core.Pipeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CapsWriterSharp.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private const int MaxHistory = 50;
    private const int MaxLogChars = 20_000;

    private readonly AppHost? _host;
    private readonly AppConfig _config;
    private readonly StringBuilder _log = new();

    [ObservableProperty] private string _stateLabel = "就绪";
    [ObservableProperty] private IBrush _stateBrush = Brushes.Gray;
    [ObservableProperty] private string _shortcutHint = "长按 CapsLock 说话，松开上屏";
    [ObservableProperty] private string _modelDir = "models/paraformer";
    [ObservableProperty] private string _outputModeLabel = "模拟打字";
    [ObservableProperty] private string _pasteAppsLabel = "";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _isPaused;

    public bool IsExiting { get; private set; }

    public ObservableCollection<HistoryEntry> History { get; } = new();

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
        OutputModeLabel = _config.Output.Mode == OutputMode.Paste ? "剪贴板粘贴" : "模拟打字";
        PasteAppsLabel = _config.Output.PasteApps.Count == 0
            ? "（无）"
            : string.Join(", ", _config.Output.PasteApps);
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
