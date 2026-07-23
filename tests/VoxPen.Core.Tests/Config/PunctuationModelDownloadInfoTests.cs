using VoxPen.Core.Config;
using Xunit;

namespace VoxPen.Core.Tests.Config;

public sealed class PunctuationModelDownloadInfoTests
{
    [Fact]
    public void Definition_describes_the_installable_punctuation_package()
    {
        var definition = PunctuationModelDefinition.Default;

        Assert.Equal("models/Punct-CT-Transformer/sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12",
            definition.DefaultModelDir);
        Assert.Contains("model.onnx", definition.RequiredFiles);
        Assert.Equal(PunctuationModelDownloadInfo.PackageName, definition.PackageName);
        Assert.Equal(PunctuationModelDownloadInfo.DownloadUrl, definition.DownloadUrl.ToString());
    }

    [Fact]
    public void DownloadUrlIsSherpaOnnxPunctuationAsset()
    {
        // 下载源必须指向上游 sherpa-onnx 的标点模型 tag；换 URL 得改 README，别偷偷改。
        Assert.StartsWith("https://github.com/k2-fsa/sherpa-onnx", PunctuationModelDownloadInfo.DownloadUrl);
        Assert.Contains("punctuation-models", PunctuationModelDownloadInfo.DownloadUrl);
        Assert.Contains("/releases/download/", PunctuationModelDownloadInfo.DownloadUrl);
    }

    [Fact]
    public void PackageNameMatchesUpstreamAsset()
    {
        Assert.Contains("sherpa-onnx-punct-ct-transformer", PunctuationModelDownloadInfo.PackageName);
        Assert.EndsWith(".tar.bz2", PunctuationModelDownloadInfo.PackageName);
    }

    [Fact]
    public void MissingModelHintIncludesUrlDirectoryAndReason()
    {
        var hint = PunctuationModelDownloadInfo.FormatMissingModelHint(
            modelDir: "C:\\VoxPen\\models\\Punct-CT-Transformer",
            reason: "缺少 model.onnx");

        Assert.Contains("CT-Transformer", hint);
        Assert.Contains("缺少 model.onnx", hint);
        Assert.Contains("C:\\VoxPen\\models\\Punct-CT-Transformer", hint);
        Assert.Contains(PunctuationModelDownloadInfo.DownloadUrl, hint);
        Assert.Contains(PunctuationModelDownloadInfo.PackageName, hint);
    }

    [Fact]
    public void MissingModelHintToleratesEmptyReason()
    {
        var hint = PunctuationModelDownloadInfo.FormatMissingModelHint(
            modelDir: "models/Punct-CT-Transformer",
            reason: "   ");

        // 空原因走默认，不会出现"标点模型：\n"这种空冒号形态。
        Assert.Contains("未检测到", hint);
        Assert.DoesNotContain("标点模型：\n", hint);
        Assert.Contains(PunctuationModelDownloadInfo.DownloadUrl, hint);
    }

    [Fact]
    public void HintExplainsDegradationSemantics()
    {
        // 强合同：README / ResolvePunctuator 都把"缺模型 = 无标点，但主功能不受影响"当官方口径，
        // Hint 里必须点出来，避免用户误以为 App 起不来。
        var hint = PunctuationModelDownloadInfo.FormatMissingModelHint("dir", "缺少 model.onnx");
        Assert.Contains("不影响主功能", hint);
    }
}
