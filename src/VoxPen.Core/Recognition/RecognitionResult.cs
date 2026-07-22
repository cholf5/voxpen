namespace VoxPen.Core.Recognition;

/// <summary>
/// ASR 识别结果。MVP 阶段只关心 <see cref="Text"/>；预留 tokens/timestamps 供 v2 文件转录用。
/// </summary>
public sealed class RecognitionResult
{
    /// <summary>
    /// 识别出的文本（未经过热词/规则处理；是否已包含标点取决于引擎能力：
    /// 参见 <see cref="Abstractions.IAsrEngine.Capabilities"/> 中的
    /// <see cref="Abstractions.EngineCapabilities.Punctuation"/> 位）。
    /// </summary>
    public required string Text { get; init; }

    /// <summary>字级 token 列表（可为空数组）。</summary>
    public IReadOnlyList<string> Tokens { get; init; } = Array.Empty<string>();

    /// <summary>字级时间戳（秒，与 Tokens 一一对应；可为空数组）。</summary>
    public IReadOnlyList<float> Timestamps { get; init; } = Array.Empty<float>();

    /// <summary>识别耗时（不含 IO/模型加载）。</summary>
    public TimeSpan Elapsed { get; init; }

    public static readonly RecognitionResult Empty = new() { Text = string.Empty };
}
