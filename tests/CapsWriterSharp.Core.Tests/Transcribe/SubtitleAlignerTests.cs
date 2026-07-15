using CapsWriterSharp.Core.Transcribe;
using FluentAssertions;
using Xunit;

namespace CapsWriterSharp.Core.Tests.Transcribe;

public class SubtitleAlignerTests
{
    private static IReadOnlyList<TranscriptWord> BuildWords(params (string word, double start, double end)[] items)
        => items.Select(i => new TranscriptWord(i.word, i.start, i.end)).ToArray();

    [Fact]
    public void EmptyInputs_ReturnsEmpty()
    {
        SubtitleAligner.Align(Array.Empty<string>(), BuildWords()).Should().BeEmpty();
        SubtitleAligner.Align(new[] { "hi" }, Array.Empty<TranscriptWord>()).Should().BeEmpty();
    }

    [Fact]
    public void SingleLine_UsesFullSpan()
    {
        var words = BuildWords(
            ("你", 0.0, 0.2),
            ("好", 0.2, 0.4),
            ("世", 0.4, 0.6),
            ("界", 0.6, 0.8));

        var subs = SubtitleAligner.Align(new[] { "你好世界" }, words);
        subs.Should().HaveCount(1);
        subs[0].Index.Should().Be(1);
        subs[0].Content.Should().Be("你好世界");
        subs[0].Start.TotalSeconds.Should().BeApproximately(0.0, 1e-6);
        subs[0].End.TotalSeconds.Should().BeApproximately(0.8, 1e-6);
    }

    [Fact]
    public void MultipleLines_SplitTimestamps()
    {
        var words = BuildWords(
            ("你", 0.0, 0.2),
            ("好", 0.2, 0.4),
            ("世", 1.0, 1.2),
            ("界", 1.2, 1.4));

        var subs = SubtitleAligner.Align(new[] { "你好", "世界" }, words);
        subs.Should().HaveCount(2);

        subs[0].Content.Should().Be("你好");
        subs[0].Start.TotalSeconds.Should().BeApproximately(0.0, 1e-6);
        subs[0].End.TotalSeconds.Should().BeApproximately(0.4, 1e-6);

        subs[1].Content.Should().Be("世界");
        subs[1].Start.TotalSeconds.Should().BeApproximately(1.0, 1e-6);
        subs[1].End.TotalSeconds.Should().BeApproximately(1.4, 1e-6);
    }

    [Fact]
    public void PunctuationInLines_IgnoredForAlignment_ButKeptInContent()
    {
        var words = BuildWords(
            ("你", 0.0, 0.2),
            ("好", 0.2, 0.4),
            ("世", 1.0, 1.2),
            ("界", 1.2, 1.4));

        // 用户手动加了标点分行
        var subs = SubtitleAligner.Align(new[] { "你好，", "世界。" }, words);
        subs.Should().HaveCount(2);
        subs[0].Content.Should().Be("你好，");
        subs[1].Content.Should().Be("世界。");
        subs[0].End.TotalSeconds.Should().BeApproximately(0.4, 1e-6);
        subs[1].Start.TotalSeconds.Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public void EmptyLines_Skipped()
    {
        var words = BuildWords(
            ("a", 0.0, 0.1),
            ("b", 0.1, 0.2));

        var subs = SubtitleAligner.Align(new[] { "a", "", "  ", "b" }, words);
        subs.Should().HaveCount(2);
        subs[0].Content.Should().Be("a");
        subs[1].Content.Should().Be("b");
    }

    [Fact]
    public void BuildWordsFromTokens_ClampsOverlap()
    {
        var tokens = new[] { "你", "好", "世", "界" };
        var timestamps = new[] { 0.0, 0.1, 0.15, 0.5 };
        var words = SubtitleAligner.BuildWordsFromTokens(tokens, timestamps, defaultDuration: 0.2);
        words.Should().HaveCount(4);

        // 前三个应该被下一个的 start 截断
        words[0].EndSec.Should().BeApproximately(0.1, 1e-6);
        words[1].EndSec.Should().BeApproximately(0.15, 1e-6);
        words[2].EndSec.Should().BeApproximately(0.35, 1e-6); // 0.15+0.2 < 0.5
        words[3].EndSec.Should().BeApproximately(0.7, 1e-6);
    }

    [Fact]
    public void BuildWordsFromTokens_StripsAtSign()
    {
        var tokens = new[] { "@你", "好@" };
        var timestamps = new[] { 0.0, 0.5 };
        var words = SubtitleAligner.BuildWordsFromTokens(tokens, timestamps);
        words[0].Word.Should().Be("你");
        words[1].Word.Should().Be("好");
    }

    [Fact]
    public void MismatchedLines_FallbackDoesntThrow()
    {
        // 文本行完全对不上 tokens，走 fallback 分支
        var words = BuildWords(
            ("你", 0.0, 0.2),
            ("好", 0.2, 0.4));
        var subs = SubtitleAligner.Align(new[] { "xyz" }, words);
        subs.Should().HaveCount(1);
        subs[0].Content.Should().Be("xyz");
        // fallback: start = words[last+1 or last].Start, end = start + 0.5
        subs[0].End.Should().BeGreaterThan(subs[0].Start);
    }
}
