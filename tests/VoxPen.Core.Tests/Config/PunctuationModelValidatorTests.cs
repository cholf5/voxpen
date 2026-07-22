using FluentAssertions;
using VoxPen.Core.Config;
using Xunit;

namespace VoxPen.Core.Tests.Config;

public sealed class PunctuationModelValidatorTests
{
    [Fact]
    public void Validate_should_accept_directory_with_model_onnx()
    {
        var dir = CreateDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "model.onnx"), "model");

            var result = PunctuationModelValidator.Validate(dir);

            result.IsValid.Should().BeTrue();
            result.ModelPath.Should().Be(Path.Combine(dir, "model.onnx"));
            result.Message.Should().Contain("完整");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Validate_should_reject_directory_without_model_onnx()
    {
        var dir = CreateDirectory();
        try
        {
            // 只放一个 tokens 文件；缺 model.onnx 应仍被判失败。
            File.WriteAllText(Path.Combine(dir, "tokens.json"), "tokens");

            var result = PunctuationModelValidator.Validate(dir);

            result.IsValid.Should().BeFalse();
            result.Message.Should().Be("缺少 model.onnx");
            result.ModelPath.Should().BeNull();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Validate_should_reject_missing_directory()
    {
        var result = PunctuationModelValidator.Validate(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("不存在");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_should_reject_empty_input(string? modelDir)
    {
        var result = PunctuationModelValidator.Validate(modelDir);

        result.IsValid.Should().BeFalse();
        result.Message.Should().Be("标点模型目录为空");
    }

    private static string CreateDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"voxpen-punct-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
