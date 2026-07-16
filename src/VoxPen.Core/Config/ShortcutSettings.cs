namespace VoxPen.Core.Config;

/// <summary>快捷键设置页使用的预设选项。</summary>
public sealed record ShortcutOption(string Key, string DisplayName);

public static class ShortcutSettings
{
    public const string DefaultKey = "caps_lock";

    public static IReadOnlyList<ShortcutOption> Options { get; } =
    [
        new("caps_lock", "Caps Lock"),
        new("x1", "鼠标后退侧键"),
        new("x2", "鼠标前进侧键"),
        new("f1", "F1"),
        new("f2", "F2"),
        new("f3", "F3"),
        new("f4", "F4"),
        new("f5", "F5"),
        new("f6", "F6"),
        new("f7", "F7"),
        new("f8", "F8"),
        new("f9", "F9"),
        new("f10", "F10"),
        new("f11", "F11"),
        new("f12", "F12"),
        new("f13", "F13"),
        new("f14", "F14"),
        new("f15", "F15"),
        new("f16", "F16"),
    ];

    public static string GetDisplayName(string key)
    {
        var option = Options.FirstOrDefault(x =>
            string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        return option?.DisplayName
            ?? throw new ArgumentException($"不支持的快捷键：{key}", nameof(key));
    }

    public static bool IsSupported(string? key) =>
        !string.IsNullOrWhiteSpace(key) &&
        Options.Any(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    public static void Save(string path, AppConfig config, string key)
    {
        if (!IsSupported(key))
        {
            throw new ArgumentException($"不支持的快捷键：{key}", nameof(key));
        }

        var oldKey = config.Shortcut.Key;
        var oldKeys = config.Shortcut.Keys;
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            config.Shortcut.Key = key;
            config.Shortcut.Keys = [key];

            var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            });
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            config.Shortcut.Key = oldKey;
            config.Shortcut.Keys = oldKeys;
        }
    }
}
