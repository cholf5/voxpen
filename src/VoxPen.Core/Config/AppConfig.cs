using System.Text.Json.Serialization;

namespace VoxPen.Core.Config;

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

    /// <summary>批量文件转录设置。</summary>
    public TranscribeConfig Transcribe { get; set; } = new();

    /// <summary>音素 RAG 热词设置（hot.txt）。</summary>
    public HotwordConfig Hotword { get; set; } = new();

    /// <summary>系统通知（Toast）设置。</summary>
    public NotificationConfig Notification { get; set; } = new();

    /// <summary>日志级别：Trace/Debug/Info/Warn/Error。</summary>
    public string LogLevel { get; set; } = "Information";
}

public sealed class ShortcutConfig
{
    /// <summary>抽象键名，例如 "caps_lock"。当 <see cref="Keys"/> 为空时启用。</summary>
    public string Key { get; set; } = "caps_lock";

    /// <summary>
    /// 多快捷键绑定，例如 <c>["caps_lock", "x2"]</c>。非空时优先于 <see cref="Key"/>。
    /// 支持 <c>caps_lock/f13/f14/f15/f16/x1/x2</c> 等；具体清单由平台层的 KeyNameMapper 决定。
    /// </summary>
    public List<string> Keys { get; set; } = new();

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

    /// <summary>是否把每次识别追加写入 <c>recordings/YYYY/MM/DD.md</c> 日记。</summary>
    public bool DiaryEnabled { get; set; } = true;
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

public sealed class TranscribeConfig
{
    /// <summary>批量转录时每段音频长度（秒）。</summary>
    public double SegDurationSeconds { get; set; } = 60.0;

    /// <summary>相邻段重叠长度（秒），用于稳健拼接。</summary>
    public double SegOverlapSeconds { get; set; } = 4.0;

    /// <summary>是否生成 SRT 字幕。</summary>
    public bool SaveSrt { get; set; } = true;

    /// <summary>是否生成分行 TXT。</summary>
    public bool SaveTxt { get; set; } = true;

    /// <summary>是否生成含 timestamps/tokens 的 JSON。</summary>
    public bool SaveJson { get; set; } = true;

    /// <summary>是否额外生成一行 <c>.merge.txt</c>（未分行原文）。</summary>
    public bool SaveMerge { get; set; } = false;
}

public sealed class HotwordConfig
{
    /// <summary>是否启用音素 RAG 热词纠错。</summary>
    public bool EnablePhonemeRag { get; set; } = true;

    /// <summary>hot.txt 文件路径（相对应用根目录）。</summary>
    public string HotwordPath { get; set; } = "hot.txt";

    /// <summary>匹配阈值（≥ 此值视为真实替换）。</summary>
    public double MatchThreshold { get; set; } = 0.85;

    /// <summary>相似阈值（≥ 此值仅提示，不替换）。</summary>
    public double SimilarThreshold { get; set; } = 0.6;
}

public sealed class NotificationConfig
{
    /// <summary>是否启用系统 Toast 通知。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>录音开始时弹通知（默认关，避免干扰）。</summary>
    public bool ShowOnRecordingStart { get; set; } = false;

    /// <summary>识别完成时弹通知。</summary>
    public bool ShowOnResult { get; set; } = true;

    /// <summary>发生错误时弹通知。</summary>
    public bool ShowOnError { get; set; } = true;
}
