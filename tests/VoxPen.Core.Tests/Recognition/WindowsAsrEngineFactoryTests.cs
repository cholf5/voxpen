using FluentAssertions;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;
using VoxPen.Platform.Windows.Recognition;
using Xunit;

namespace VoxPen.Core.Tests.Recognition;

public sealed class WindowsAsrEngineFactoryTests
{
    [Theory]
    [InlineData(AsrEngineKind.Paraformer, "paraformer-onnx", EngineCapabilities.Timestamps)]
    [InlineData(AsrEngineKind.SenseVoice, "sensevoice-onnx",
        EngineCapabilities.Punctuation | EngineCapabilities.Timestamps | EngineCapabilities.Hotwords)]
    [InlineData(AsrEngineKind.FunAsrNano, "fun-asr-nano-onnx",
        EngineCapabilities.Punctuation | EngineCapabilities.Timestamps | EngineCapabilities.Hotwords)]
    [InlineData(AsrEngineKind.QwenAsr, "qwen3-asr-onnx", EngineCapabilities.Punctuation)]
    public void Create_uses_the_engine_selected_by_config(
        AsrEngineKind kind, string expectedName, EngineCapabilities expectedCapabilities)
    {
        using var engine = WindowsAsrEngineFactory.Create(new AsrConfig { Engine = kind });

        engine.Name.Should().Be(expectedName);
        engine.Capabilities.Should().Be(expectedCapabilities);
    }
}
