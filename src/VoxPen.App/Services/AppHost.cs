using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;
using VoxPen.Core.Hotword;
using VoxPen.Core.Hotword.Phoneme;
using VoxPen.Core.Notification;
using VoxPen.Core.Models;
using VoxPen.Core.Pipeline;
using VoxPen.Core.Postprocess;
using VoxPen.Core.Storage;
using VoxPen.Platform.Windows.Audio;
using VoxPen.Platform.Windows.Hooks;
using VoxPen.Platform.Windows.Notifications;
using VoxPen.Platform.Windows.Models;
using VoxPen.Platform.Windows.Recognition;
using VoxPen.Platform.Windows.Text;

namespace VoxPen.App.Services;

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
    private readonly IAsrEngine _asr;
    private readonly WindowsTextOutput _output;
    private readonly WindowsForegroundApp _foreground;
    private readonly AudioArchive _archive;
    private readonly DiaryWriter? _diary;
    private readonly PhonemeCorrector? _phonemeCorrector;
    private readonly INotificationService _notifier;
    private readonly IPunctuator _punctuator;
    private readonly IReadOnlyList<string> _hotkeyNames;

    private HotRuleReplacer _hotRule = HotRuleReplacer.Empty;
    private TrashPuncCleaner _trashPunc;

    private DebouncedFileWatcher? _hotRuleWatcher;
    private DebouncedFileWatcher? _configWatcher;
    private DebouncedFileWatcher? _hotwordWatcher;

    private bool _disposed;

    /// <summary>轻量日志通道：UI 订阅后填到"日志"面板。</summary>
    public event EventHandler<string>? LogEmitted;

    /// <summary>保存快捷键设置；全局监听器将在应用重启后使用新配置。</summary>
    public void SaveShortcut(string key) => ShortcutSettings.Save(_configPath, Config, key);

    public ModelDirectoryValidation ValidateModelDirectory(AsrEngineKind kind)
    {
        var definition = AsrModelCatalog.Get(kind);
        var resolved = ModelDirectoryResolver.Resolve(_appBaseDir, definition.DefaultModelDir);
        return AsrModelValidator.Validate(definition, resolved);
    }

    public bool IsModelLoaded => _asr.IsLoaded;
    public IReadOnlyList<AsrModelDefinition> AsrModels => AsrModelCatalog.All;

    public Task<string> DownloadModelAsync(AsrEngineKind kind, IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var coordinator = new ModelInstallCoordinator(
            new HttpRangeModelPackageDownloader(), new CompressedModelPackageInstaller());
        return coordinator.InstallAsync(AsrModelCatalog.Get(kind), _appBaseDir, progress, cancellationToken);
    }

    public void SaveAsrModel(AsrEngineKind kind)
    {
        var oldKind = Config.Asr.Engine;
        var oldDir = Config.Asr.ModelDir;
        var tempPath = $"{_configPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var definition = AsrModelCatalog.Get(kind);
            Config.Asr.Engine = kind;
            Config.Asr.ModelDir = ModelDirectoryResolver.Resolve(_appBaseDir, definition.DefaultModelDir);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(Config, SerializerOptions));
            File.Move(tempPath, _configPath, overwrite: true);
        }
        catch
        {
            Config.Asr.Engine = oldKind;
            Config.Asr.ModelDir = oldDir;
            throw;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// 向用户弹一条"模型缺失"的 Toast 提醒。失败会静默降级到日志。
    /// 只在 <see cref="AppConfig.Notification"/> 的 Enabled == true 时才真的发通知。
    /// </summary>
    public async Task NotifyModelMissingAsync(string modelDir)
    {
        if (!Config.Notification.Enabled) return;
        try
        {
            var (title, body) = ModelDownloadInfo.FormatToast(modelDir);
            await _notifier.ShowAsync(NotificationKind.Error, title, body).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Emit($"[toast] 模型缺失提醒发送失败：{ex.Message}");
        }
    }

    public bool IsModelLoadedFor(AsrEngineKind kind)
    {
        var definition = AsrModelCatalog.Get(kind);
        var resolved = ModelDirectoryResolver.Resolve(_appBaseDir, definition.DefaultModelDir);
        return Config.Asr.Engine == kind
            && _asr.IsLoaded
            && string.Equals(Path.GetFullPath(resolved), Path.GetFullPath(Config.Asr.ModelDir),
                StringComparison.OrdinalIgnoreCase);
    }

    private AppHost(string appBaseDir,
                    string configPath,
                    AppConfig config,
                    WindowsGlobalHotkey hotkey,
                    IReadOnlyList<string> hotkeyNames,
                    WindowsAudioCapture capture,
                    IAsrEngine asr,
                    WindowsTextOutput output,
                    WindowsForegroundApp foreground,
                    AudioArchive archive,
                    DiaryWriter? diary,
                    PhonemeCorrector? phonemeCorrector,
                    INotificationService notifier,
                    IPunctuator punctuator,
                    DictationPipeline pipeline,
                    TrashPuncCleaner trashPunc)
    {
        _appBaseDir = appBaseDir;
        _configPath = configPath;
        Config = config;
        _hotkey = hotkey;
        _hotkeyNames = hotkeyNames;
        _capture = capture;
        _asr = asr;
        _output = output;
        _foreground = foreground;
        _archive = archive;
        _diary = diary;
        _phonemeCorrector = phonemeCorrector;
        _notifier = notifier;
        _punctuator = punctuator;
        Pipeline = pipeline;
        _trashPunc = trashPunc;
    }

    /// <summary>创建 AppHost。此时组件已实例化但尚未加载模型/启动 hook。</summary>
    public static AppHost Create(string appBaseDir)
    {
        var configPath = Path.Combine(appBaseDir, "config.json");
        var config = LoadOrCreateConfig(configPath);

        // 模型目录由程序约定；旧 config.json 的 modelDir 仅为反序列化兼容而保留。
        ModelDirectoryConvention.Apply(config);

        // 发布目录优先；开发运行时允许从仓库根目录找到 models/。
        config.Asr.ModelDir = ModelDirectoryResolver.Resolve(appBaseDir, config.Asr.ModelDir);
        config.Punctuation.ModelDir =
            ModelDirectoryResolver.Resolve(appBaseDir, config.Punctuation.ModelDir);

        // 快捷键：优先使用 Keys 列表；否则退化为 Key
        var keyNames = config.Shortcut.Keys is { Count: > 0 }
            ? (IReadOnlyList<string>)config.Shortcut.Keys.ToArray()
            : new[] { config.Shortcut.Key };

        var hotkey = new WindowsGlobalHotkey(keyNames, config.Shortcut.Suppress);
        var capture = new WindowsAudioCapture(config.Audio.InputDevice);
        var asr = WindowsAsrEngineFactory.Create(config.Asr);
        var output = new WindowsTextOutput();
        var foreground = new WindowsForegroundApp();
        var archive = new AudioArchive(
            rootDir: Path.Combine(appBaseDir, "recordings"),
            nameLength: config.Audio.AudioNameLength);

        // 日记：默认写到与录音同根（recordings/YYYY/MM/DD.md）
        DiaryWriter? diary = config.Audio.DiaryEnabled
            ? new DiaryWriter(Path.Combine(appBaseDir, "recordings"))
            : null;

        // 音素 RAG（首次为空；ReloadHotwords 会填充）
        PhonemeCorrector? phonemeCorrector = config.Hotword.EnablePhonemeRag
            ? new PhonemeCorrector(
                threshold: config.Hotword.MatchThreshold,
                similarThreshold: config.Hotword.SimilarThreshold)
            : null;

        // Toast：Config.Notification.Enabled 关闭时退化为 Null 实现
        INotificationService notifier = config.Notification.Enabled
            ? new WindowsToastNotifier()
            : NullNotificationService.Instance;

        var pipeline = new DictationPipeline(hotkey, capture, asr, output, config, foreground);

        var trashPunc = new TrashPuncCleaner(
            config.Postprocess.TrashPunctuation,
            config.Postprocess.TrashPuncThreshold,
            config.Postprocess.TrashPuncApps);

        // 标点补全：ASR 引擎已自带标点则跳过；否则按 model.onnx 是否存在决定装配 Sherpa 还是 Null。
        IPunctuator punctuator = ResolvePunctuator(asr, config.Punctuation);

        var host = new AppHost(appBaseDir, configPath, config,
            hotkey, keyNames, capture, asr, output, foreground, archive,
            diary, phonemeCorrector, notifier, punctuator, pipeline, trashPunc);

        // 组装后处理管线：punctuator → hot-rule (regex) → phoneme-rag → trash-punc
        pipeline.PostProcess = host.RunPostProcess;

        // 录音归档 + 日记
        pipeline.ArchiveHandler = host.HandleArchiveAndDiaryAsync;

        // 通知钩子
        pipeline.NotificationHandler = host.HandleNotificationAsync;

        // 短按补发：只对 toggle 键（caps/num/scroll_lock）生效，把切换语义还给用户；
        // 其它键（F1..F12、鼠标侧键等）短按直接静默丢弃，避免误触发大小写。
        pipeline.ShortPressDetected += (_, keyName) =>
        {
            try { output.ResendToggleKey(keyName); } catch { /* 忽略 */ }
        };

        // 首次加载 hot-rule.txt / hot.txt，启动监视
        host.ReloadHotRule();
        host.ReloadHotwords();
        host.SetupWatchers();

        return host;
    }

    /// <summary>加载模型 + 启动全局钩子。加载失败会抛出。</summary>
    public async Task LoadAndStartAsync()
    {
        Emit($"hot-rule.txt: {_hotRule.RuleCount} 条规则已就绪");
        if (_phonemeCorrector is not null)
        {
            Emit($"音素 RAG 热词：{_phonemeCorrector.HotwordCount} 条已就绪");
        }
        Emit($"开始加载模型：{Config.Asr.ModelDir}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _asr.LoadAsync().ConfigureAwait(false);
        sw.Stop();
        Emit($"模型加载完成，耗时 {sw.ElapsedMilliseconds} ms");

        // 标点模型：与 ASR 一样两阶段初始化。NullPunctuator 的 IsLoaded 恒为 true，天然跳过；
        // SherpaPunctuator 加载失败时自身降级为原样返回，只在日志中告警。
        if (!_punctuator.IsLoaded)
        {
            Emit($"开始加载标点模型：{Config.Punctuation.ModelDir}");
            var puncSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await _punctuator.LoadAsync().ConfigureAwait(false);
                puncSw.Stop();
                Emit($"标点模型加载完成（{_punctuator.Name}），耗时 {puncSw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                puncSw.Stop();
                Emit($"[punc] 标点模型加载失败，进入无标点模式：{ex.Message}");
            }
        }
        else if (_punctuator is NullPunctuator)
        {
            Emit("标点补全未启用（未找到 model.onnx 或 ASR 已自带标点）");
        }

        Pipeline.Start();
        Emit($"已监听快捷键：[{string.Join(", ", _hotkeyNames)}] (suppress={Config.Shortcut.Suppress})");
    }

    public void Emit(string message) => LogEmitted?.Invoke(this, message);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _hotRuleWatcher?.Dispose(); } catch { }
        try { _configWatcher?.Dispose(); } catch { }
        try { _hotwordWatcher?.Dispose(); } catch { }
        try { Pipeline.Dispose(); } catch { }
        try { _hotkey.Dispose(); } catch { }
        try { _capture.Dispose(); } catch { }
        try { _asr.Dispose(); } catch { }
        try { _punctuator.Dispose(); } catch { }
    }

    // ---------- 后处理 + 归档 ----------

    private string RunPostProcess(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        // 顺序：标点补全 → hot-rule → phoneme-rag → trash-punc。
        // 上游 CapsWriter-Offline 的 hot-rule.txt 用 [，。]? 邻位吞噬来吸掉
        // 标点模型在"逗号/句号/问号/回车"周围可能多加的标点，因此标点必须在 hot-rule 之前。
        var text = raw;
        try
        {
            text = _punctuator.AddPunctuation(text);
        }
        catch (Exception ex)
        {
            Emit($"[punc] 标点补全异常，跳过：{ex.Message}");
        }

        if (Config.Postprocess.EnableHotRule)
        {
            text = _hotRule.Apply(text);
        }

        if (_phonemeCorrector is not null)
        {
            try
            {
                var result = _phonemeCorrector.Correct(text);
                text = result.Text;
                if (result.Matches.Count > 0)
                {
                    Emit($"[hotword] 应用 {result.Matches.Count} 处替换");
                }
            }
            catch (Exception ex)
            {
                Emit($"[hotword] 纠错失败：{ex.Message}");
            }
        }

        var exe = _foreground.GetForegroundExeName();
        text = _trashPunc.Apply(text, exe);
        return text;
    }

    private async Task HandleArchiveAndDiaryAsync(float[] samples, string finalText)
    {
        string? wavPath = null;
        if (Config.Audio.SaveRecording)
        {
            wavPath = await _archive.SaveAsync(samples, finalText, DateTime.Now).ConfigureAwait(false);
        }
        if (_diary is not null && !string.IsNullOrEmpty(finalText))
        {
            try
            {
                await _diary.WriteAsync(finalText, DateTime.Now, wavPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Emit($"[diary] 写入失败：{ex.Message}");
            }
        }
    }

    private Task HandleNotificationAsync(NotificationKind kind, string title, string body)
    {
        if (!Config.Notification.Enabled) return Task.CompletedTask;
        try
        {
            return _notifier.ShowAsync(kind, title, body);
        }
        catch (Exception ex)
        {
            Emit($"[toast] 发送失败：{ex.Message}");
            return Task.CompletedTask;
        }
    }

    // ---------- 热重载 ----------

    private void SetupWatchers()
    {
        var hotRulePath = ResolveHotRulePath();
        _hotRuleWatcher = new DebouncedFileWatcher(hotRulePath, DebounceInterval, ReloadHotRule);
        _configWatcher = new DebouncedFileWatcher(_configPath, DebounceInterval, ReloadConfig);

        if (_phonemeCorrector is not null)
        {
            var hotwordPath = ResolveHotwordPath();
            _hotwordWatcher = new DebouncedFileWatcher(hotwordPath, DebounceInterval, ReloadHotwords);
        }
    }

    private string ResolveHotRulePath()
    {
        var p = Config.Postprocess.HotRulePath;
        return Path.IsPathRooted(p) ? p : Path.Combine(_appBaseDir, p);
    }

    private string ResolveHotwordPath()
    {
        var p = Config.Hotword.HotwordPath;
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

    private void ReloadHotwords()
    {
        if (_phonemeCorrector is null) return;
        try
        {
            var path = ResolveHotwordPath();
            if (!File.Exists(path))
            {
                Emit($"hot.txt 未找到（{path}），音素 RAG 已加载 0 条");
                _phonemeCorrector.UpdateHotwords(Array.Empty<HotwordEntry>());
                return;
            }
            var content = File.ReadAllText(path);
            var count = _phonemeCorrector.UpdateHotwordsFromText(content);
            Emit($"已加载 hot.txt（{count} 条热词）");
        }
        catch (Exception ex)
        {
            Emit($"hot.txt 加载失败：{ex.Message}");
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

            // 只热重载"能安全在运行时替换"的部分：后处理 / 上屏 / 归档 / 通知
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
            Config.Audio.DiaryEnabled = cfg.Audio.DiaryEnabled;

            Config.Notification.Enabled = cfg.Notification.Enabled;
            Config.Notification.ShowOnRecordingStart = cfg.Notification.ShowOnRecordingStart;
            Config.Notification.ShowOnError = cfg.Notification.ShowOnError;

            Config.Hotword.MatchThreshold = cfg.Hotword.MatchThreshold;
            Config.Hotword.SimilarThreshold = cfg.Hotword.SimilarThreshold;

            _trashPunc = new TrashPuncCleaner(
                Config.Postprocess.TrashPunctuation,
                Config.Postprocess.TrashPuncThreshold,
                Config.Postprocess.TrashPuncApps);

            Emit("已热重载 config.json（快捷键/模型/日记根目录改动需重启生效）");
        }
        catch (Exception ex)
        {
            Emit($"config.json 热重载失败：{ex.Message}");
        }
    }

    // ---------- 配置文件加载 ----------

    /// <summary>
    /// 根据 ASR 引擎能力位与配置决定注入哪种 <see cref="IPunctuator"/>。
    ///
    /// - 若引擎自带 <see cref="EngineCapabilities.Punctuation"/>：直接 NullPunctuator（不重复加标点）。
    /// - 若配置的 <c>model.onnx</c> 不存在：NullPunctuator（用户未安装标点模型，走"无标点模式"）。
    /// - 否则返回未加载的 <see cref="SherpaPunctuator"/>，由 <see cref="LoadAndStartAsync"/> 真正加载；
    ///   加载失败时 SherpaPunctuator 内部自动降级为原样返回。
    /// </summary>
    private static IPunctuator ResolvePunctuator(IAsrEngine asr, PunctuationConfig cfg)
    {
        if ((asr.Capabilities & EngineCapabilities.Punctuation) != 0)
        {
            return NullPunctuator.Instance;
        }

        if (string.IsNullOrWhiteSpace(cfg.ModelDir))
        {
            return NullPunctuator.Instance;
        }

        var modelFile = Path.Combine(cfg.ModelDir, "model.onnx");
        if (!File.Exists(modelFile))
        {
            return NullPunctuator.Instance;
        }

        return new SherpaPunctuator(modelFile, cfg.NumThreads, cfg.Provider);
    }

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
