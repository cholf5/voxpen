using FluentAssertions;
using VoxPen.Core.Config;
using Xunit;

namespace VoxPen.Core.Tests.Config;

public sealed class SettingsSelectionTests
{
    [Fact]
    public void Apply_UpdatesShortcutAndSelectedModelTogether()
    {
        var config = new AppConfig();

        SettingsSelection.Apply(config, "x2", AsrEngineKind.SenseVoice);

        config.Shortcut.Key.Should().Be("x2");
        config.Shortcut.Keys.Should().ContainSingle().Which.Should().Be("x2");
        config.Asr.Engine.Should().Be(AsrEngineKind.SenseVoice);
        config.Asr.ModelDir.Should().Be(AsrModelCatalog.Get(AsrEngineKind.SenseVoice).DefaultModelDir);
    }
}
