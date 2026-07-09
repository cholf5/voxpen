using System.Diagnostics;
using CapsWriterSharp.Cli;
using CapsWriterSharp.Core.Abstractions;
using CapsWriterSharp.Core.Config;
using CapsWriterSharp.Core.Pipeline;
using CapsWriterSharp.Core.Storage;
using CapsWriterSharp.Platform.Windows.Audio;
using CapsWriterSharp.Platform.Windows.Hooks;
using CapsWriterSharp.Platform.Windows.Recognition;
using CapsWriterSharp.Platform.Windows.Text;

// -------- 参数 --------
// 无参数 / interactive : 交互式录音 → 识别（P2 用；stdin 触发）
// --file <path>        : 直接识别 wav（P2 用）
// run                  : 常驻监听 CapsLock，按住说话松开上屏（P3 端到端）
// --model <dir>        : 覆盖模型目录
// --device <name>      : 偏好的输入设备名
// --paste              : run 模式下默认使用剪贴板粘贴而非模拟打字
// --no-suppress        : run 模式下不抑制 CapsLock 默认行为（调试用）
string? filePath = null;
string modelDir = "models/paraformer";
string? deviceName = null;
string mode = "interactive";
bool paste = false;
bool suppress = true;
int autoExitSecs = 0;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "run": mode = "run"; break;
        case "interactive": mode = "interactive"; break;
        case "test-postprocess": mode = "test-postprocess"; break;
        case "--file" when i + 1 < args.Length: filePath = args[++i]; mode = "file"; break;
        case "--model" when i + 1 < args.Length: modelDir = args[++i]; break;
        case "--device" when i + 1 < args.Length: deviceName = args[++i]; break;
        case "--paste": paste = true; break;
        case "--no-suppress": suppress = false; break;
        case "--auto-exit-secs" when i + 1 < args.Length: autoExitSecs = int.Parse(args[++i]); break;
        case "-h" or "--help": PrintHelp(); return 0;
    }
}

Console.WriteLine("CapsWriter-Sharp CLI");
Console.WriteLine($"Mode           : {mode}");
Console.WriteLine($"Model dir      : {Path.GetFullPath(modelDir)}");

if (mode == "test-postprocess")
{
    return RunPostProcessTest();
}

var config = new AppConfig
{
    Asr = { ModelDir = modelDir, NumThreads = 2, Provider = "cpu" },
    Output = { Mode = paste ? OutputMode.Paste : OutputMode.Type, RestoreClipboard = true },
    Shortcut = { Key = "caps_lock", Suppress = suppress, ShortPressThresholdSeconds = 0.3 },
};

using IAsrEngine engine = new ParaformerEngine(config.Asr);

Console.Write("Loading Paraformer model ... ");
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

    var hot = CapsWriterSharp.Core.Hotword.HotRuleReplacer.Load(hotRulePath);
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

    var punc = new CapsWriterSharp.Core.Postprocess.TrashPuncCleaner(
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
    using var hotkey = new WindowsGlobalHotkey(config.Shortcut.Key, config.Shortcut.Suppress);
    var textOutput = new WindowsTextOutput();
    var foreground = new WindowsForegroundApp();

    using var pipeline = new DictationPipeline(
        hotkey, capture, engine, textOutput, config, foreground);

    // 短按补发（只针对 CapsLock 有意义；其他键暂用同接口 stub）
    pipeline.ShortPressDetected += (_, _) =>
    {
        if (string.Equals(config.Shortcut.Key, "caps_lock", StringComparison.OrdinalIgnoreCase))
        {
            textOutput.ResendCapsLock();
        }
    };

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
    Console.WriteLine("  CapsWriterSharp.Cli                            interactive mic → ASR (stdin trigger)");
    Console.WriteLine("  CapsWriterSharp.Cli run [--paste] [--no-suppress]");
    Console.WriteLine("                                                 live: hold CapsLock to dictate");
    Console.WriteLine("  CapsWriterSharp.Cli --file <path.wav>          recognize a WAV file");
    Console.WriteLine("  CapsWriterSharp.Cli --model <dir>              override model directory");
    Console.WriteLine("  CapsWriterSharp.Cli --device <name substring>  pick input device");
}
