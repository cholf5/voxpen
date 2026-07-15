using CapsWriterSharp.Core.Hotword.Phoneme;
using FluentAssertions;
using Xunit;

namespace CapsWriterSharp.Core.Tests.Hotword;

public class PhonemeSimilarityTests
{
    [Fact]
    public void IdenticalStrings_NotConsideredSimilar()
    {
        // 相同不算 similar（similar 是"不同但相近"）
        PhonemeSimilarity.IsSimilar("an", "an").Should().BeFalse();
    }

    [Theory]
    [InlineData("an", "ang")]
    [InlineData("en", "eng")]
    [InlineData("z", "zh")]
    [InlineData("l", "n")]
    [InlineData("f", "h")]
    [InlineData("ai", "ei")]
    public void KnownSimilarPairs_AreSimilar(string a, string b)
    {
        PhonemeSimilarity.IsSimilar(a, b).Should().BeTrue();
        PhonemeSimilarity.IsSimilar(b, a).Should().BeTrue();
    }

    [Fact]
    public void UnknownPair_NotSimilar()
    {
        PhonemeSimilarity.IsSimilar("a", "e").Should().BeFalse();
        PhonemeSimilarity.IsSimilar("hello", "world").Should().BeFalse();
    }

    [Fact]
    public void LcsLength_Basic()
    {
        PhonemeSimilarity.LcsLength("abc", "abc").Should().Be(3);
        PhonemeSimilarity.LcsLength("abc", "xyz").Should().Be(0);
        PhonemeSimilarity.LcsLength("abcde", "ace").Should().Be(3);
        PhonemeSimilarity.LcsLength("", "abc").Should().Be(0);
    }

    [Fact]
    public void PhonemeCost_ExactMatch_Zero()
    {
        var p = new Phoneme("an", PhonemeLang.Zh, true, false, 0, 1);
        var q = new Phoneme("an", PhonemeLang.Zh, true, false, 5, 6);
        PhonemeSimilarity.PhonemeCost(p, q).Should().Be(0f);
    }

    [Fact]
    public void PhonemeCost_ChineseSimilar_Half()
    {
        var p = new Phoneme("an", PhonemeLang.Zh, true, false, 0, 1);
        var q = new Phoneme("ang", PhonemeLang.Zh, true, false, 5, 6);
        PhonemeSimilarity.PhonemeCost(p, q).Should().Be(0.5f);
    }

    [Fact]
    public void PhonemeCost_DifferentLang_One()
    {
        var p = new Phoneme("hello", PhonemeLang.En, true, true, 0, 5);
        var q = new Phoneme("你好", PhonemeLang.Zh, true, true, 0, 2);
        PhonemeSimilarity.PhonemeCost(p, q).Should().Be(1f);
    }

    [Fact]
    public void PhonemeCost_EnglishByLcs()
    {
        var p = new Phoneme("cat", PhonemeLang.En, true, true, 0, 3);
        var q = new Phoneme("bat", PhonemeLang.En, true, true, 0, 3);
        // LCS = 2 ("at"), max_len = 3 → cost = 1 - 2/3 ≈ 0.333
        PhonemeSimilarity.PhonemeCost(p, q).Should().BeApproximately(1f - 2f / 3f, 1e-4f);
    }
}
