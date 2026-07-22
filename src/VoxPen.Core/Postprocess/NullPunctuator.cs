using VoxPen.Core.Abstractions;

namespace VoxPen.Core.Postprocess;

/// <summary>
/// <see cref="IPunctuator"/> 的空实现：不做任何修改，原样返回文本。
///
/// 使用场景：
/// - ASR 引擎自带标点（<see cref="EngineCapabilities.Punctuation"/>），无需外挂标点模型
/// - 用户未配置或删除了标点模型目录
/// - 外挂标点模型加载失败，走"无标点模式"降级
///
/// 无状态，可用单例 <see cref="Instance"/>。
/// </summary>
public sealed class NullPunctuator : IPunctuator
{
    public static readonly NullPunctuator Instance = new();

    private NullPunctuator() { }

    public string Name => "null";
    public bool IsLoaded => true;

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public string AddPunctuation(string text) => text;

    public void Dispose() { /* 无资源可释放 */ }
}
