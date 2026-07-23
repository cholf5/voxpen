namespace VoxPen.Core.Config;

/// <summary>
/// CT-Transformer 标点模型下载相关的公共文案 / URL。
/// UI（设置页红色横幅、状态行）与日志共用，保持口径一致。
///
/// 与 <see cref="ModelDownloadInfo"/> 分开维护：Paraformer 走 CapsWriter-Offline 的
/// Release 页面（附国内网盘镜像），标点模型走上游 sherpa-onnx 官方 Release，两者的
/// 包名与下载源都不一样。
/// </summary>
public static class PunctuationModelDownloadInfo
{
    /// <summary>官方模型包直链。</summary>
    public const string DownloadUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/punctuation-models/" + PackageName;

    /// <summary>Release 页面上的资产名，方便用户在页面里定位。</summary>
    public const string PackageName =
        "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12.tar.bz2";

    /// <summary>红色横幅 / 日志共用的多行提醒。</summary>
    public static string FormatMissingModelHint(string modelDir, string reason)
    {
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? "未检测到标点模型文件" : reason.Trim();
        return
            $"未检测到 CT-Transformer 标点模型：{trimmedReason}\n" +
            $"请下载 {PackageName} 并解压到：{modelDir}\n" +
            $"下载地址：{DownloadUrl}\n" +
            "解压后目录内至少需要包含 model.onnx。缺此模型不影响主功能，但识别结果将没有自动标点。";
    }
}
