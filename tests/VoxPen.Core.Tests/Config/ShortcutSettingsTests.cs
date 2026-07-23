using FluentAssertions;
using System.Text.Json;
using Xunit;
using VoxPen.Core.Config;
using VoxPen.Platform.Windows.Hooks;
using SharpHook.Data;

namespace VoxPen.Core.Tests.Config;

public sealed class ShortcutSettingsTests
{
    [Fact]
    public void Options_should_keep_caps_lock_as_default_and_expose_supported_keys()
    {
        ShortcutSettings.DefaultKey.Should().Be("caps_lock");
        ShortcutSettings.Options.Should().Contain(option => option.Key == "caps_lock" && option.DisplayName == "Caps Lock");
        ShortcutSettings.Options.Should().Contain(option => option.Key == "x2" && option.DisplayName.Contains("前进"));

        ShortcutSettings.Options
            .Where(option => option.Key.StartsWith("f", StringComparison.Ordinal))
            .Select(option => option.Key)
            .Should()
            .ContainInOrder(Enumerable.Range(1, 12).Select(index => $"f{index}"));
    }

    [Theory]
    [InlineData("caps_lock", "Caps Lock")]
    [InlineData("x1", "鼠标后退侧键")]
    [InlineData("x2", "鼠标前进侧键")]
    [InlineData("f13", "F13")]
    public void GetDisplayName_should_translate_supported_key(string key, string expected)
    {
        ShortcutSettings.GetDisplayName(key).Should().Be(expected);
    }

    [Fact]
    public void GetDisplayName_should_reject_unknown_key()
    {
        var action = () => ShortcutSettings.GetDisplayName("unknown");

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Save_should_write_both_current_and_legacy_shortcut_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"voxpen-config-{Guid.NewGuid():N}.json");
        try
        {
            var config = new AppConfig();
            ShortcutSettings.Save(path, config, "x2");

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var shortcut = document.RootElement.GetProperty("shortcut");
            shortcut.GetProperty("key").GetString().Should().Be("x2");
            shortcut.GetProperty("keys")[0].GetString().Should().Be("x2");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Save_should_reject_unknown_key_without_creating_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"voxpen-config-{Guid.NewGuid():N}.json");
        var action = () => ShortcutSettings.Save(path, new AppConfig(), "unknown");

        action.Should().Throw<ArgumentException>();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void NormalizeKeys_should_keep_a_combination_and_reject_a_single_letter()
    {
        ShortcutSettings.NormalizeKeys(["left_ctrl", "left_shift", "a", "left_ctrl"])
            .Should().Equal("left_ctrl", "left_shift", "a");

        var action = () => ShortcutSettings.NormalizeKeys(["a"]);

        action.Should().Throw<ArgumentException>().WithMessage("*单独使用字母键*");
    }

    [Fact]
    public void Save_should_write_all_combination_keys_and_keep_the_first_as_legacy_key()
    {
        var path = Path.Combine(Path.GetTempPath(), $"voxpen-config-{Guid.NewGuid():N}.json");
        try
        {
            ShortcutSettings.Save(path, new AppConfig(), ["left_ctrl", "left_shift", "a"]);

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var shortcut = document.RootElement.GetProperty("shortcut");
            shortcut.GetProperty("key").GetString().Should().Be("left_ctrl");
            shortcut.GetProperty("keys").EnumerateArray().Select(x => x.GetString())
                .Should().Equal("left_ctrl", "left_shift", "a");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void KeyNameMapper_should_map_modifiers_and_letter_keys_used_by_recording()
    {
        KeyNameMapper.Map("left_ctrl").Should().Be(KeyCode.VcLeftControl);
        KeyNameMapper.Map("a").Should().Be(KeyCode.VcA);
        KeyNameMapper.GetName(KeyCode.VcRightShift).Should().Be("right_shift");
    }
}
