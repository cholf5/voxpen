using CapsWriterSharp.Core.Config;
using Xunit;

namespace CapsWriterSharp.Core.Tests.Config;

public sealed class StartupErrorFormatterTests
{
    [Fact]
    public void FormatsMissingModelErrorWithActionableGuidance()
    {
        var error = new DirectoryNotFoundException(
            "Model directory not found: C:\\CapsWriter\\models\\paraformer.");

        var message = StartupErrorFormatter.Format(error);

        Assert.Contains("模型加载失败", message);
        Assert.Contains("C:\\CapsWriter\\models\\paraformer", message);
        Assert.Contains("请检查模型目录", message);
    }
}
