namespace VoxPen.Core.Abstractions;

/// <summary>
/// 文字上屏抽象。平台特定实现负责把文本注入前台窗口。
/// </summary>
public interface ITextOutput
{
    /// <summary>逐字模拟键盘输入（Unicode）。适合大多数普通输入框。</summary>
    /// <param name="text">要输入的文本。</param>
    /// <param name="cancellationToken">支持取消长文本输入。</param>
    Task TypeAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>写剪贴板并模拟 Ctrl+V 粘贴。适合聊天软件等抵制 Unicode input 的场景。</summary>
    /// <param name="text">要粘贴的文本。</param>
    /// <param name="restoreClipboard">粘贴后是否恢复原剪贴板内容。</param>
    Task PasteAsync(string text, bool restoreClipboard);
}
