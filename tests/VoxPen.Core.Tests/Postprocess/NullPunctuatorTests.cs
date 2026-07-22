using FluentAssertions;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Postprocess;
using Xunit;

namespace VoxPen.Core.Tests.Postprocess;

/// <summary>
/// <see cref="NullPunctuator"/> 的行为约束：
/// - 单例、幂等、原样返回；作为"无标点模式"的默认降级实现，必须不改动输入。
/// </summary>
public sealed class NullPunctuatorTests
{
    [Fact]
    public void Instance_is_singleton_and_reports_loaded()
    {
        var a = NullPunctuator.Instance;
        var b = NullPunctuator.Instance;

        a.Should().BeSameAs(b);
        a.IsLoaded.Should().BeTrue();
        a.Name.Should().Be("null");
    }

    [Fact]
    public void Implements_IPunctuator()
    {
        NullPunctuator.Instance.Should().BeAssignableTo<IPunctuator>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("今天天气不错")]
    [InlineData("今天天气不错，我们出去走走。")]
    [InlineData("hello world")]
    public void AddPunctuation_returns_input_unchanged(string input)
    {
        NullPunctuator.Instance.AddPunctuation(input).Should().Be(input);
    }

    [Fact]
    public async Task LoadAsync_completes_immediately()
    {
        var task = NullPunctuator.Instance.LoadAsync();
        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [Fact]
    public void Dispose_does_not_throw()
    {
        var act = () => NullPunctuator.Instance.Dispose();
        act.Should().NotThrow();
    }
}
