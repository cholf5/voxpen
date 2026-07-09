using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CapsWriterSharp.Core.Abstractions;
using CapsWriterSharp.Core.Config;
using CapsWriterSharp.Core.Hotword;
using CapsWriterSharp.Core.Pipeline;
using CapsWriterSharp.Core.Postprocess;
using CapsWriterSharp.Core.Storage;
using CapsWriterSharp.Platform.Windows.Audio;
using CapsWriterSharp.Platform.Windows.Hooks;
using CapsWriterSharp.Platform.Windows.Recognition;
using CapsWriterSharp.Platform.Windows.Text;

namespace CapsWriterSharp.App.Services;

/// <summary>
/// 组合根：把 Core 抽象 + Windows 平台实现串起来。
///
/// 生命周期：
/// - <see cref="LoadAndStartAsync"/> 加载 ASR 模型 + 启动 hotkey；成功后进入监听状态
/// - <see cref="Dispose"/> 停止所有资源；应用退出前调用
///
/// UI 层订阅：Pipeline.StateChanged / Pipeline.TextRecognized；本类另暴露 LogEmitted。
/// </summary>
public sealed class AppHost : IDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(3);

    public AppConfig Config { get; }
    public DictationPipeline Pipeline { get; }

    private readonly string _appBaseDir;
    private readonly string _configPath;
    private readonly WindowsGlobalHotkey _hotkey;
    private readonly WindowsAudioCapture _capture;
    private readonly ParaformerEngine _asr;
    private readonly WindowsTextOutput _output;
    private readonly WindowsForegroundApp _foreground;
    private readonly AudioArchive _archive;

    private HotRuleReplacer _hotRule = HotRuleReplacer.Empty;
    private TrashPuncCleaner _trashPunc;

    private DebouncedFileWatcher? _hotRuleWatcher;
    private DebouncedFileWatcher? _configWatcher;

    private bool _disposed;

    /// <summary>轻量日志通道：UI 订阅后填到"日志"面板。</summary>
    public event EventHandler<string>? LogEmitted;

    private AppHost(string appBaseDir,
                    string configPath,
                    AppConfig config,
                    WindowsGlobalHotkey hotkey,
                    WindowsAudioCapture capture,
                    ParaformerEngine asr,
                    WindowsTextOutput output,
                    WindowsForegroundApp foreground,
                    AudioArchive archive,
                    DictationPipeline pipeline,
                    TrashPuncCleaner trashPunc)
    {
        _appBaseDir = appBaseDir;
        _configPath = configPath;
        Config = config;
        _hotkey = hotkey;
        _capture = capture;
        _asr = asr;
        _output = output;
        _foreground = foreground;
        _archive = archive;
        Pipeline = pipeline;
        _trashPunc = trashPunc;
    }

    /// <summary>创建 AppHost。此时组件已实例化但尚未加载模型/启动 hook。</summary>
    public static AppHost Create(string appBaseDir)
    {
        var configPath = Path.Combine(appBaseDir, "config.json");
        var config = LoadOrCreateConfig(configPath);

        // 若 ModelDir 是相对路径，转成绝对
        if (!Path.IsPathRooted(config.Asr.ModelDir))
        {
            config.Asr.ModelDir = Path.Combine(appBaseDir, config.Asr.ModelDir);
        }

        var hotkey = new WindowsGlobalHotkey(config.Shortcut.Key, config.Shortcut.Suppress);
        var capture = new WindowsAudioCapture(config.Audio.InputDevice);
        var asr = new ParaformerEngine(config.Asr);
        var output = new WindowsTextOutput();
        var foreground = new WindowsForegroundApp();
        var archive = new AudioArchive(
            rootDir: Path.Combine(appBaseDir, "recordings"),
            nameLength: config.Audio.AudioNameLength);

        var pipeline = new DictationPipeline(hotkey, capture, asr, output, config, foreground);

        var trashPunc = new TrashPuncCleaner(
            config.Postprocess.TrashPunctuation,
            config.Postprocess.TrashPuncThreshold,
            config.Postprocess.TrashPuncApps);

        var host = new AppHost(appBaseDir, configPath, config,
            hotkey, capture, asr, output, foreground, archive, pipeline, trashPunc);

        // 组装后处理管线：hot-rule → trash-punc
        pipeline.PostProcess = host.RunPostProcess;

        // 录音归档
        pipeline.ArchiveHandler = host.HandleArchiveAsync;

        // 短按补发：CapsLock 场景下把切换语义还给用户
        pipeline.ShortPressDetected += (_, _) =>
        {
            if (string.Equals(config.Shortcut.Key, "caps_lock", StringComparison.OrdinalIgnoreCase))
            {
                try { output.ResendCapsLock(); } catch { /* 忽略 */ }
            }
        };

        // 首次加载 hot-rule.txt 并启动监视
        host.ReloadHotRule();
        host.SetupWatchers();

        return host;
    }

    /// <summary>加载模型 + 启动全局钩子。加载失败会抛出。</summary>
    public async Task LoadAndStartAsync()
    {
        Emit($"hot-rule.txt: {_hotRule.RuleCount} 条规则已就绪");
        Emit($"开始加载模型：{Config.Asr.ModelDir}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _asr.LoadAsync().ConfigureAwait(false);
        sw.Stop();
        Emit($"模型加载完成，耗时 {sw.ElapsedMilliseconds} ms");

        Pipeline.Start();
        Emit($"已监听快捷键：{Config.Shortcut.Key} (suppress={Config.Shortcut.Suppress})");
    }

    public void Emit(string message) => LogEmitted?.Invoke(this, message);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _hotRuleWatcher?.Dispose(); } catch { }
        try { _configWatcher?.Dispose(); } catch { }
        try { Pipeline.Dispose(); } catch { }
        try { _hotkey.Dispose(); } catch { }
        try { _capture.Dispose(); } catch { }
        try { _asr.Dispose(); } catch { }
    }

    // ---------- 后处理 + 归档 ----------

    private string RunPostProcess(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        var text = raw;
        if (Config.Postprocess.EnableHotRule)
        {
            text = _hotRule.Apply(text);
        }

        var exe = _foreground.GetForegroundExeName();
        text = _trashPunc.Apply(text, exe);
        return text;
    }

    private Task HandleArchiveAsync(float[] samples, string finalText)
    {
        if (!Config.Audio.SaveRecording) return Task.CompletedTask;
        return _archive.SaveAsync(samples, finalText, DateTime.Now);
    }

    // ---------- 热重载 ----------

    private void SetupWatchers()
    {
        var hotRulePath = ResolveHotRulePath();
        _hotRuleWatcher = new DebouncedFileWatcher(hotRulePath, DebounceInterval, ReloadHotRule);
        _configWatcher = new DebouncedFileWatcher(_configPath, DebounceInterval, ReloadConfig);
    }

    private string ResolveHotRulePath()
    {
        var p = Config.Postprocess.HotRulePath;
        return Path.IsPathRooted(p) ? p : Path.Combine(_appBaseDir, p);
    }

    private void ReloadHotRule()
    {
        try
        {
            var path = ResolveHotRulePath();
            _hotRule = HotRuleReplacer.Load(path);
            Emit($"已加载 hot-rule.txt（{_hotRule.RuleCount} 条规则）");
        }
        catch (Exception ex)
        {
            Emit($"hot-rule.txt 加载失败：{ex.Message}");
        }
    }

    private void ReloadConfig()
    {
        try
        {
            if (!File.Exists(_configPath)) return;
            var json = File.ReadAllText(_configPath);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
            if (cfg is null) return;

            // 只热重载"能安全在运行时替换"的部分：后处理 / 上屏 / 归档相关
            Config.Postprocess.EnableHotRule = cfg.Postprocess.EnableHotRule;
            Config.Postprocess.HotRulePath = cfg.Postprocess.HotRulePath;
            Config.Postprocess.TrashPunctuation = cfg.Postprocess.TrashPunctuation;
            Config.Postprocess.TrashPuncThreshold = cfg.Postprocess.TrashPuncThreshold;
            Config.Postprocess.TrashPuncApps = cfg.Postprocess.TrashPuncApps;

            Config.Output.Mode = cfg.Output.Mode;
            Config.Output.RestoreClipboard = cfg.Output.RestoreClipboard;
            Config.Output.PasteApps = cfg.Output.PasteApps;

            Config.Audio.SaveRecording = cfg.Audio.SaveRecording;
            Config.Audio.AudioNameLength = cfg.Audio.AudioNameLength;

            _trashPunc = new TrashPuncCleaner(
                Config.Postprocess.TrashPunctuation,
                Config.Postprocess.TrashPuncThreshold,
                Config.Postprocess.TrashPuncApps);

            Emit("已热重载 config.json（快捷键/模型改动需重启生效）");
        }
        catch (Exception ex)
        {
            Emit($"config.json 热重载失败：{ex.Message}");
        }
    }

    // ---------- 配置文件加载 ----------

    private static AppConfig LoadOrCreateConfig(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
                if (cfg is not null) return cfg;
            }
        }
        catch
        {
            // 解析失败：使用默认配置，不覆盖用户文件
        }

        var fresh = new AppConfig();
        try
        {
            var json = JsonSerializer.Serialize(fresh, SerializerOptions);
            File.WriteAllText(path, json);
        }
        catch { /* 首次写入失败不致命 */ }
        return fresh;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
