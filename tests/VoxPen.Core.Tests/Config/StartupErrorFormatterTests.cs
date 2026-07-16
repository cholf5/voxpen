using VoxPen.Core.Config;
using Xunit;

namespace VoxPen.Core.Tests.Config;

public sealed class StartupErrorFormatterTests
{
    [Fact]
    public void FormatsMissingModelErrorWithActionableGuidance()
    {
        var error = new DirectoryNotFoundException(
            "Model directory not found: C:\\VoxPen\\models\\paraformer.");

        var message = StartupErrorFormatter.Format(error);

        Assert.Contains("模型加载失败", message);
        Assert.Contains("C:\\VoxPen\\models\\paraformer", message);
        Assert.Contains("请检查模型目录", message);
        Assert.Contains(ModelDownloadInfo.DownloadUrl, message);
        Assert.Contains(ModelDownloadInfo.PackageName, message);
    }
}
