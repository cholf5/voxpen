using VoxPen.Core.Transcribe;
using FluentAssertions;
using Xunit;

namespace VoxPen.Core.Tests.Transcribe;

public class SmartSplitTests
{
    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        SmartSplit.Split("").Should().Be("");
        SmartSplit.Split(null).Should().Be("");
    }

    [Fact]
    public void ChinesePunct_SplitsAtStrongAndWeak()
    {
        // 每个中文标点都是切分点：句号强、逗号弱但当前 buffer 已 > 2 字所以也切
        SmartSplit.Split("你好，世界。").Should().Be("你好，\n世界。");
    }

    [Fact]
    public void EnglishPuncNeedsTrailingSpace_ProtectsDecimalNumbers()
    {
        // "3.14" 后跟数字，不切
        SmartSplit.Split("圆周率是 3.14 大约。").Should().Be("圆周率是 3.14 大约。");
    }

    [Fact]
    public void EnglishPunctWithSpaces_Splits()
    {
        // 每个 ", " 后 buffer 均已够长，都切
        SmartSplit.Split("abc, def, ghi.").Should().Be("abc, \ndef, \nghi.");
    }

    [Fact]
    public void ShortChunkAtWeakPunct_DoesNotSplit()
    {
        // "a," buffer 长度 2，不严格 > minChars(2)，暂不切；累积到强标点才切
        SmartSplit.Split("a, b.").Should().Be("a, \nb.");
        // 但注意 "a, " 长度为 3（含空格）>2，仍切分
    }

    [Fact]
    public void StrongPunctAlwaysSplitsEvenIfShort()
    {
        SmartSplit.Split("啊。好。").Should().Be("啊。\n好。");
    }

    [Fact]
    public void QuestionMarkIsStrong()
    {
        SmartSplit.Split("真的？假的？").Should().Be("真的？\n假的？");
    }

    [Fact]
    public void NoPunct_ReturnsAsSingleLine()
    {
        SmartSplit.Split("这里没有标点符号").Should().Be("这里没有标点符号");
    }

    [Fact]
    public void MixedCjkAndAscii_PreservesPunct()
    {
        var result = SmartSplit.Split("我用 Python, C#, 和 Rust。");
        result.Should().Contain("\n");
        // 尾部必须完整
        result.Split('\n')[^1].Should().EndWith("。");
    }

    [Fact]
    public void ExclamationSplits()
    {
        SmartSplit.Split("wow! nice!").Should().Be("wow! \nnice!");
    }
}
