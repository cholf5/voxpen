using FluentAssertions;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;
using VoxPen.Platform.Windows.Recognition;
using Xunit;

namespace VoxPen.Core.Tests.Recognition;

/// <summary>
/// 校验 <see cref="ParaformerEngine"/> 通过 <see cref="EngineCapabilities"/> 声明自身能力。
///
/// - 具备 <see cref="EngineCapabilities.Timestamps"/>（Paraformer 会产出字级时间戳）。
/// - 不具备 <see cref="EngineCapabilities.Punctuation"/>（不能视为自带标点，需要外挂标点模型）。
///
/// 构造 ParaformerEngine 不会加载模型（<see cref="ParaformerEngine.LoadAsync"/> 才会碰文件），
/// 因此本测试在无模型的 CI 环境里也能跑。
/// </summary>
public sealed class ParaformerEngineCapabilitiesTests
{
    [Fact]
    public void Reports_timestamps_capability()
    {
        using var engine = new ParaformerEngine(new AsrConfig());

        (engine.Capabilities & EngineCapabilities.Timestamps).Should().Be(EngineCapabilities.Timestamps);
    }

    [Fact]
    public void Does_not_report_punctuation_capability()
    {
        using var engine = new ParaformerEngine(new AsrConfig());

        (engine.Capabilities & EngineCapabilities.Punctuation).Should().Be(EngineCapabilities.None);
    }

    [Fact]
    public void Interface_default_returns_none()
    {
        // 直接读接口默认实现：任意未覆盖的 IAsrEngine 实现应回退到 None。
        // 这是给未来第三方引擎的兜底行为。
        EngineCapabilities defaultValue = default;
        defaultValue.Should().Be(EngineCapabilities.None);
    }
}
