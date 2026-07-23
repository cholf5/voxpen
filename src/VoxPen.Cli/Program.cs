using System.Diagnostics;
using VoxPen.Cli;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;
using VoxPen.Core.Hotword;
using VoxPen.Core.Hotword.Phoneme;
using VoxPen.Core.Pipeline;
using VoxPen.Core.Storage;
using VoxPen.Core.Transcribe;
using VoxPen.Platform.Windows.Audio;
using VoxPen.Platform.Windows.Hooks;
using VoxPen.Platform.Windows.Recognition;
using VoxPen.Platform.Windows.Text;

// -------- 参数 --------
// 无参数 / interactive : 交互式录音 → 识别（P2 用；stdin 触发）
// --file <path>        : 直接识别 wav（P2 用）
// run                  : 常驻监听 CapsLock，按住说话松开上屏（P3 端到端）
// transcribe <files>   : 批量文件转录（P7）
// test-hotword / test-diary / test-merger : 冒烟命令，不加载模型
// test-punc [<text>]   : 加载 CT-Transformer 标点模型，对文本加标点（需要标点模型目录）
// --engine <name>      : 选择 ASR 引擎（paraformer / sensevoice / fun_asr_nano / qwen_asr）
// --device <name>      : 偏好的输入设备名
// --paste              : run 模式下默认使用剪贴板粘贴而非模拟打字
// --no-suppress        : run 模式下不抑制 CapsLock 默认行为（调试用）
string? filePath = null;
AsrEngineKind engineKind = AsrEngineKind.Paraformer;
string? deviceName = null;
string mode = "interactive";
bool paste = false;
bool suppress = true;
int autoExitSecs = 0;
string puncTestText = "你好世界这是一段没有标点的文本这是第二句话对吗";

// transcribe 专用参数
var transcribeInputs = new List<string>();
var transcribeConfig = new TranscribeConfig();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "run": mode = "run"; break;
        case "interactive": mode = "interactive"; break;
        case "test-postprocess": mode = "test-postprocess"; break;
        case "test-hotword": mode = "test-hotword"; break;
        case "test-diary": mode = "test-diary"; break;
        case "test-merger": mode = "test-merger"; break;
        case "test-punc":
            mode = "test-punc";
            if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) puncTestText = args[++i];
            break;
        case "transcribe": mode = "transcribe"; break;
        case "--file" when i + 1 < args.Length: filePath = args[++i]; mode = "file"; break;
        case "--model" or "--punct-model":
            Console.Error.WriteLine("模型目录由 VoxPen 固定管理；请将模型复制到程序约定的 models/ 目录。");
            return 2;
        case "--engine" when i + 1 < args.Length:
            var engineName = args[++i].Trim().ToLowerInvariant();
            if (engineName is "paraformer") engineKind = AsrEngineKind.Paraformer;
            else if (engineName is "sensevoice") engineKind = AsrEngineKind.SenseVoice;
            else if (engineName is "fun_asr_nano") engineKind = AsrEngineKind.FunAsrNano;
            else if (engineName is "qwen_asr") engineKind = AsrEngineKind.QwenAsr;
            else
            {
                Console.Error.WriteLine("未知引擎。可选：paraformer、sensevoice、fun_asr_nano、qwen_asr。");
                return 2;
            }
            break;
        case "--device" when i + 1 < args.Length: deviceName = args[++i]; break;
        case "--paste": paste = true; break;
        case "--no-suppress": suppress = false; break;
        case "--auto-exit-secs" when i + 1 < args.Length: autoExitSecs = int.Parse(args[++i]); break;
        case "--seg-duration" when i + 1 < args.Length:
            transcribeConfig.SegDurationSeconds = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--seg-overlap" when i + 1 < args.Length:
            transcribeConfig.SegOverlapSeconds = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--no-srt": transcribeConfig.SaveSrt = false; break;
        case "--no-json": transcribeConfig.SaveJson = false; break;
        case "--no-txt": transcribeConfig.SaveTxt = false; break;
        case "--merge": transcribeConfig.SaveMerge = true; break;
        case "-h" or "--help": PrintHelp(); return 0;
        default:
            // transcribe 模式下，未识别的裸参数当作输入文件
            if (mode == "transcribe" && !args[i].StartsWith("-"))
            {
                transcribeInputs.Add(args[i]);
            }
            break;
    }
}

