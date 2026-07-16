using FluentAssertions;
using VoxPen.Core.Config;
using Xunit;

namespace VoxPen.Core.Tests.Config;

public sealed class ModelDirectoryValidatorTests
{
    [Fact]
    public void Validate_should_accept_int8_model_with_tokens()
    {
        var dir = CreateDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "model.int8.onnx"), "model");
            File.WriteAllText(Path.Combine(dir, "tokens.txt"), "tokens");

            var result = ModelDirectoryValidator.Validate(dir);

            result.IsValid.Should().BeTrue();
            result.ModelPath.Should().Be(Path.Combine(dir, "model.int8.onnx"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Validate_should_reject_directory_without_required_files()
    {
        var dir = CreateDirectory();
        try
        {
            var result = ModelDirectoryValidator.Validate(dir);

            result.IsValid.Should().BeFalse();
            result.Message.Should().Contain("model.int8.onnx");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Validate_should_reject_missing_directory()
    {
        var result = ModelDirectoryValidator.Validate(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("不存在");
    }

    private static string CreateDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"voxpen-model-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
