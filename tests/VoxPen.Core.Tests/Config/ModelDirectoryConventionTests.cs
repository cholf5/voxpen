using FluentAssertions;
using VoxPen.Core.Config;
using Xunit;

namespace VoxPen.Core.Tests.Config;

public sealed class ModelDirectoryConventionTests
{
    [Fact]
    public void Apply_ignores_legacy_custom_model_directories()
    {
        var config = new AppConfig
        {
            Asr = { Engine = AsrEngineKind.SenseVoice, ModelDir = "D:/legacy/asr" },
            Punctuation = { ModelDir = "D:/legacy/punctuation" },
        };

        ModelDirectoryConvention.Apply(config);

        config.Asr.ModelDir.Should().Be(AsrModelCatalog.Get(AsrEngineKind.SenseVoice).DefaultModelDir);
        config.Punctuation.ModelDir.Should().Be(ModelDirectoryConvention.PunctuationModelDirectory);
    }
}
