namespace VoxPen.Core.Config;

/// <summary>设置页一次提交的快捷键与识别模型选择。</summary>
public static class SettingsSelection
{
    public static void Apply(AppConfig config, string shortcutKey, AsrEngineKind asrEngine)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutKey);

        config.Shortcut.Key = shortcutKey;
        config.Shortcut.Keys = [shortcutKey];
        config.Asr.Engine = asrEngine;
        config.Asr.ModelDir = AsrModelCatalog.Get(asrEngine).DefaultModelDir;
    }
}
