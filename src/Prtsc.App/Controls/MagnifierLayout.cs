using System;
using Avalonia;

namespace Prtsc.App.Controls;

/// <summary>放大镜网格 + 浮签的一组摆放结果（DIP）。</summary>
public readonly record struct MagnifierPlacement(Rect Grid, Rect Label);

/// <summary>
/// E-1 M9：取色放大镜的摆放纯几何，独立成 static helper 便于单元测试（同 SelectionGeometry 风格）。
/// 坐标全为 DIP，Rect 语义与 Avalonia 一致。
///
/// - 网格以光标为中心（中心格 = 光标所指像素）。刻意不做贴边搬移：搬移会让「中心格高亮」
///   与实际采样点脱节；出界部分由 overlay 窗口裁剪（flameshot 同款行为）。
/// - 浮签默认在网格正下方、与网格水平居中；底部放不下翻到网格上方；横向钳进安全边距
///   （简报：浮签跟随光标偏移避让屏幕边缘）。
/// </summary>
public static class MagnifierLayout
{
    /// <summary>浮签与网格的间距（DIP）。</summary>
    public const double Gap = 6;

    /// <summary>浮签距屏幕边缘的安全边距（DIP）。</summary>
    public const double Margin = 8;

    public static MagnifierPlacement ComputePlacement(
        Point cursor, Size gridSize, Size labelSize, Size surface,
        double gap = Gap, double margin = Margin)
    {
        var grid = new Rect(
            cursor.X - gridSize.Width / 2,
            cursor.Y - gridSize.Height / 2,
            gridSize.Width,
            gridSize.Height);

        // 浮签纵向：默认下方；底部放不下翻上方；再钳进 [margin, surface.Height - margin - labelH]
        var labelY = grid.Bottom + gap;
        if (labelY + labelSize.Height > surface.Height - margin)
            labelY = grid.Y - gap - labelSize.Height;
        var maxY = Math.Max(margin, surface.Height - margin - labelSize.Height);
        labelY = Math.Clamp(labelY, margin, maxY);

        // 浮签横向：与网格居中；钳进 [margin, surface.Width - margin - labelW]
        var labelX = grid.Center.X - labelSize.Width / 2;
        var maxX = Math.Max(margin, surface.Width - margin - labelSize.Width);
        labelX = Math.Clamp(labelX, margin, maxX);

        return new MagnifierPlacement(
            grid,
            new Rect(labelX, labelY, labelSize.Width, labelSize.Height));
    }
}
