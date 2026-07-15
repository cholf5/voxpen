using CapsWriterSharp.Core.Transcribe;
using FluentAssertions;
using Xunit;

namespace CapsWriterSharp.Core.Tests.Transcribe;

public class SegmentMergerTests
{
    // ----------------- MergeText -----------------

    [Fact]
    public void MergeText_EmptyPrev_ReturnsNext()
    {
        SegmentMerger.MergeText("", "abc").Should().Be("abc");
        SegmentMerger.MergeText(null, "abc").Should().Be("abc");
    }

    [Fact]
    public void MergeText_EmptyNext_ReturnsPrev()
    {
        SegmentMerger.MergeText("abc", "").Should().Be("abc");
        SegmentMerger.MergeText("abc", null).Should().Be("abc");
    }

    [Fact]
    public void MergeText_NoOverlap_ConcatenatesDirectly()
    {
        SegmentMerger.MergeText("hello", "world").Should().Be("helloworld");
    }

    [Fact]
    public void MergeText_ChineseOverlap_JoinsAtBestBlock()
    {
        // prev 尾部"真好"与 new 头部"真好"应对齐
        var merged = SegmentMerger.MergeText("今天天气真好", "真好我很开心");
        merged.Should().Be("今天天气真好我很开心");
    }

    [Fact]
    public void MergeText_TrailingPunctuationOnPrev_Stripped()
    {
        // 逗号/句号不影响对齐；结果保留 next 尾部标点
        var merged = SegmentMerger.MergeText("今天天气真好，", "真好我很开心。");
        merged.Should().EndWith("。");
        merged.Should().Contain("今天天气真好我很开心");
    }

    [Fact]
    public void MergeText_LongOverlap_PrefersLongestBlock()
    {
        // "ABCDEF" 6 字重叠 → 全部拼上
        SegmentMerger.MergeText("XYZABCDEF", "ABCDEFPQR")
                     .Should().Be("XYZABCDEFPQR");
    }

    // ----------------- MergeTokens -----------------

    [Fact]
    public void MergeTokens_FirstSegment_ReturnsNewAsGlobal()
    {
        var (tok, ts) = SegmentMerger.MergeTokens(
            prevTokens: Array.Empty<string>(),
            prevTimestamps: Array.Empty<float>(),
            newTokens: new[] { "你", "好" },
            newTimestamps: new[] { 0.0f, 0.1f },
            offsetSeconds: 2.0,
            overlapSeconds: 0.5,
            isFirstSegment: true);
        tok.Should().Equal("你", "好");
        ts.Should().HaveCount(2);
        ts[0].Should().BeApproximately(2.0f, 1e-4f);
        ts[1].Should().BeApproximately(2.1f, 1e-4f);
    }

    [Fact]
    public void MergeTokens_EmptyNew_ReturnsPrevAsIs()
    {
        var (tok, ts) = SegmentMerger.MergeTokens(
            prevTokens: new[] { "a" },
            prevTimestamps: new[] { 0.5f },
            newTokens: Array.Empty<string>(),
            newTimestamps: Array.Empty<float>(),
            offsetSeconds: 1.0,
            overlapSeconds: 0.5,
            isFirstSegment: false);
        tok.Should().Equal("a");
        ts.Should().Equal(0.5f);
    }

    [Fact]
    public void MergeTokens_Overlap_JoinsAtMatch()
    {
        // prev 尾部 "真好" 与 new 头部 "真好" 对齐；new 从索引 2 起接上
        var prev = new[] { "今", "天", "天", "气", "真", "好" };
        var prevTs = new[] { 0.0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
        var next = new[] { "真", "好", "我", "很", "开", "心" };
        var nextTs = new[] { 0.0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f }; // 片段相对

        var (tok, ts) = SegmentMerger.MergeTokens(prev, prevTs, next, nextTs,
            offsetSeconds: 0.4, overlapSeconds: 0.2, isFirstSegment: false);

        tok.Should().Equal("今", "天", "天", "气", "真", "好", "我", "很", "开", "心");
        // 全局时间戳：prev 保持不变，new 从 idx=2 起加 offset=0.4（浮点近似）
        ts.Should().HaveCount(10);
        var expected = new[] { 0.0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f };
        for (int i = 0; i < expected.Length; i++)
            ts[i].Should().BeApproximately(expected[i], 1e-4f);
    }

    [Fact]
    public void MergeTokens_NoOverlap_FallsBackByTimestamp()
    {
        // 完全不同的文本 + new 时间戳偏移超过 prev 末尾 0.1s → 全保留
        var (tok, ts) = SegmentMerger.MergeTokens(
            prevTokens: new[] { "a", "b" },
            prevTimestamps: new[] { 0.0f, 0.1f },
            newTokens: new[] { "x", "y" },
            newTimestamps: new[] { 0.0f, 0.1f },
            offsetSeconds: 0.2,        // 使得 new_global = 0.2, 0.3
            overlapSeconds: 0.05,      // 让 tail/head 都足够小
            isFirstSegment: false);
        // last_time+0.1 = 0.2, new_global[0]=0.2 不严格 >，new_global[1]=0.3 > → 从 idx=1 起接
        tok.Should().Equal("a", "b", "y");
        ts.Should().HaveCount(3);
        ts[0].Should().BeApproximately(0.0f, 1e-4f);
        ts[1].Should().BeApproximately(0.1f, 1e-4f);
        ts[2].Should().BeApproximately(0.3f, 1e-4f);
    }

    [Fact]
    public void MergeTokens_CleansConsecutiveRepeatedPunct()
    {
        // prev 尾 "，"，new 头也是 "，"；对齐后拼接不应保留连续标点
        var prev = new[] { "你", "好", "啊", "，" };
        var prevTs = new[] { 0.0f, 0.1f, 0.2f, 0.3f };
        var next = new[] { "，", "世", "界" };
        var nextTs = new[] { 0.0f, 0.1f, 0.2f };

        var (tok, _) = SegmentMerger.MergeTokens(prev, prevTs, next, nextTs,
            offsetSeconds: 0.3, overlapSeconds: 0.1, isFirstSegment: false);

        // "，" 不应连着出现两次
        int commas = 0;
        for (int i = 1; i < tok.Length; i++)
            if (tok[i] == "，" && tok[i - 1] == "，") commas++;
        commas.Should().Be(0);
    }

    // ----------------- 辅助函数 -----------------

    [Fact]
    public void CharPosToTokenIdx_MultibyteTokensCountedCorrectly()
    {
        // Token 长度不定：["hello", "，", "世界"]
        var tokens = new[] { "hello", "，", "世界" };
        // charPos=0 → 0
        SegmentMerger.CharPosToTokenIdx(tokens, 0, 0).Should().Be(0);
        // charPos=5 → 累计到 5 时 i=1
        SegmentMerger.CharPosToTokenIdx(tokens, 0, 5).Should().Be(1);
        // charPos=6 → 累计到 6 时 i=2（越过 hello 和 "，"）
        SegmentMerger.CharPosToTokenIdx(tokens, 0, 6).Should().Be(2);
        // 超范围 → tokens.Count
        SegmentMerger.CharPosToTokenIdx(tokens, 0, 100).Should().Be(3);
    }

    [Fact]
    public void FindBestOverlap_ScoresLongerBlockHigher()
    {
        // "ABCDEF" 6 字比 "XY" 2 字更优，即便位置也满足
        var best = SegmentMerger.FindBestOverlap("XYZABCDEF", "ABCDEFPQR");
        best.Should().NotBeNull();
        best!.Value.Size.Should().Be(6);
    }
}
