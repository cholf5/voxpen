namespace VoxPen.Core.Config;

/// <summary>与上游 CapsWriter-Offline 对齐的离线语音识别引擎。</summary>
public enum AsrEngineKind
{
    Paraformer,
    SenseVoice,
    FunAsrNano,
    QwenAsr,
}