var modelDir = AsrModelCatalog.Get(engineKind).DefaultModelDir;
var punctModelDir = ModelDirectoryConvention.PunctuationModelDirectory;

Console.WriteLine("VoxPen CLI");
Console.WriteLine($"Mode           : {mode}");
Console.WriteLine($"Model dir      : {Path.GetFullPath(modelDir)}");

// 不需要模型的冒烟命令，先短路
if (mode == "test-postprocess") return RunPostProcessTest();
if (mode == "test-hotword") return RunHotwordTest();
if (mode == "test-diary") return RunDiaryTest();
if (mode == "test-merger") return RunMergerTest();
if (mode == "test-punc") return await RunPuncTestAsync(punctModelDir, puncTestText);

var config = new AppConfig
{
    Asr = { Engine = engineKind, ModelDir = modelDir, NumThreads = 2, Provider = "cpu" },
    Output = { Mode = paste ? OutputMode.Paste : OutputMode.Type, RestoreClipboard = true },
    Shortcut = { Key = "caps_lock", Suppress = suppress, ShortPressThresholdSeconds = 0.3 },
};

using IAsrEngine engine = WindowsAsrEngineFactory.Create(config.Asr);

Console.Write($"Loading {engine.Name} model ... ");
var loadSw = Stopwatch.StartNew();
try
{
    await engine.LoadAsync();
}
catch (Exception ex)
{
    Console.WriteLine("FAILED");
    Console.WriteLine();
    Console.WriteLine(ex.Message);
    return 2;
}
loadSw.Stop();
Console.WriteLine($"OK ({loadSw.ElapsedMilliseconds} ms)");

if (mode == "file")
{
    return await RunFileModeAsync(filePath!, engine);
}
if (mode == "transcribe")
{
    return await RunTranscribeModeAsync(engine, transcribeInputs, transcribeConfig);
}
if (mode == "interactive")
{
    return await RunInteractiveModeAsync(engine, deviceName);
}
return await RunLiveModeAsync(engine, config, deviceName, autoExitSecs);


static int RunPostProcessTest()
{
    Console.WriteLine();
    Console.WriteLine("== Post-process smoke test (HotRuleReplacer + TrashPuncCleaner) ==");

    var hotRulePath = Path.Combine(AppContext.BaseDirectory, "hot-rule.txt");
    Console.WriteLine($"hot-rule.txt : {hotRulePath}  (exists={File.Exists(hotRulePath)})");

    var hot = VoxPen.Core.Hotword.HotRuleReplacer.Load(hotRulePath);
    Console.WriteLine($"Rules loaded : {hot.RuleCount}");

    var cases = new (string input, string expectSubstring)[]
    {
        ("三百毫安时", "mAh"),
        ("采样率四十四点一千赫兹", "Hz"),
        ("五伏特电源", "V"),
        ("负一度", "-1"),
        ("艾特 QQ 点 邮箱", "@qq."),
        ("欧拉玛服务", "Ollama"),
    };
    int passed = 0;
    foreach (var (input, expect) in cases)
    {
        var output = hot.Apply(input);
        var ok = output.Contains(expect);
        if (ok) passed++;
        Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] '{input}' -> '{output}' (expected substring: '{expect}')");
    }

    var punc = new VoxPen.Core.Postprocess.TrashPuncCleaner(
        trashPunctuation: "，。,.",
        threshold: 8,
        forceApps: new[] { "WeiXin.exe" });

    var puncCases = new (string input, string? exe, string expected)[]
    {
        ("你好，", null, "你好"),        // 短文本 -> 清
        ("你好世界。", null, "你好世界"),  // 短文本 -> 清
        ("这是一段够长够长够长够长的中文文本，", null, "这是一段够长够长够长够长的中文文本，"), // 达阈值 -> 保留
        ("很长的正常句子。", "WeiXin.exe", "很长的正常句子"), // 强制清理
    };
    int puncPassed = 0;
    foreach (var (input, exe, expected) in puncCases)
    {
        var output = punc.Apply(input, exe);
        var ok = output == expected;
        if (ok) puncPassed++;
        Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] punc '{input}' (exe={exe ?? "-"}) -> '{output}' (expected '{expected}')");
    }

    Console.WriteLine();
    Console.WriteLine($"HotRule cases: {passed}/{cases.Length}");
    Console.WriteLine($"TrashPunc cases: {puncPassed}/{puncCases.Length}");
    return (passed == cases.Length && puncPassed == puncCases.Length) ? 0 : 1;
}


