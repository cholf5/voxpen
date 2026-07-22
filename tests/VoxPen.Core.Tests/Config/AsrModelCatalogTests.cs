using FluentAssertions;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;
using Xunit;

namespace VoxPen.Core.Tests.Config;

public sealed class AsrModelCatalogTests
{
    [Theory]
    [InlineData(AsrEngineKind.Paraformer, "Paraformer", EngineCapabilities.Timestamps)]
    [InlineData(AsrEngineKind.SenseVoice, "SenseVoice-Small",
        EngineCapabilities.Punctuation | EngineCapabilities.Timestamps | EngineCapabilities.Hotwords)]
    [InlineData(AsrEngineKind.FunAsrNano, "Fun-ASR-Nano",
        EngineCapabilities.Punctuation | EngineCapabilities.Timestamps | EngineCapabilities.Hotwords)]
    [InlineData(AsrEngineKind.QwenAsr, "Qwen3-ASR", EngineCapabilities.Punctuation)]
    public void Get_returns_the_upstream_supported_model_definition(
        AsrEngineKind kind, string displayName, EngineCapabilities capabilities)
    {
        var definition = AsrModelCatalog.Get(kind);

        definition.DisplayName.Should().Be(displayName);
        definition.Capabilities.Should().Be(capabilities);
        definition.DownloadUrl.AbsolutePath.Should().Contain(kind == AsrEngineKind.Paraformer
            ? "/HaujetZhao/CapsWriter-Offline/releases/download/models/"
            : "/k2-fsa/sherpa-onnx/releases/download/asr-models/");
        definition.RequiredFiles.Should().NotBeEmpty();
    }

    [Fact]
    public void All_contains_exactly_the_four_upstream_asr_engines()
    {
        AsrModelCatalog.All.Select(model => model.Kind).Should().BeEquivalentTo(new[]
        {
            AsrEngineKind.Paraformer,
            AsrEngineKind.SenseVoice,
            AsrEngineKind.FunAsrNano,
            AsrEngineKind.QwenAsr,
        });
    }

    [Theory]
    [InlineData(AsrEngineKind.SenseVoice, "model.int8.onnx", "tokens.txt")]
    [InlineData(AsrEngineKind.FunAsrNano, "encoder_adaptor.int8.onnx", "Qwen3-0.6B/tokenizer.json")]
    [InlineData(AsrEngineKind.QwenAsr, "conv_frontend.onnx", "tokenizer/vocab.json")]
    public void Official_sherpa_onnx_packages_define_their_required_files(
        AsrEngineKind kind, params string[] requiredFiles)
    {
        var definition = AsrModelCatalog.Get(kind);

        definition.DownloadUrl.Host.Should().Be("github.com");
        definition.PackageName.Should().EndWith(".tar.bz2");
        definition.RequiredFiles.Should().Contain(requiredFiles);
    }
}
