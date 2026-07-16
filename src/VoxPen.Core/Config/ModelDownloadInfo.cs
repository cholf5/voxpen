namespace VoxPen.Core.Config;

/// <summary>
/// Paraformer 模型下载相关的公共文案 / URL。
/// UI（红色横幅、设置页状态行）、启动异常格式化、Toast 通知都从这里取值，
/// 保持"缺模型时该显示什么"的口径一致。
/// </summary>
public static class ModelDownloadInfo
{
    /// <summary>官方 Release 页面（Paraformer.zip 就在这里）。</summary>
    public const string DownloadUrl =
        "https://github.com/HaujetZhao/CapsWriter-Offline/releases/tag/models";

    /// <summary>Release 页面上的资产名，方便用户在页面里定位。</summary>
    public const string PackageName = "Paraformer.zip";

    /// <summary>红色横幅 / 日志共用的多行提醒。</summary>
    public static string FormatMissingModelHint(string modelDir, string reason)
    {
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? "未检测到模型文件" : reason.Trim();
        return
            $"未检测到 Paraformer 语音模型：{trimmedReason}\n" +
            $"请下载 {PackageName} 并解压到：{modelDir}\n" +
            $"下载地址：{DownloadUrl}\n" +
            "解压后目录内至少需要包含 model.onnx（或 model.int8.onnx）与 tokens.txt，" +
            "详细步骤见 README.md。";
    }

    /// <summary>Toast 用的短文案。</summary>
    public static (string Title, string Body) FormatToast(string modelDir)
    {
        var title = "未检测到 Paraformer 模型";
        var body =
            $"请下载 {PackageName} 到 {modelDir}\n" +
            DownloadUrl;
        return (title, body);
    }
}
