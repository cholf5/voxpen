using System.Text.Json;
using VoxPen.Core.Transcribe;
using FluentAssertions;
using Xunit;

namespace VoxPen.Core.Tests.Transcribe;

public class TranscriptJsonWriterTests
{
    [Fact]
    public void Compose_EmptyInputs_ValidEmptyJson()
    {
        var json = TranscriptJsonWriter.Compose(Array.Empty<double>(), Array.Empty<string>());
        json.Should().Contain("\"timestamps\":[]");
        json.Should().Contain("\"tokens\":[]");
    }

    [Fact]
    public void Compose_Roundtrip_KeepsValuesAndOrder()
    {
        var ts = new[] { 0.1, 0.25, 1.5 };
        var tokens = new[] { "你", "好", "世" };
        var json = TranscriptJsonWriter.Compose(ts, tokens);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("timestamps").EnumerateArray()
            .Select(e => e.GetDouble()).ToArray().Should().Equal(0.1, 0.25, 1.5);
        root.GetProperty("tokens").EnumerateArray()
            .Select(e => e.GetString()).ToArray().Should().Equal("你", "好", "世");
    }

    [Fact]
    public void Compose_CjkNotEscaped()
    {
        // UnsafeRelaxedJsonEscaping 下 CJK 应原样输出
        var json = TranscriptJsonWriter.Compose(new[] { 0.0 }, new[] { "你好" });
        json.Should().Contain("你好");
        json.Should().NotContain("\\u");
    }

    [Fact]
    public async Task WriteAsync_ProducesReadableFile()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await TranscriptJsonWriter.WriteAsync(tmp, new[] { 0.5, 1.0 }, new[] { "a", "b" });
            var text = await File.ReadAllTextAsync(tmp);
            using var doc = JsonDocument.Parse(text);
            doc.RootElement.GetProperty("tokens").GetArrayLength().Should().Be(2);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Compose_NullInputs_TreatedAsEmpty()
    {
        var json = TranscriptJsonWriter.Compose(null!, null!);
        json.Should().Contain("[]");
    }
}
