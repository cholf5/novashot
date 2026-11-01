using SkiaSharp;

namespace Prtsc.Core.Annotation.Tools;

/// <summary>
/// 两点类工具绘制时的角度吸附（Flameshot drawMoveWithAdjustment 语义）：
/// - <see cref="Snap45"/>：吸附水平/垂直/±45° 对角（Line / Arrow 系）；
/// - <see cref="SnapOrtho"/>：仅水平/垂直（Rectangle / Ellipse 的对角拖拽）。
/// 吸附语义 = 保留主分量：水平/垂直保留较大位移分量，对角取两分量较大值。
/// </summary>
public static class AngleSnap
{
    /// <summary>距吸附方向超过该角度（度）时不吸附，原样返回。</summary>
    public const float MaxSnapDegrees = 10f;

    private const float DegreesToRadians = MathF.PI / 180f;

    /// <summary>吸附到水平 / 垂直 / ±45°（八方向中最近的），容差 10°，超容差原样。</summary>
    public static SKPoint Snap45(float dx, float dy)
    {
        var angle = MathF.Atan2(dy, dx) / DegreesToRadians;
        var snapped = MathF.Round(angle / 45f) * 45f;
        if (AngleDelta(angle, snapped) > MaxSnapDegrees) return new SKPoint(dx, dy);

        var deg = MathF.Abs(snapped);
        if (deg is 0f or 180f) return new SKPoint(dx, 0);
        if (deg is 90f) return new SKPoint(0, dy);

        // ±45°/±135°：两分量取较大值（保号）
        var m = MathF.Max(MathF.Abs(dx), MathF.Abs(dy));
        return new SKPoint(MathF.Sign(dx) * m, MathF.Sign(dy) * m);
    }

    /// <summary>仅吸附水平 / 垂直，容差 10°，超容差原样。</summary>
    public static SKPoint SnapOrtho(float dx, float dy)
    {
        var angle = MathF.Atan2(dy, dx);
        // 0°/180° 归为水平，90°/270° 归为垂直
        var horizontalDist = MathF.Min(MathF.Abs(angle), MathF.Abs(MathF.PI - MathF.Abs(angle)));
        var verticalDist = MathF.Abs(MathF.PI / 2f - MathF.Abs(angle));
        var maxRad = MaxSnapDegrees * DegreesToRadians;

        if (horizontalDist <= maxRad) return new SKPoint(dx, 0);
        if (verticalDist <= maxRad) return new SKPoint(0, dy);
        return new SKPoint(dx, dy);
    }

    /// <summary>仅吸附 ±45° / ±135° 对角（矩形→正方形、椭圆→正圆），容差 10°，超容差原样。</summary>
    public static SKPoint SnapDiagonal(float dx, float dy)
    {
        var angle = MathF.Atan2(dy, dx) / DegreesToRadians;
        var snapped = MathF.Round(angle / 45f) * 45f;
        var deg = MathF.Abs(snapped);
        if (deg is not (45f or 135f)) return new SKPoint(dx, dy); // 非 ±45°/±135° 不吸附
        if (AngleDelta(angle, snapped) > MaxSnapDegrees) return new SKPoint(dx, dy);

        var m = MathF.Max(MathF.Abs(dx), MathF.Abs(dy));
        return new SKPoint(MathF.Sign(dx) * m, MathF.Sign(dy) * m);
    }

    private static float AngleDelta(float a, float b)
    {
        var d = MathF.Abs(a - b) % 360f;
        return d > 180f ? 360f - d : d;
    }
}
