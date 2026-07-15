using Xunit;

namespace VoxPen.Core.Tests;

/// <summary>
/// 骨架自检：确认测试基础设施本身能跑起来。
/// </summary>
public class SmokeTests
{
    [Fact]
    public void TestFrameworkIsWiredUp()
    {
        Assert.True(true);
    }
}
