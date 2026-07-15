using VoxPen.Core.Transcribe;
using FluentAssertions;
using Xunit;

namespace VoxPen.Core.Tests.Transcribe;

public class SrtWriterTests
{
    [Fact]
    public void FormatTime_ZeroAndBasic()
    {
        SrtWriter.FormatTime(TimeSpan.Zero).Should().Be("00:00:00,000");
        SrtWriter.FormatTime(TimeSpan.FromSeconds(1.234)).Should().Be("00:00:01,234");
    }

    [Fact]
    public void FormatTime_HoursMinutesSeconds()
    {
        var t = new TimeSpan(0, 1, 23, 45, 678);
        SrtWriter.FormatTime(t).Should().Be("01:23:45,678");
    }

    [Fact]
    public void FormatTime_NegativeClampsToZero()
    {
        SrtWriter.FormatTime(TimeSpan.FromSeconds(-1)).Should().Be("00:00:00,000");
    }

    [Fact]
    public void Compose_Empty_ReturnsEmptyString()
    {
        SrtWriter.Compose(Array.Empty<SubtitleEntry>()).Should().BeEmpty();
    }

    [Fact]
    public void Compose_SingleSubtitle_HasExpectedShape()
    {
        var s = new SubtitleEntry(1, "你好", TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.5));
        var srt = SrtWriter.Compose(new[] { s });
        srt.Should().Be("1\n00:00:00,500 --> 00:00:01,500\n你好\n\n");
    }

    [Fact]
    public void Compose_MultipleSubtitles_JoinedWithBlankLine()
    {
        var subs = new[]
        {
            new SubtitleEntry(1, "line A", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1)),
            new SubtitleEntry(2, "line B", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
        };
        var srt = SrtWriter.Compose(subs);
        srt.Should().Contain("1\n00:00:00,000 --> 00:00:01,000\nline A\n\n");
        srt.Should().Contain("2\n00:00:01,000 --> 00:00:02,000\nline B\n\n");
    }

    [Fact]
    public async Task WriteAsync_WritesUtf8NoBom()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".srt");
        try
        {
            var subs = new[]
            {
                new SubtitleEntry(1, "你好", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            };
            await SrtWriter.WriteAsync(tmp, subs);
            var bytes = await File.ReadAllBytesAsync(tmp);
            // 无 BOM
            (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).Should().BeFalse();
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            text.Should().Contain("你好");
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
