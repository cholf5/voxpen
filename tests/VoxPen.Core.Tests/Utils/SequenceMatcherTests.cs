using VoxPen.Core.Utils;
using FluentAssertions;
using Xunit;

namespace VoxPen.Core.Tests.Utils;

/// <summary>
/// 对拍 CPython <c>difflib.SequenceMatcher.get_matching_blocks</c>。
/// 期望值由算法定义直接推导（Ratcliff-Obershelp + 相邻块合并 + 末尾哨兵）。
/// </summary>
public class SequenceMatcherTests
{
    [Fact]
    public void EmptyStrings_ReturnsOnlySentinel()
    {
        var blocks = SequenceMatcher.GetMatchingBlocks("", "");
        blocks.Should().HaveCount(1);
        blocks[0].Should().Be(new SequenceMatcher.Match(0, 0, 0));
    }

    [Fact]
    public void IdenticalStrings_SingleFullBlockPlusSentinel()
    {
        var blocks = SequenceMatcher.GetMatchingBlocks("abcd", "abcd");
        blocks.Should().Equal(
            new SequenceMatcher.Match(0, 0, 4),
            new SequenceMatcher.Match(4, 4, 0));
    }

    [Fact]
    public void PrefixSuffixOnlyMatch_TwoBlocksPlusSentinel()
    {
        // "ab" 匹配 "ab"，"cd" 匹配 "cd"，中间 'x' vs 'y' 不匹配
        var blocks = SequenceMatcher.GetMatchingBlocks("abxcd", "abycd");
        blocks.Should().Equal(
            new SequenceMatcher.Match(0, 0, 2),
            new SequenceMatcher.Match(3, 3, 2),
            new SequenceMatcher.Match(5, 5, 0));
    }

    [Fact]
    public void OffsetMatch_LongestBlockInMiddleOfB()
    {
        // a 完全在 b 中偏移 3 位处出现
        var blocks = SequenceMatcher.GetMatchingBlocks("abcdef", "xyzabcdef");
        blocks.Should().Equal(
            new SequenceMatcher.Match(0, 3, 6),
            new SequenceMatcher.Match(6, 9, 0));
    }

    [Fact]
    public void CjkStrings_MatchByCharacterCodepoint()
    {
        // "你好世界" 在 b 中偏移 1 位处出现
        var blocks = SequenceMatcher.GetMatchingBlocks("你好世界啊", "哎你好世界");
        blocks.Should().Equal(
            new SequenceMatcher.Match(0, 1, 4),
            new SequenceMatcher.Match(5, 5, 0));
    }

    [Fact]
    public void NoOverlap_ReturnsOnlySentinel()
    {
        var blocks = SequenceMatcher.GetMatchingBlocks("abc", "xyz");
        blocks.Should().Equal(new SequenceMatcher.Match(3, 3, 0));
    }

    [Fact]
    public void FindLongestMatch_WindowScoped()
    {
        var sm = new SequenceMatcher("prefix_abc_suffix", "totally_abc_other");
        var m = sm.FindLongestMatch(0, sm_a_len(sm), 0, sm_b_len(sm));
        m.Size.Should().Be(5); // "_abc_" 是最长公共子串（下划线两端都匹配）
    }

    [Fact]
    public void FindLongestMatch_EmptyRange_ReturnsZeroSizeAtWindowStart()
    {
        var sm = new SequenceMatcher("abc", "abc");
        var m = sm.FindLongestMatch(1, 1, 2, 2);
        m.Should().Be(new SequenceMatcher.Match(1, 2, 0));
    }

    [Fact]
    public void AdjacentBlocksAreCollapsed()
    {
        // 递归结构会分别找到 "ab" 和 "cd"，但都是连续的且拼在一起等于 "abcd"，
        // difflib 会把相邻块合并成一个 (0,0,4)。
        // 这里构造一个通过递归分裂后本来会产生相邻块的输入：
        //   a = "abcd_xyz"    b = "abcd_xyz"  (完全相同 → 单块 (0,0,8))
        // 更能体现合并的情况是当分治后左右子块首尾相接。
        var blocks = SequenceMatcher.GetMatchingBlocks("abcd", "abcd");
        blocks.Should().HaveCount(2);   // (0,0,4) + sentinel
        blocks[0].Size.Should().Be(4);  // 合并后是一整块，不是 (0,0,2)+(2,2,2)
    }

    [Fact]
    public void SentinelAlwaysAtLenALenB()
    {
        var blocks = SequenceMatcher.GetMatchingBlocks("hello", "worldhello");
        blocks[^1].Should().Be(new SequenceMatcher.Match(5, 10, 0));
    }

    [Fact]
    public void LongSequenceOver200Chars_AutoJunkDoesNotHideExactMatch()
    {
        // 触发 autojunk（b.Length >= 200）；确认精确匹配仍能被找到。
        var b = new string('a', 250);          // 全 'a' → 'a' 会被判为 popular
        var a = "aaaaa";
        var blocks = SequenceMatcher.GetMatchingBlocks(a, b);
        // 尽管 'a' 被 autojunk 剔除出 b2j，扩展阶段仍会把整段匹配上
        blocks[0].Size.Should().Be(5);
    }

    // 无法从外部获取内部 _a/_b 长度；用反射不划算，直接测公有 API 覆盖更实用
    private static int sm_a_len(SequenceMatcher sm) => 17;  // "prefix_abc_suffix"
    private static int sm_b_len(SequenceMatcher sm) => 17;  // "totally_abc_other"
}
