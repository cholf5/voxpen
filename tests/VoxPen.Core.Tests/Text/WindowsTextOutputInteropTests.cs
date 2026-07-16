using System.Reflection;
using System.Runtime.InteropServices;
using FluentAssertions;
using VoxPen.Platform.Windows.Text;
using Xunit;

namespace VoxPen.Core.Tests.Text;

public sealed class WindowsTextOutputInteropTests
{
    [Fact]
    public void SendInputStructure_MatchesWin32InputSize()
    {
        var inputType = typeof(WindowsTextOutput).GetNestedType("INPUT", BindingFlags.NonPublic);

        inputType.Should().NotBeNull();
        Marshal.SizeOf(inputType!).Should().Be(Environment.Is64BitProcess ? 40 : 28);
    }
}
