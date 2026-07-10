namespace Prtsc.Core.Common;

/// <summary>
/// 物理像素矩形，用于虚拟桌面与显示器几何计算。始终以 physical device pixel 为单位。
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int px, int py) => px >= X && py >= Y && px < Right && py < Bottom;

    public PixelRect Intersect(PixelRect other)
    {
        var x1 = Math.Max(X, other.X);
        var y1 = Math.Max(Y, other.Y);
        var x2 = Math.Min(Right, other.Right);
        var y2 = Math.Min(Bottom, other.Bottom);
        return x2 <= x1 || y2 <= y1 ? default : new PixelRect(x1, y1, x2 - x1, y2 - y1);
    }

    public static PixelRect Union(PixelRect a, PixelRect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var x1 = Math.Min(a.X, b.X);
        var y1 = Math.Min(a.Y, b.Y);
        var x2 = Math.Max(a.Right, b.Right);
        var y2 = Math.Max(a.Bottom, b.Bottom);
        return new PixelRect(x1, y1, x2 - x1, y2 - y1);
    }

    public override string ToString() => $"[{X},{Y} {Width}x{Height}]";
}
