using VoxPen.Core.Hotword.Phoneme;
using FluentAssertions;
using Xunit;

namespace VoxPen.Core.Tests.Hotword;

public class PhonemeExtractorTests
{
    [Fact]
    public void EmptyOrNull_ReturnsEmpty()
    {
        PhonemeExtractor.Default.Extract("").Should().BeEmpty();
        PhonemeExtractor.Default.Extract(null).Should().BeEmpty();
    }

    [Fact]
    public void PureChinese_ExtractsInitialFinalTone()
    {
        // "撒贝宁" (sā bèi níng) — 每个字都是 声母 + 韵母 + 声调 三个音素
        var seq = PhonemeExtractor.Default.Extract("撒贝宁");
        seq.Should().HaveCount(9);   // 3 字 × 3 音素

        // 首字 "撒" (sā, tone=1)：s / a / 1
        seq[0].Value.Should().Be("s");
        seq[0].IsWordStart.Should().BeTrue();
        seq[0].Lang.Should().Be(PhonemeLang.Zh);

        seq[1].Value.Should().Be("a");

        seq[2].Value.Should().Be("1");
        seq[2].IsWordEnd.Should().BeTrue();
        seq[2].IsTone.Should().BeTrue();
    }

    [Fact]
    public void ChineseZeroInitial_MarksFinalAsWordStart()
    {
        // "啊" 是零声母字（韵母 a）
        var seq = PhonemeExtractor.Default.Extract("啊");
        seq.Should().NotBeEmpty();
        seq[0].IsWordStart.Should().BeTrue();
    }

    [Fact]
    public void EnglishWord_SplitByChar_AllWordStartEnd()
    {
        // "hello" → h e l l o
        var seq = PhonemeExtractor.Default.Extract("hello");
        seq.Select(p => p.Value).Should().Equal("h", "e", "l", "l", "o");
        seq.All(p => p.Lang == PhonemeLang.En).Should().BeTrue();
        seq[0].IsWordStart.Should().BeTrue();
        seq[^1].IsWordEnd.Should().BeTrue();
    }

    [Fact]
    public void CamelCase_SplitsAtBoundary()
    {
        // "iPhone" → i / p h o n e 两段，中间无空格但边界拆开
        var seq = PhonemeExtractor.Default.Extract("iPhone");
        seq.Should().HaveCount(6);
        seq[0].Value.Should().Be("i");
        seq[0].IsWordEnd.Should().BeTrue();    // 单字符 token 起讫在同一位
        seq[1].Value.Should().Be("p");
        seq[1].IsWordStart.Should().BeTrue();  // Phone 的开头
    }

    [Fact]
    public void LetterDigitBoundary_SplitsTokens()
    {
        // "iPhone15Pro" → i / phone / 15 / pro
        var seq = PhonemeExtractor.Default.Extract("iPhone15Pro");
        var tokens = string.Concat(seq.Select(p => p.Value));
        tokens.Should().Be("iphone15pro");
        // 每段第一个字符 IsWordStart=true
        seq.Where(p => p.IsWordStart).Should().HaveCountGreaterOrEqualTo(4);
    }

    [Fact]
    public void ChineseMixedWithAscii_ProcessedPerFragment()
    {
        var seq = PhonemeExtractor.Default.Extract("测试123");
        // "测试" 两个 CJK 字（各 3 音素）+ 数字 "123"（3 个 num 音素）
        seq.Should().HaveCount(3 + 3 + 3);
        seq.Skip(6).All(p => p.Lang == PhonemeLang.Num).Should().BeTrue();
    }

    [Fact]
    public void PunctuationSkipped()
    {
        var seq = PhonemeExtractor.Default.Extract("你好，世界！");
        // 4 个汉字 × 3 音素 = 12，标点全部丢弃
        seq.Count.Should().Be(12);
    }

    [Fact]
    public void CharPositionsAreRecorded()
    {
        var seq = PhonemeExtractor.Default.Extract("撒hello");
        seq[0].CharStart.Should().Be(0);
        seq[0].CharEnd.Should().Be(1);
        // "撒" 3 个音素后是 "hello" 的 h（第 1 位）
        var firstEn = seq.First(p => p.Lang == PhonemeLang.En);
        firstEn.CharStart.Should().Be(1);
    }

    [Fact]
    public void XiAnVsXian_Disambiguated()
    {
        // "西安" 两个字：西=x/i/1 (3 音素) + 安=an/1 (零声母 → 2 音素) = 5 音素
        var seqXiAn = PhonemeExtractor.Default.Extract("西安");
        seqXiAn.Should().HaveCount(5);

        // "先" 一个字（xian） → 3 音素
        var seqXian = PhonemeExtractor.Default.Extract("先");
        seqXian.Should().HaveCount(3);

        // 关键：两者音素序列不同（"西安" 有独立的字边界，"先" 是单字）
        seqXiAn.Should().NotBeEquivalentTo(seqXian);
    }
}
