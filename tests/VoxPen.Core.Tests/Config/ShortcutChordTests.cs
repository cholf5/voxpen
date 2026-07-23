using FluentAssertions;
using VoxPen.Core.Config;
using Xunit;

namespace VoxPen.Core.Tests.Config;

public sealed class ShortcutChordTests
{
    [Fact]
    public void Press_should_start_only_when_the_last_key_completes_the_chord()
    {
        var chord = new ShortcutChord(["left_ctrl", "a"]);

        chord.Press("left_ctrl").Should().BeFalse();
        chord.Press("c").Should().BeFalse();
        chord.Press("a").Should().BeTrue();
    }

    [Fact]
    public void Release_should_end_only_when_a_completed_chord_is_broken()
    {
        var chord = new ShortcutChord(["left_ctrl", "a"]);
        chord.Press("left_ctrl");
        chord.Press("a");

        chord.Release("c").Should().BeFalse();
        chord.Release("a").Should().BeTrue();
    }
}
