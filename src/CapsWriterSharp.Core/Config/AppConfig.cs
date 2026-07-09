using System.Text.Json.Serialization;

namespace CapsWriterSharp.Core.Config;

/// <summary>
/// 应用配置，序列化到 config.json。默认值参考原项目 config_client.py。
/// </summary>
public sealed class AppConfig
{
    /// <summary>快捷键设置。</summary>
    public ShortcutConfig Shortcut { get; set; } = new();

    /// <summary>音频采集设置。</summary>
    public AudioConfig Audio { get; set; } = new();

    /// <summary>ASR 引擎设置。</summary>
    public AsrConfig Asr { get; set; } = new();

    /// <summary>上屏设置。</summary>
    public OutputConfig Output { get; set; } = new();

    /// <summary>后处理设置（末尾标点、hot-rule 等）。</summary>
    public PostprocessConfig Postprocess { get; set; } = new();

    /// <summary>日志级别：Trace/Debug/Info/Warn/Error。</summary>
    public string LogLevel { get; set; } = "Information";
}

public sealed class ShortcutConfig
{
    /// <summary>抽象键名，例如 "caps_lock"。</summary>
    public string Key { get; set; } = "caps_lock";

    /// <summary>是否抑制系统默认行为（CapsLock 不切换大小写）。</summary>
    public bool Suppress { get; set; } = true;

    /// <summary>短按阈值（秒）。低于此值视为短按，会自动补发原按键。</summary>
    public double ShortPressThresholdSeconds { get; set; } = 0.3;
}

public sealed class AudioConfig
{
    /// <summary>输入设备名。null 表示系统默认设备。</summary>
    public string? InputDevice { get; set; }

    /// <summary>是否保存原始录音到本地归档。</summary>
    public bool SaveRecording { get; set; } = true;

    /// <summary>录音文件名保留识别结果前多少个字。</summary>
    public int AudioNameLength { get; set; } = 20;
}

public sealed class AsrConfig
{
    /// <summary>ASR 引擎类型（当前仅 "paraformer"）。</summary>
    public string Engine { get; set; } = "paraformer";

    /// <summary>模型目录（含 model.int8.onnx / tokens.txt）。</summary>
    public string ModelDir { get; set; } = "models/paraformer";

    /// <summary>推理线程数。</summary>
    public int NumThreads { get; set; } = 2;

    /// <summary>Provider：cpu / directml（当前 MVP 只用 cpu）。</summary>
    public string Provider { get; set; } = "cpu";
}

public sealed class OutputConfig
{
    /// <summary>默认上屏模式：Type（模拟打字）或 Paste（剪贴板粘贴）。</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OutputMode Mode { get; set; } = OutputMode.Type;

    /// <summary>粘贴后是否恢复原剪贴板。</summary>
    public bool RestoreClipboard { get; set; } = true;

    /// <summary>命中这些进程时强制使用粘贴模式（例如微信、Telegram）。</summary>
    public List<string> PasteApps { get; set; } = new() { "WeiXin.exe", "Telegram.exe" };
}

public enum OutputMode
{
    Type,
    Paste,
}

public sealed class PostprocessConfig
{
    /// <summary>启用 hot-rule.txt 正则规则替换。</summary>
    public bool EnableHotRule { get; set; } = true;

    /// <summary>hot-rule.txt 文件路径（相对应用根目录）。</summary>
    public string HotRulePath { get; set; } = "hot-rule.txt";

    /// <summary>要清理的末尾标点字符集合。</summary>
    public string TrashPunctuation { get; set; } = "，。,.";

    /// <summary>文本长度低于此值时强制清理末尾标点。</summary>
    public int TrashPuncThreshold { get; set; } = 8;

    /// <summary>对这些进程强制清理末尾标点。</summary>
    public List<string> TrashPuncApps { get; set; } = new() { "WeiXin.exe" };
}
