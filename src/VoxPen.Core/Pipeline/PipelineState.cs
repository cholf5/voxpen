namespace VoxPen.Core.Pipeline;

/// <summary>
/// 听写流水线的状态机。托盘图标颜色、UI 状态灯都读这个。
/// </summary>
public enum PipelineState
{
    /// <summary>空闲，等待快捷键。</summary>
    Idle,

    /// <summary>正在录音（快捷键按住中）。</summary>
    Recording,

    /// <summary>录音结束，正在推理。</summary>
    Recognizing,

    /// <summary>推理完成，正在上屏。</summary>
    Outputting,

    /// <summary>发生错误（例如模型未加载、麦克风未授权）。</summary>
    Error,

    /// <summary>已暂停（用户从托盘菜单点了"暂停监听"）。</summary>
    Paused,
}
