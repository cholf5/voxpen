using VoxPen.Core.Config;
using Xunit;

namespace VoxPen.Core.Tests.Config;

public sealed class ModelDownloadInfoTests
{
    [Fact]
    public void MissingModelHintIncludesUrlDirectoryAndReason()
    {
        var hint = ModelDownloadInfo.FormatMissingModelHint(
            modelDir: "C:\\VoxPen\\models\\paraformer",
            reason: "缺少 tokens.txt");

        Assert.Contains("Paraformer", hint);
        Assert.Contains("缺少 tokens.txt", hint);
        Assert.Contains("C:\\VoxPen\\models\\paraformer", hint);
        Assert.Contains(ModelDownloadInfo.DownloadUrl, hint);
        Assert.Contains(ModelDownloadInfo.PackageName, hint);
    }

    [Fact]
    public void MissingModelHintTolatesEmptyReason()
    {
        var hint = ModelDownloadInfo.FormatMissingModelHint(
            modelDir: "models/paraformer",
            reason: "   ");

        // 空原因不应让首行崩成"未检测到 Paraformer 语音模型："形式；应给一个默认原因。
        Assert.Contains("未检测到", hint);
        Assert.DoesNotContain("语音模型：\n", hint);
        Assert.Contains(ModelDownloadInfo.DownloadUrl, hint);
    }

    [Fact]
    public void ToastIncludesDirectoryAndUrl()
    {
        var (title, body) = ModelDownloadInfo.FormatToast("models/paraformer");

        Assert.Contains("Paraformer", title);
        Assert.Contains("models/paraformer", body);
        Assert.Contains(ModelDownloadInfo.DownloadUrl, body);
    }
}
