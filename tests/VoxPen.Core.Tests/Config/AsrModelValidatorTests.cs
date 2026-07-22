using FluentAssertions;
using VoxPen.Core.Config;
using Xunit;

namespace VoxPen.Core.Tests.Config;

public sealed class AsrModelValidatorTests
{
    [Theory]
    [InlineData(AsrEngineKind.Paraformer)]
    [InlineData(AsrEngineKind.SenseVoice)]
    [InlineData(AsrEngineKind.FunAsrNano)]
    [InlineData(AsrEngineKind.QwenAsr)]
    public void Validate_accepts_a_directory_with_all_required_files(AsrEngineKind kind)
    {
        var directory = CreateDirectory();
        try
        {
            var definition = AsrModelCatalog.Get(kind);
            foreach (var relativePath in definition.RequiredFiles)
            {
                var filePath = Path.Combine(directory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, "fixture");
            }

            var result = AsrModelValidator.Validate(definition, directory);

            result.IsValid.Should().BeTrue();
            result.Message.Should().Be("模型文件完整");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Validate_identifies_the_missing_file_for_the_selected_engine()
    {
        var directory = CreateDirectory();
        try
        {
            var definition = AsrModelCatalog.Get(AsrEngineKind.SenseVoice);
            var result = AsrModelValidator.Validate(definition, directory);

            result.IsValid.Should().BeFalse();
            result.Message.Should().Contain("model.int8.onnx");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"voxpen-asr-model-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
