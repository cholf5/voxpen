using VoxPen.Core.Hotword.Phoneme;
using FluentAssertions;
using Xunit;

namespace VoxPen.Core.Tests.Hotword;

public class HotwordFileTests
{
    [Fact]
    public void EmptyContent_ReturnsEmpty()
    {
        HotwordFile.Parse("").Should().BeEmpty();
        HotwordFile.Parse(null!).Should().BeEmpty();
    }

    [Fact]
    public void CommentAndBlankLines_Ignored()
    {
        var content = @"# 这是注释
   
撒贝宁
# 又一条注释
康辉
";
        var entries = HotwordFile.Parse(content);
        entries.Should().HaveCount(2);
        entries[0].Target.Should().Be("撒贝宁");
        entries[1].Target.Should().Be("康辉");
    }

    [Fact]
    public void AliasesSeparatedByPipe()
    {
        var entries = HotwordFile.Parse("Claude | Cloud");
        entries.Should().HaveCount(1);
        entries[0].Target.Should().Be("Claude");
        entries[0].PhonemeLists.Should().HaveCount(2); // 目标 + 一个别名
    }

    [Fact]
    public void BlacklistParsed()
    {
        var entries = HotwordFile.Parse("句子 ~~~ 一句话 | 结束");
        entries.Should().HaveCount(1);
        entries[0].Target.Should().Be("句子");
        entries[0].Blacklist.Should().BeEquivalentTo(new[] { "一句话", "结束" });
    }

    [Fact]
    public void NoBlacklist_EmptySet()
    {
        var entries = HotwordFile.Parse("Python");
        entries[0].Blacklist.Should().BeEmpty();
    }

    [Fact]
    public void MalformedLine_Skipped()
    {
        // 单纯 "~~~ 什么" 没有 target，跳过
        var entries = HotwordFile.Parse("~~~ black");
        entries.Should().BeEmpty();
    }

    [Fact]
    public void WhitespaceTrimmedAroundAliases()
    {
        var entries = HotwordFile.Parse("  麦当劳   |  麦大老 ");
        entries[0].Target.Should().Be("麦当劳");
    }
}
