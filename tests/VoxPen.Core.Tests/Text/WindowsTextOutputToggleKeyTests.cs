using FluentAssertions;
using VoxPen.Platform.Windows.Text;
using Xunit;

namespace VoxPen.Core.Tests.Text;

public sealed class WindowsTextOutputToggleKeyTests
{
    [Theory]
    [InlineData("caps_lock")]
    [InlineData("CapsLock")]
    [InlineData("num_lock")]
    [InlineData("numlock")]
    [InlineData("scroll_lock")]
    [InlineData("scrolllock")]
    [InlineData("  Caps_Lock  ")]
    public void RecognisesToggleKeys(string name)
    {
        WindowsTextOutput.IsToggleKey(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("f1")]
    [InlineData("f13")]
    [InlineData("x1")]
    [InlineData("x2")]
    [InlineData("space")]
    [InlineData("")]
    [InlineData("caps")]
    public void RejectsNonToggleKeys(string name)
    {
        WindowsTextOutput.IsToggleKey(name).Should().BeFalse();
    }

    [Fact]
    public void ResendToggleKey_ReturnsFalseForUnknownName_WithoutDispatch()
    {
        var output = new WindowsTextOutput();

        // 非 toggle 键：不排队任何注入
        output.ResendToggleKey("x2").Should().BeFalse();
        output.ResendToggleKey("f5").Should().BeFalse();
        output.ResendToggleKey(string.Empty).Should().BeFalse();
    }
}
