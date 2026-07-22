namespace VoxPen.Core.Abstractions;

/// <summary>
/// 文本标点补全器抽象。给一段无标点或半标点的 ASR 文本补上标点（如 <c>，。？！</c>）。
///
/// 装配约定：当所选 <see cref="IAsrEngine"/> 的 <see cref="IAsrEngine.Capabilities"/>
/// 已包含 <see cref="EngineCapabilities.Punctuation"/> 时应跳过外挂标点模型，注入
/// 空实现（例如 <c>NullPunctuator</c>）。
///
/// 线程模型：<see cref="AddPunctuation"/> 需支持并发调用，或由调用方外部串行化。
/// 当前 sherpa-onnx 的 OfflinePunctuation 是线程安全的短请求，直接并发即可。
/// </summary>
public interface IPunctuator : IDisposable
{
    /// <summary>实现名，用于日志。例如 "ct-transformer-onnx" 或 "null"。</summary>
    string Name { get; }

    /// <summary>是否已完成模型加载（空实现始终为 <c>true</c>）。</summary>
    bool IsLoaded { get; }

    /// <summary>加载模型。空实现应立刻返回已完成任务。</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 给一段文本补标点。空串/空白直接原样返回；模型未加载时也应原样返回，
    /// 以便调用方在降级路径下无需额外判断。
    /// </summary>
    string AddPunctuation(string text);
}