static async Task<int> RunPuncTestAsync(string modelDir, string text)
{
    Console.WriteLine();
    Console.WriteLine("== Punctuation model smoke test (SherpaPunctuator) ==");

    // 允许传目录或直接传 model.onnx；ModelDirectoryResolver 沿父目录回溯，便于开发运行时定位
    var resolved = VoxPen.Core.Config.ModelDirectoryResolver.Resolve(AppContext.BaseDirectory, modelDir);
    // 已带 .onnx 扩展名：直接用；否则按目录处理，追加 model.onnx（与 AppHost.ResolvePunctuator 一致）
    var modelFile = resolved.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)
        ? resolved
        : Path.Combine(resolved, "model.onnx");

    Console.WriteLine($"  model dir : {resolved}");
    Console.WriteLine($"  model file: {modelFile}  (exists={File.Exists(modelFile)})");

    if (!File.Exists(modelFile))
    {
        Console.WriteLine();
        Console.WriteLine("FAIL: 未找到 model.onnx；请下载 sherpa-onnx-punct-ct-transformer-*，并放到");
        Console.WriteLine("      models/Punct-CT-Transformer/... 目录。");
        return 2;
    }

    using var punctuator = new VoxPen.Platform.Windows.Recognition.SherpaPunctuator(modelFile, numThreads: 2, provider: "cpu");
    Console.Write("Loading punctuation model ... ");
    var sw = Stopwatch.StartNew();
    try
    {
        await punctuator.LoadAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine("FAILED");
        Console.WriteLine(ex.Message);
        return 3;
    }
    sw.Stop();
    Console.WriteLine($"OK ({sw.ElapsedMilliseconds} ms)");

    Console.WriteLine();
    Console.WriteLine($"[in ] {text}");
    var t = Stopwatch.StartNew();
    var output = punctuator.AddPunctuation(text);
    t.Stop();
    Console.WriteLine($"[out] {output}");
    Console.WriteLine($"[t  ] {t.ElapsedMilliseconds} ms");

    // 冒烟判定：输出应非空，且长度不小于输入（标点只增不减）
    var okNonEmpty = !string.IsNullOrWhiteSpace(output);
    var okLen = output.Length >= text.Length;
    Console.WriteLine();
    Console.WriteLine($"  [{(okNonEmpty ? "OK" : "FAIL")}] 输出非空");
    Console.WriteLine($"  [{(okLen ? "OK" : "FAIL")}] 输出长度 ≥ 输入长度（{output.Length} vs {text.Length}）");
    return (okNonEmpty && okLen) ? 0 : 1;
}


static async Task<int> RunFileModeAsync(string path, IAsrEngine engine)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"File not found: {path}");
        return 3;
    }
    Console.WriteLine($"Recognizing file: {path}");
    var (samples, sr) = WavReader.ReadAsFloatMono(path);
    if (sr != engine.SampleRate)
    {
        Console.WriteLine($"WARN: file sample rate {sr} Hz, engine expects {engine.SampleRate} Hz.");
    }
    var result = await engine.RecognizeAsync(samples);
    Console.WriteLine($"[text  ] {result.Text}");
    Console.WriteLine($"[timing] {result.Elapsed.TotalMilliseconds:F0} ms / {samples.Length / (float)sr:F2} s audio");
    return 0;
}

