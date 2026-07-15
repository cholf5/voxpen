using VoxPen.Core.Hotword.Phoneme;
using FluentAssertions;
using Xunit;

namespace VoxPen.Core.Tests.Hotword;

public class FastRagTests
{
    private static FastRag BuildRag(string hotContent, double threshold = 0.6)
    {
        var entries = HotwordFile.Parse(hotContent);
        var rag = new FastRag(threshold);
        var pairs = new List<KeyValuePair<string, IReadOnlyList<IReadOnlyList<Phoneme>>>>();
        foreach (var e in entries) pairs.Add(new(e.Target, e.PhonemeLists));
        rag.AddHotwords(pairs);
        return rag;
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var rag = BuildRag("撒贝宁");
        rag.Search(Array.Empty<Phoneme>()).Should().BeEmpty();
    }

    [Fact]
    public void PerfectMatch_Recovered()
    {
        var rag = BuildRag("撒贝宁");
        var input = PhonemeExtractor.Default.Extract("我喜欢撒贝宁说话");
        var res = rag.Search(input);
        res.Should().NotBeEmpty();
        res[0].Hotword.Should().Be("撒贝宁");
        res[0].Score.Should().BeGreaterOrEqualTo(0.85);
    }

    [Fact]
    public void PhonemeTypo_Recovered_ThroughSimilarPhoneme()
    {
        // "撒贝你" 的音素 ni 与 "撒贝宁" 的 n+ing 相似度足以被 FastRag 命中
        var rag = BuildRag("撒贝宁", threshold: 0.5);
        var input = PhonemeExtractor.Default.Extract("我说撒贝你的段子");
        var res = rag.Search(input);
        res.Any(r => r.Hotword == "撒贝宁").Should().BeTrue();
    }

    [Fact]
    public void HotwordCount_TracksAliases()
    {
        var rag = BuildRag("Claude | Cloud");
        rag.HotwordCount.Should().Be(2); // 1 target + 1 alias
    }

    [Fact]
    public void LocalEditDistance_ExactMatch_Zero()
    {
        var (dist, pos) = FastRag.LocalEditDistance(new[] { 1, 2, 3 }, new[] { 1, 2, 3 });
        dist.Should().Be(0);
        pos.Should().Be(3);
    }

    [Fact]
    public void LocalEditDistance_OneSubstitution_One()
    {
        var (dist, _) = FastRag.LocalEditDistance(new[] { 1, 9, 3 }, new[] { 1, 2, 3 });
        dist.Should().Be(1);
    }

    [Fact]
    public void LocalEditDistance_EmptySub_ReturnsZero()
    {
        var (dist, pos) = FastRag.LocalEditDistance(new[] { 1, 2, 3 }, Array.Empty<int>());
        dist.Should().Be(0);
        pos.Should().Be(0);
    }
}
