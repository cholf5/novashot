using Prtsc.Core.Capture;
using Xunit;

namespace Prtsc.Core.Tests;

/// <summary>
/// D-1：延时秒数合法值表与钳制。合法值 0/3/5，其他值钳到最近合法值（等距取较小，4 → 3）。
/// </summary>
public class CaptureDelayOptionsTests
{
    [Fact]
    public void AllowedSeconds_IsZeroThreeFive()
    {
        Assert.Equal(new[] { 0, 3, 5 }, CaptureDelayOptions.AllowedSeconds);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    public void Clamp_LegalValues_PassThrough(int input, int expected)
    {
        Assert.Equal(expected, CaptureDelayOptions.Clamp(input));
    }

    [Theory]
    [InlineData(-100, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, 0)]   // |1-0|=1 < |1-3|=2
    [InlineData(2, 3)]   // |2-3|=1 < |2-0|=2
    [InlineData(4, 3)]   // 等距 3/5，取较小
    [InlineData(6, 5)]
    [InlineData(7, 5)]
    [InlineData(999, 5)]
    public void Clamp_IllegalValues_RoundToNearest(int input, int expected)
    {
        Assert.Equal(expected, CaptureDelayOptions.Clamp(input));
    }

    [Fact]
    public void IndexOf_MapsConfigValueToSelectorIndex()
    {
        Assert.Equal(0, CaptureDelayOptions.IndexOf(0));
        Assert.Equal(1, CaptureDelayOptions.IndexOf(3));
        Assert.Equal(2, CaptureDelayOptions.IndexOf(5));
        Assert.Equal(0, CaptureDelayOptions.IndexOf(-7)); // 非法值钳到 0 → 下标 0
        Assert.Equal(2, CaptureDelayOptions.IndexOf(99));
    }

    // —— Resolve（review M-2：咽喉点延时解析抽成 Core 纯函数） ——

    [Theory]
    [InlineData(3, 0, 3)]    // 显式意图优先（托盘延时子项 / Launcher）
    [InlineData(0, 3, 0)]    // 显式 0 = 不延时，覆盖 config 默认
    [InlineData(null, 3, 3)] // 无显式 → config 默认
    [InlineData(null, 99, 5)] // config 非法值钳制
    [InlineData(null, -1, 0)]
    [InlineData(-2, 3, 0)]   // 显式负数归 0（防御，正常路径不出现）
    public void Resolve_ExplicitWinsOtherwiseClampedConfig(int? explicitSeconds, int configured, int expected)
    {
        Assert.Equal(expected, CaptureDelayOptions.Resolve(explicitSeconds, configured));
    }
}
