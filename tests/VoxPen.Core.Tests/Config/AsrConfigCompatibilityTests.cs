using System.Text.Json;
using FluentAssertions;
using VoxPen.Core.Config;
using Xunit;

namespace VoxPen.Core.Tests.Config;

public sealed class AsrConfigCompatibilityTests
{
    [Fact]
    public void Missing_engine_in_a_legacy_config_defaults_to_paraformer()
    {
        const string json = """
        { "asr": { "modelDir": "models/custom" } }
        """;

        var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        config!.Asr.Engine.Should().Be(AsrEngineKind.Paraformer);
        config.Asr.ModelDir.Should().Be("models/custom");
    }
}
