using FluentAssertions;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;
using VoxPen.Platform.Windows.Recognition;
using Xunit;

namespace VoxPen.Core.Tests.Recognition;

public sealed class AdditionalAsrEngineCapabilitiesTests
{
    [Fact]
    public void SenseVoice_has_native_punctuation_timestamps_and_hotwords()
    {
        using var engine = new SenseVoiceEngine(new AsrConfig { Engine = AsrEngineKind.SenseVoice });

        engine.Capabilities.Should().Be(
            EngineCapabilities.Punctuation | EngineCapabilities.Timestamps | EngineCapabilities.Hotwords);
    }

    [Fact]
    public void FunAsrNano_has_native_punctuation_timestamps_and_hotwords()
    {
        using var engine = new FunAsrNanoEngine(new AsrConfig { Engine = AsrEngineKind.FunAsrNano });

        engine.Capabilities.Should().Be(
            EngineCapabilities.Punctuation | EngineCapabilities.Timestamps | EngineCapabilities.Hotwords);
    }

    [Fact]
    public void Qwen3Asr_has_native_punctuation_without_timestamps()
    {
        using var engine = new Qwen3AsrEngine(new AsrConfig { Engine = AsrEngineKind.QwenAsr });

        engine.Capabilities.Should().Be(EngineCapabilities.Punctuation);
    }
}
