using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;

namespace VoxPen.Platform.Windows.Recognition;

/// <summary>Windows 组合根使用的 ASR 引擎工厂。</summary>
public static class WindowsAsrEngineFactory
{
    public static IAsrEngine Create(AsrConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.Engine switch
        {
            AsrEngineKind.Paraformer => new ParaformerEngine(config),
            AsrEngineKind.SenseVoice => new SenseVoiceEngine(config),
            AsrEngineKind.FunAsrNano => new FunAsrNanoEngine(config),
            AsrEngineKind.QwenAsr => new Qwen3AsrEngine(config),
            _ => throw new ArgumentOutOfRangeException(nameof(config.Engine), config.Engine, "不支持的 ASR 引擎"),
        };
    }
}
