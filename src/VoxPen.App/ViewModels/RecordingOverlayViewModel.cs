using CommunityToolkit.Mvvm.ComponentModel;

namespace VoxPen.App.ViewModels;

/// <summary>
/// 录音状态浮窗的 VM。仅暴露 5 段电平（0..1），由 UI 转成条形高度；
/// 平滑与衰减由 <see cref="Push(float)"/> 负责，UI 侧无状态。
/// </summary>
public sealed partial class RecordingOverlayViewModel : ObservableObject
{
    // 5 个条对应不同的响应速度，模拟频谱条的错落感
    [ObservableProperty] private double _bar0;
    [ObservableProperty] private double _bar1;
    [ObservableProperty] private double _bar2;
    [ObservableProperty] private double _bar3;
    [ObservableProperty] private double _bar4;

    // 每根条的目标值（新样本推入后立即更新），当前值向目标值平滑逼近
    private readonly double[] _target = new double[5];
    private readonly double[] _current = new double[5];

    // 每根条的衰减 / 上升速度：靠中间的条更"活跃"
    private static readonly double[] AttackWeights = { 0.55, 0.75, 0.90, 0.75, 0.55 };

    /// <summary>输入一次电平（0..1）；VM 会分配到 5 条并推进平滑。</summary>
    public void Push(float level)
    {
        var l = level;
        if (l < 0) l = 0;
        if (l > 1) l = 1;

        for (int i = 0; i < 5; i++)
        {
            // 目标值 = 原始电平 × 位置权重（中间高、两端低），加入伪随机抖动感
            var target = l * AttackWeights[i];
            // 用位置和电平构造轻量抖动，避免所有条完全同步
            var jitter = 0.08 * System.Math.Sin((_frame + i * 1.7) * 0.9) * l;
            target += jitter;
            if (target < 0) target = 0;
            if (target > 1) target = 1;
            _target[i] = target;
        }
        _frame++;

        // 立即向目标推进一次（Push 频率就是音频块频率，~10..30ms 一次）
        Advance();
        Publish();
    }

    /// <summary>UI 定时器每帧调用（例如 30fps），继续把 current 拉向 target，同时施加自然衰减。</summary>
    public void Tick()
    {
        // 无新样本时目标缓慢衰减到 0，让条自然回落
        for (int i = 0; i < 5; i++)
        {
            _target[i] *= 0.85;
        }
        Advance();
        Publish();
    }

    /// <summary>清零所有条（停止录音时调用）。</summary>
    public void Reset()
    {
        for (int i = 0; i < 5; i++)
        {
            _target[i] = 0;
            _current[i] = 0;
        }
        Publish();
    }

    private long _frame;

    private void Advance()
    {
        // 上升快、下降慢：更接近人眼观感
        for (int i = 0; i < 5; i++)
        {
            var diff = _target[i] - _current[i];
            var factor = diff > 0 ? 0.55 : 0.20;
            _current[i] += diff * factor;
        }
    }

    private void Publish()
    {
        Bar0 = _current[0];
        Bar1 = _current[1];
        Bar2 = _current[2];
        Bar3 = _current[3];
        Bar4 = _current[4];
    }
}