static async Task<int> RunInteractiveModeAsync(IAsrEngine engine, string? deviceName)
{
    Console.WriteLine();
    using var capture = new WindowsAudioCapture(deviceName);
    Console.WriteLine("Input devices:");
    foreach (var d in capture.ListInputDevices()) Console.WriteLine($"  - {d}");
    Console.WriteLine();
    Console.WriteLine("Press Enter to start, Enter again to stop, 'q'+Enter to quit.");

    var buffer = new List<float>();
    var lockObj = new object();
    capture.ChunkAvailable += (_, e) => { lock (lockObj) buffer.AddRange(e.Samples); };

    while (true)
    {
        var line = Console.ReadLine();
        if (line is null || line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase)) break;

        lock (lockObj) buffer.Clear();
        capture.Start();
        var sw = Stopwatch.StartNew();
        Console.WriteLine("● Recording ... Enter to stop.");
        Console.ReadLine();
        capture.Stop();
        sw.Stop();

        float[] captured;
        lock (lockObj) captured = buffer.ToArray();
        Console.WriteLine($"■ {captured.Length / (float)capture.SampleRate:F2} s captured.");

        var wav = Path.Combine(Path.GetTempPath(), $"cws-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
        await WavWriter.SaveMono16kAsync(wav, captured, capture.SampleRate);
        Console.WriteLine($"  wav: {wav}");

        var r = await engine.RecognizeAsync(captured);
        Console.WriteLine($"[text  ] {r.Text}");
        Console.WriteLine($"[timing] rec {sw.Elapsed.TotalSeconds:F1}s  asr {r.Elapsed.TotalMilliseconds:F0}ms");
        Console.WriteLine();
    }
    return 0;
}

static async Task<int> RunLiveModeAsync(IAsrEngine engine, AppConfig config, string? deviceName, int autoExitSecs)
{
    using var capture = new WindowsAudioCapture(deviceName);
    var shortcutKeys = config.Shortcut.Keys.Count > 0 ? config.Shortcut.Keys : [config.Shortcut.Key];
    using var hotkey = new WindowsGlobalHotkey(shortcutKeys, config.Shortcut.Suppress);
    var textOutput = new WindowsTextOutput();
    var foreground = new WindowsForegroundApp();

    using var pipeline = new DictationPipeline(
        hotkey, capture, engine, textOutput, config, foreground);

    // 短按补发：只对 toggle 键（caps/num/scroll_lock）生效；非 toggle 键静默丢弃
    pipeline.ShortPressDetected += (_, keyName) => textOutput.ResendToggleKey(keyName);

    pipeline.StateChanged += (_, s) =>
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.WriteLine($"[{stamp}] state → {s}");
    };
    pipeline.TextRecognized += (_, text) =>
    {
        Console.WriteLine($"[TEXT] {text}");
    };

    pipeline.Start();

    Console.WriteLine();
    Console.WriteLine("Live mode running. 按住 CapsLock 说话，松开上屏。");
    Console.WriteLine($"  suppress = {config.Shortcut.Suppress}   paste = {config.Output.Mode}");
    Console.WriteLine("  Ctrl+C 退出。");

    var exitEvent = new ManualResetEventSlim();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; exitEvent.Set(); };
    if (autoExitSecs > 0)
    {
        Console.WriteLine($"  (auto-exit after {autoExitSecs}s for smoke test)");
        _ = Task.Run(async () => { await Task.Delay(autoExitSecs * 1000); exitEvent.Set(); });
    }
    exitEvent.Wait();
    Console.WriteLine("Bye.");
    return 0;
}

static void PrintHelp()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  VoxPen.Cli                            interactive mic → ASR (stdin trigger)");
    Console.WriteLine("  VoxPen.Cli run [--paste] [--no-suppress]");
    Console.WriteLine("                                                 live: hold CapsLock to dictate");
    Console.WriteLine("  VoxPen.Cli --file <path.wav>          recognize a WAV file");
    Console.WriteLine("  VoxPen.Cli transcribe <files...>      batch transcribe (60s/4s overlap)");
    Console.WriteLine("       [--seg-duration N] [--seg-overlap N] [--no-srt] [--no-json] [--no-txt] [--merge]");
    Console.WriteLine("  VoxPen.Cli test-postprocess           smoke: hot-rule + trash-punc");
    Console.WriteLine("  VoxPen.Cli test-punc [\"<text>\"]       smoke: CT-Transformer punctuation model");
    Console.WriteLine("  VoxPen.Cli test-hotword               smoke: phoneme RAG");
    Console.WriteLine("  VoxPen.Cli test-diary                 smoke: diary writer");
    Console.WriteLine("  VoxPen.Cli test-merger                smoke: segment merger");
    Console.WriteLine("  VoxPen.Cli --engine <name>            paraformer / sensevoice / fun_asr_nano / qwen_asr");
    Console.WriteLine("  VoxPen.Cli --device <name substring>  pick input device");
}

