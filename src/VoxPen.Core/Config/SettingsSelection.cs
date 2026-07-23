namespace VoxPen.Core.Config;

/// <summary>设置页一次提交的快捷键与识别模型选择。</summary>
public static class SettingsSelection
{
    public static void Apply(AppConfig config, string shortcutKey, AsrEngineKind asrEngine)
        => Apply(config, new[] { shortcutKey }, asrEngine);

    public static void Apply(AppConfig config, IEnumerable<string> shortcutKeys, AsrEngineKind asrEngine)
    {
        ArgumentNullException.ThrowIfNull(config);
        var normalized = ShortcutSettings.NormalizeKeys(shortcutKeys);

        config.Shortcut.Key = normalized[0];
        config.Shortcut.Keys = normalized.ToList();
        config.Asr.Engine = asrEngine;
        config.Asr.ModelDir = AsrModelCatalog.Get(asrEngine).DefaultModelDir;
    }
}
