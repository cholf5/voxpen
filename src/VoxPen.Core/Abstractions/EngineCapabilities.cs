namespace VoxPen.Core.Abstractions;

/// <summary>
/// ASR 引擎自身具备的能力位。用于让 <see cref="VoxPen.Core.Abstractions.IPunctuator"/>
/// 等后处理组件按引擎能力做出装配决策。
///
/// 例如 Paraformer 不自带标点，需要外挂标点模型；SenseVoice / Fun-ASR-Nano / Qwen3-ASR
/// 自带标点，则应跳过外挂标点模型的加载。
///
/// 命名与上游 Python 项目 <c>core/server/engines/base.py::EngineCapabilities</c> 对齐。
/// </summary>
[Flags]
public enum EngineCapabilities
{
    /// <summary>无附加能力。</summary>
    None = 0,

    /// <summary>识别文本自带标点（无需外挂标点模型）。</summary>
    Punctuation = 1 << 0,

    /// <summary>可产出字级时间戳（Tokens / Timestamps）。</summary>
    Timestamps = 1 << 1,

    /// <summary>支持流式识别（预留，MVP 尚未接入）。</summary>
    Streaming = 1 << 2,

    /// <summary>支持热词偏置（预留，用于对应上游 hot-server.txt）。</summary>
    Hotwords = 1 << 3,
}
