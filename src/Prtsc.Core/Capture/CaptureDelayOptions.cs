namespace Prtsc.Core.Capture;

/// <summary>
/// D-1：延时截图秒数合法值表（0 = 不延时）。托盘子菜单、设置页下拉、Capture Launcher、L-1 CLI
/// <c>--delay</c> 都以这份表为准；config 里出现非法值时钳到最近合法值。
/// </summary>
public static class CaptureDelayOptions
{
    /// <summary>合法延时秒数（升序）。</summary>
    public static readonly int[] AllowedSeconds = { 0, 3, 5 };

    /// <summary>钳到最近的合法值；两侧等距取较小（4 → 3）。负数/0 一律归 0。</summary>
    public static int Clamp(int seconds)
    {
        var best = AllowedSeconds[0];
        foreach (var candidate in AllowedSeconds)
        {
            var d = Math.Abs(candidate - seconds);
            var bestD = Math.Abs(best - seconds);
            if (d < bestD || (d == bestD && candidate < best))
                best = candidate;
        }
        return best;
    }

    /// <summary>合法值在 UI 选择器（下拉/单选）里的下标；非法值先钳制再查表。</summary>
    public static int IndexOf(int seconds)
    {
        var clamped = Clamp(seconds);
        var index = Array.IndexOf(AllowedSeconds, clamped);
        return index < 0 ? 0 : index;
    }
}
