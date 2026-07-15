using CapsWriterSharp.Core.Storage;
using FluentAssertions;
using Xunit;

namespace CapsWriterSharp.Core.Tests.Storage;

public class DiaryWriterTests : IDisposable
{
    private readonly string _tempRoot;

    public DiaryWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(),
            "capswriter-diary-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void FirstWrite_CreatesFileWithHeader()
    {
        var writer = new DiaryWriter(_tempRoot);
        var ts = new DateTime(2026, 7, 9, 15, 30, 45, DateTimeKind.Local);

        var path = writer.Write("你好世界", ts);

        File.Exists(path).Should().BeTrue();
        path.Should().EndWith(Path.Combine("2026", "07", "09.md"));

        var content = File.ReadAllText(path);
        content.Should().StartWith("```txt");
        content.Should().Contain(DiaryWriter.HeaderMd);
        content.Should().EndWith("15:30:45 你好世界\n\n");
    }

    [Fact]
    public void SecondWrite_AppendsWithoutDuplicatingHeader()
    {
        var writer = new DiaryWriter(_tempRoot);
        var ts1 = new DateTime(2026, 7, 9, 10, 0, 0, DateTimeKind.Local);
        var ts2 = new DateTime(2026, 7, 9, 10, 5, 0, DateTimeKind.Local);

        writer.Write("first line", ts1);
        writer.Write("second line", ts2);

        var content = File.ReadAllText(Path.Combine(_tempRoot, "2026", "07", "09.md"));

        // header 只出现一次
        var headerCount = CountOccurrences(content, "正则表达式 Tip");
        headerCount.Should().Be(1);
        content.Should().Contain("10:00:00 first line\n\n");
        content.Should().Contain("10:05:00 second line\n\n");
    }

    [Fact]
    public void WithAudio_UsesRelativePosixLink()
    {
        var writer = new DiaryWriter(_tempRoot);
        var ts = new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Local);

        // 与原项目一致：audio 落在 YYYY/MM/assets/xxx.wav 里
        var assetsDir = Path.Combine(_tempRoot, "2026", "07", "assets");
        Directory.CreateDirectory(assetsDir);
        var audioPath = Path.Combine(assetsDir, "clip.wav");
        File.WriteAllBytes(audioPath, new byte[] { 0 });

        writer.Write("with audio", ts, audioPath);

        var md = File.ReadAllText(Path.Combine(_tempRoot, "2026", "07", "09.md"));
        md.Should().Contain("[12:00:00](assets/clip.wav) with audio");
        // 追加的链接段本身必须是 POSIX 分隔符（不检查 header，因为 header 含正则转义 \）
        md.Should().NotContain("assets\\clip.wav");
    }

    [Fact]
    public void AudioPathWithSpaces_UrlEncoded()
    {
        var writer = new DiaryWriter(_tempRoot);
        var ts = new DateTime(2026, 7, 9, 8, 0, 0, DateTimeKind.Local);

        var assetsDir = Path.Combine(_tempRoot, "2026", "07", "assets");
        Directory.CreateDirectory(assetsDir);
        var audioPath = Path.Combine(assetsDir, "hello world.wav");
        File.WriteAllBytes(audioPath, new byte[] { 0 });

        writer.Write("space test", ts, audioPath);

        var md = File.ReadAllText(Path.Combine(_tempRoot, "2026", "07", "09.md"));
        md.Should().Contain("(assets/hello%20world.wav)");
        md.Should().NotContain("hello world.wav");
    }

    [Fact]
    public void NoAudio_OmitsBracketLink()
    {
        var writer = new DiaryWriter(_tempRoot);
        var ts = new DateTime(2026, 7, 9, 20, 30, 0, DateTimeKind.Local);

        writer.Write("plain text", ts, audioPath: null);
        writer.Write("empty audio", ts, audioPath: "  ");

        var md = File.ReadAllText(Path.Combine(_tempRoot, "2026", "07", "09.md"));
        md.Should().Contain("20:30:00 plain text\n\n");
        md.Should().Contain("20:30:00 empty audio\n\n");
        md.Should().NotContain("[20:30:00]");
    }

    [Fact]
    public void MultipleDays_UseSeparateFiles()
    {
        var writer = new DiaryWriter(_tempRoot);
        writer.Write("day1", new DateTime(2026, 7, 8, 9, 0, 0));
        writer.Write("day2", new DateTime(2026, 7, 9, 9, 0, 0));

        File.Exists(Path.Combine(_tempRoot, "2026", "07", "08.md")).Should().BeTrue();
        File.Exists(Path.Combine(_tempRoot, "2026", "07", "09.md")).Should().BeTrue();
    }

    [Fact]
    public void BuildAudioLink_NullOrWhitespace_ReturnsEmpty()
    {
        DiaryWriter.BuildAudioLink("C:/a/b.md", null).Should().BeEmpty();
        DiaryWriter.BuildAudioLink("C:/a/b.md", "").Should().BeEmpty();
        DiaryWriter.BuildAudioLink("C:/a/b.md", "   ").Should().BeEmpty();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
