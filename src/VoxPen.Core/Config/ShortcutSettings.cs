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

    private static readonly HashSet<string> SupportedKeys = new(
        Options.Select(option => option.Key)
            .Concat(Enumerable.Range('a', 26).Select(code => ((char)code).ToString()))
            .Concat(Enumerable.Range(0, 10).Select(number => number.ToString()))
            .Concat([
                "left_ctrl", "right_ctrl", "left_shift", "right_shift", "left_alt", "right_alt",
                "left_meta", "right_meta", "space", "enter", "tab", "esc", "backspace", "delete",
                "insert", "home", "end", "page_up", "page_down", "up", "down", "left", "right",
                "num_lock", "scroll_lock", "print_screen", "pause", "context_menu", "minus", "equals",
                "comma", "period", "slash", "semicolon", "quote", "open_bracket", "close_bracket", "backslash",
                "mouse_left", "mouse_right", "mouse_middle"
            ]),
        StringComparer.OrdinalIgnoreCase);

    public static string GetDisplayName(string key)
    {
        var option = Options.FirstOrDefault(x =>
            string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        if (option is not null) return option.DisplayName;
        if (!IsSupported(key)) throw new ArgumentException($"不支持的快捷键：{key}", nameof(key));
        return key.Trim().ToLowerInvariant() switch
        {
            "left_ctrl" => "左 Ctrl", "right_ctrl" => "右 Ctrl",
            "left_shift" => "左 Shift", "right_shift" => "右 Shift",
            "left_alt" => "左 Alt", "right_alt" => "右 Alt",
            "left_meta" => "左 Win", "right_meta" => "右 Win",
            "space" => "空格", "enter" => "Enter", "esc" => "Esc", "page_up" => "Page Up", "page_down" => "Page Down",
            var value when value.Length == 1 => value.ToUpperInvariant(),
            var value => string.Join(' ', value.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..])),
        };
    }

    public static string GetDisplayName(IEnumerable<string> keys) =>
        string.Join(" + ", NormalizeKeys(keys).Select(GetDisplayName));

    public static bool IsSupported(string? key) =>
        !string.IsNullOrWhiteSpace(key) &&
        SupportedKeys.Contains(key.Trim());

    /// <summary>规范化组合键。单独的字母键会干扰正常输入，因此只允许作为组合的一部分录制。</summary>
    public static IReadOnlyList<string> NormalizeKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var normalized = keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
            throw new ArgumentException("至少需要一个快捷键", nameof(keys));
        if (normalized.Any(key => !IsSupported(key)))
            throw new ArgumentException("快捷键包含无法识别的键名", nameof(keys));
        if (normalized.Length == 1 && normalized[0].Length == 1 && char.IsLetter(normalized[0][0]))
            throw new ArgumentException("禁止单独使用字母键作为快捷键，请搭配修饰键或功能键。", nameof(keys));

        return normalized;
    }

    public static void Save(string path, AppConfig config, string key)
        => Save(path, config, new[] { key });

    public static void Save(string path, AppConfig config, IEnumerable<string> keys)
    {
        var normalized = NormalizeKeys(keys);

        var oldKey = config.Shortcut.Key;
        var oldKeys = config.Shortcut.Keys;
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            config.Shortcut.Key = normalized[0];
            config.Shortcut.Keys = normalized.ToList();

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