// -------------- P7 新增：批量转录 --------------

static async Task<int> RunTranscribeModeAsync(IAsrEngine engine, List<string> files, TranscribeConfig cfg)
{
    if (files.Count == 0)
    {
        Console.Error.WriteLine("transcribe: no input files.");
        return 3;
    }

    // hot-rule.txt + hot.txt 走应用根目录
    var baseDir = AppContext.BaseDirectory;
    var hotRulePath = Path.Combine(baseDir, "hot-rule.txt");
    var hotwordPath = Path.Combine(baseDir, "hot.txt");
    var hotRule = HotRuleReplacer.Load(hotRulePath);
    Console.WriteLine($"  hot-rule.txt : {hotRule.RuleCount} 条");

    PhonemeCorrector? phonemeCorrector = null;
    if (File.Exists(hotwordPath))
    {
        try
        {
            var pc = new PhonemeCorrector();
            pc.UpdateHotwordsFromText(File.ReadAllText(hotwordPath));
            phonemeCorrector = pc;
            Console.WriteLine($"  hot.txt      : {pc.HotwordCount} 条");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  hot.txt load failed: {ex.Message}");
        }
    }

    // 简单日志适配
    var logSink = new ConsoleLogSink();
    // 组装解码器：优先 MediaFoundation（支持 mp3/m4a/wav 等），失败退到 WavAudioDecoder
    IAudioDecoder decoder;
    try
    {
        decoder = new VoxPen.Platform.Windows.Audio.MediaFoundationAudioDecoder();
    }
    catch
    {
        decoder = WavAudioDecoder.Instance;
    }

    var transcriber = new FileTranscriber(engine, decoder, cfg, hotRule, phonemeCorrector, logSink);

    int ok = 0, fail = 0;
    foreach (var f in files)
    {
        Console.WriteLine();
        Console.WriteLine($"== {f} ==");
        try
        {
            var res = await transcriber.TranscribeAsync(f);
            Console.WriteLine($"  text: {res.Text}");
            foreach (var w in res.WrittenFiles)
            {
                Console.WriteLine($"  wrote: {w}");
            }
            ok++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAILED: {ex.Message}");
            fail++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Done. ok={ok}  failed={fail}");
    return fail == 0 ? 0 : 4;
}

// -------------- P7 新增：冒烟命令 --------------

static int RunHotwordTest()
{
    Console.WriteLine();
    Console.WriteLine("== Phoneme RAG smoke test ==");
    var hotSrc = string.Join("\n", new[]
    {
        "# 内置样例 hot.txt",
        "撒贝宁",
        "北大青鸟",
        "iPhone",
        "西安",
        "先|xiān|xian",
        "GitHub",
    });

    var corrector = new PhonemeCorrector(threshold: 0.75);
    var loaded = corrector.UpdateHotwordsFromText(hotSrc);
    Console.WriteLine($"  loaded {loaded} hotwords");

    var cases = new (string input, string expected)[]
    {
        ("撒贝宁在央视工作", "撒贝宁"),
        ("我去西安玩", "西安"),
        ("苹果发布了iphone", "iPhone"),
        ("上传到github", "GitHub"),
    };
    int pass = 0;
    foreach (var (input, expected) in cases)
    {
        var result = corrector.Correct(input);
        var ok = result.Text.Contains(expected);
        if (ok) pass++;
        Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] '{input}' -> '{result.Text}' (need '{expected}')");
    }
    Console.WriteLine($"Cases: {pass}/{cases.Length}");
    return pass == cases.Length ? 0 : 1;
}

static int RunDiaryTest()
{
    Console.WriteLine();
    Console.WriteLine("== Diary writer smoke test ==");
    var tmp = Path.Combine(Path.GetTempPath(), "cws-diary-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tmp);
    try
    {
        var diary = new DiaryWriter(tmp);
        var ts = new DateTime(2026, 7, 9, 12, 34, 56);
        var mdPath = diary.Write("测试内容 hello", ts);
        var mdPath2 = diary.Write("第二条", ts.AddMinutes(1),
            audioPath: Path.Combine(tmp, "2026", "07", "assets", "clip.wav"));

        if (mdPath != mdPath2)
        {
            Console.WriteLine($"  FAIL: expected same path, got {mdPath} vs {mdPath2}");
            return 1;
        }
        var content = File.ReadAllText(mdPath);
        Console.WriteLine("  --- md content ---");
        Console.WriteLine(content);
        Console.WriteLine("  --- end ---");

        var checks = new (string label, bool ok)[]
        {
            ("首次写入含 header", content.Contains("正则表达式 Tip")),
            ("含首条文本", content.Contains("测试内容 hello")),
            ("含第二条", content.Contains("第二条")),
            ("含时间戳", content.Contains("12:34:56")),
            ("含相对音频链接", content.Contains("assets/clip.wav")),
            ("header 只出现一次", content.Split("正则表达式 Tip").Length == 2),
        };
        int pass = 0;
        foreach (var (label, ok) in checks)
        {
            if (ok) pass++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {label}");
        }
        return pass == checks.Length ? 0 : 1;
    }
    finally
    {
        try { Directory.Delete(tmp, recursive: true); } catch { }
    }
}

static int RunMergerTest()
{
    Console.WriteLine();
    Console.WriteLine("== Segment merger smoke test ==");

    // 三段重叠文本
    var a = "你好世界这是第一段落尾";
    var b = "落尾第二段中";
    var c = "段中第三段末";
    var m1 = VoxPen.Core.Transcribe.SegmentMerger.MergeText(a, b);
    var m2 = VoxPen.Core.Transcribe.SegmentMerger.MergeText(m1, c);
    Console.WriteLine($"  step1: '{m1}'");
    Console.WriteLine($"  step2: '{m2}'");

    // 相邻段重叠应被吸收
    var luoWei = System.Text.RegularExpressions.Regex.Matches(m2, "落尾").Count;
    var duanZhong = System.Text.RegularExpressions.Regex.Matches(m2, "段中").Count;
    var checks = new (string label, bool ok)[]
    {
        ("包含 '你好世界'", m2.Contains("你好世界")),
        ("包含 '第三段末'", m2.Contains("第三段末")),
        ("重叠 '落尾' ≤1 次", luoWei <= 1),
        ("重叠 '段中' ≤1 次", duanZhong <= 1),
    };
    int pass = 0;
    foreach (var (label, ok) in checks)
    {
        if (ok) pass++;
        Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {label}");
    }

    // Token 拼接冒烟：三段 token+timestamp
    var t1 = VoxPen.Core.Transcribe.SegmentMerger.MergeTokens(
        Array.Empty<string>(), Array.Empty<float>(),
        new[] { "你", "好", "世", "界" }, new[] { 0f, 0.2f, 0.4f, 0.6f },
        offsetSeconds: 0, overlapSeconds: 4, isFirstSegment: true);
    var t2 = VoxPen.Core.Transcribe.SegmentMerger.MergeTokens(
        t1.Tokens, t1.Timestamps,
        new[] { "世", "界", "你", "在" }, new[] { 0f, 0.2f, 0.4f, 0.6f },
        offsetSeconds: 0.4, overlapSeconds: 4, isFirstSegment: false);
    Console.WriteLine($"  tokens after 2 segs: [{string.Join(" ", t2.Tokens)}]");
    var tokenOk = string.Concat(t2.Tokens).Contains("你好世界你在");
    Console.WriteLine($"  [{(tokenOk ? "OK" : "FAIL")}] token merge deduped overlap");
    if (tokenOk) pass++;

    var total = checks.Length + 1;
    Console.WriteLine($"Cases: {pass}/{total}");
    return pass == total ? 0 : 1;
}

sealed class ConsoleLogSink : FileTranscriber.ILogSink
{
    public void OnInfo(string message) => Console.WriteLine(message);
    public void OnWarn(string message) => Console.WriteLine("[warn] " + message);
}
