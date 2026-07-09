namespace CapsWriterSharp.Core.Abstractions;

/// <summary>
/// 前台应用探测抽象。用于按目标应用切换上屏策略（paste_apps、enter_apps、trash_punc_apps）。
/// </summary>
public interface IForegroundApp
{
    /// <summary>
    /// 当前前台窗口所属进程的可执行文件名（含扩展名，例如 "WeiXin.exe"）。
    /// 若无法获取则返回 null。
    /// </summary>
    string? GetForegroundExeName();
}
