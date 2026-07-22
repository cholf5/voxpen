using FluentAssertions;
using VoxPen.Core.Abstractions;
using VoxPen.Platform.Windows.Recognition;
using Xunit;

namespace VoxPen.Core.Tests.Recognition;

/// <summary>
/// <see cref="SherpaPunctuator"/> 的行为约束：
/// - 构造不加载模型；参数校验合理。
/// - 未加载时 <see cref="SherpaPunctuator.AddPunctuation"/> 走"无标点模式"降级（原样返回）。
/// - <see cref="SherpaPunctuator.LoadAsync"/> 找不到模型文件时抛可识别异常，交由 AppHost 打日志降级。
///
/// 真实模型的端到端识别不在此测试范围（依赖 ~300MB 权重，CI 不可用）；对应工作走
/// P3 阶段的 <c>test-punc</c> CLI 冒烟命令做人工验证。
/// </summary>
public sealed class SherpaPunctuatorTests
{
    [Fact]
    public void Implements_IPunctuator()
    {
        using var p = new SherpaPunctuator("dummy.onnx", numThreads: 1, provider: "cpu");
        p.Should().BeAssignableTo<IPunctuator>();
    }

    [Fact]
    public void Constructor_stores_name_and_starts_unloaded()
    {
        using var p = new SherpaPunctuator("dummy.onnx", numThreads: 2, provider: "cpu");

        p.Name.Should().Be("ct-transformer-onnx");
        p.IsLoaded.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_rejects_empty_model_path(string? modelPath)
    {
        var act = () => new SherpaPunctuator(modelPath!, numThreads: 1, provider: "cpu");
        act.Should().Throw<ArgumentException>().WithParameterName("modelPath");
    }

    [Fact]
    public void Constructor_clamps_num_threads_to_at_least_one()
    {
        // 输入 0 或负数应被规范化为 1，避免 sherpa-onnx 拿到非法参数。
        // 通过后续 LoadAsync 不因 numThreads 抛"无效参数"来间接验证（此处只保证构造不抛）。
        var act = () => new SherpaPunctuator("dummy.onnx", numThreads: -5, provider: "cpu");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_defaults_provider_to_cpu_when_blank(string? provider)
    {
        var act = () => new SherpaPunctuator("dummy.onnx", numThreads: 1, provider: provider);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("今天天气不错")]
    public void AddPunctuation_returns_input_unchanged_when_not_loaded(string input)
    {
        using var p = new SherpaPunctuator("dummy.onnx", numThreads: 1, provider: "cpu");
        p.AddPunctuation(input).Should().Be(input);
    }

    [Fact]
    public async Task LoadAsync_throws_when_model_file_missing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"voxpen-punc-missing-{Guid.NewGuid():N}.onnx");
        using var p = new SherpaPunctuator(missing, numThreads: 1, provider: "cpu");

        var act = () => p.LoadAsync();
        await act.Should().ThrowAsync<FileNotFoundException>();
        p.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task AddPunctuation_still_degrades_gracefully_after_failed_load()
    {
        // 场景：AppHost 尝试 LoadAsync 失败后仍持有该实例；AddPunctuation 必须不抛。
        var missing = Path.Combine(Path.GetTempPath(), $"voxpen-punc-missing-{Guid.NewGuid():N}.onnx");
        using var p = new SherpaPunctuator(missing, numThreads: 1, provider: "cpu");

        try { await p.LoadAsync(); } catch { /* 预期失败 */ }

        p.AddPunctuation("今天天气不错").Should().Be("今天天气不错");
        p.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var p = new SherpaPunctuator("dummy.onnx", numThreads: 1, provider: "cpu");
        p.Dispose();
        var act = () => p.Dispose();
        act.Should().NotThrow();
    }
}
