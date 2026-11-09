using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering;
using Prtsc.Core.Editor;
using SkiaSharp;

namespace Prtsc.App.Controls;

/// <summary>
/// E-1 M9 取色模式的自绘放大镜（OverlayWindow 顶层，Control.Render 自绘，同 SelectionAdorner 风格）。
///
/// - 11×11 像素最近邻放大，每格 ≈12 设备像素（<c>12 / ScaleFactor</c> DIP，约 132 设备像素见方），
///   + 网格线 + 中心格高亮框；数据源是 overlay 手里的本屏物理像素位图，
///   采样走 <see cref="ColorSampler"/>（image 空间坐标，无 DPI 换算，越界钳制）。
/// - 浮签 HEX / RGB 两行跟随光标，贴边避让见 <see cref="MagnifierLayout"/>。
/// - 取色点击 → <see cref="ColorPicked"/>（复制/历史/描边色由 OverlayWindow 接）；
///   复制成功后 OverlayWindow 调 <see cref="ShowCopied"/> 进入短暂「已复制」态（输入冻结，纯视觉）。
/// - 只在取色模式 IsVisible=true 时参与渲染与命中，常态选框零开销。
///
/// 生命周期注意：<see cref="Source"/> 指向 overlay 抓帧位图但不拥有其所有权；上位者（OverlayWindow）
/// 保证位图在本窗口生命期内存活，本控件所有位图访问都先做隐身守卫 + 判空 + 零尺寸防御，
/// 杜绝渲染线程踩到已释放句柄（macOS 渲染线程 SIGSEGV 实锤过的坑）。
/// </summary>
public sealed class ColorMagnifier : Control, ICustomHitTest
{
    // ---- 视觉常量 ----
    private const double CellDevicePx = 12;      // 每格设备像素（brief 建议）
    private const double LabelTextPadding = 6;   // 浮签文字内边距
    private const double CenterHighlightPad = 1; // 中心格高亮框外扩

    // ---- 画笔 ----
    // 静态/每帧渲染资源一律 Immutable 变体（无线程仿亲）：同 SelectionAdorner 的说明，
    // 多 session/多 compositor 并发 AddRef 时 mutable Brush/Pen 会炸「calling thread cannot access」。
    private static readonly IBrush LabelBgBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xD9, 0x10, 0x10, 0x14));
    private static readonly IBrush LabelFgBrush  = Brushes.White;
    private static readonly IBrush CopiedBrush   = new ImmutableSolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly IPen GridPen         = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0xB4, 0, 0, 0)), 1);
    private static readonly IPen CenterPenOuter  = new ImmutablePen(Brushes.Black, 2);
    private static readonly IPen CenterPenInner  = new ImmutablePen(Brushes.White, 1);

    // ---- 状态 ----
    private SKBitmap? _source;
    private Point? _cursor;
    private string? _copiedText;

    /// <summary>本屏物理像素位图（overlay 抓帧结果；不拥有所有权，访问前判空）。</summary>
    public SKBitmap? Source
    {
        get => _source;
        set
        {
            if (ReferenceEquals(_source, value)) return;
            _source = value;
            InvalidateVisual();
        }
    }

    /// <summary>屏幕缩放系数（DIP → 物理像素）。0/负值按 1 处理。</summary>
    public double ScaleFactor { get; set; } = 1.0;

    /// <summary>光标位置（本控件 DIP 坐标）；null = 尚未移动过，不渲染。</summary>
    public Point? CursorPoint
    {
        get => _cursor;
        set
        {
            if (_cursor == value) return;
            _cursor = value;
            InvalidateVisual();
        }
    }

    /// <summary>「已复制」态：短暂冻结输入、浮签换成已复制文案。</summary>
    public bool CopiedFlash => _copiedText is not null;

    /// <summary>取色点击（左键）。</summary>
    public event EventHandler<SKColor>? ColorPicked;

    public ColorMagnifier()
    {
        IsHitTestVisible = true;
        Cursor = new Cursor(StandardCursorType.Arrow); // 与选框阶段的 Cross 区分模式
    }

    /// <summary>整个 Bounds 可命中：取色模式独占指针输入，拦截下层 SelectionAdorner（同其 ICustomHitTest 手法）。</summary>
    public bool HitTest(Point point) => IsVisible;

    /// <summary>进入「已复制」态（OverlayWindow 在剪贴板写入成功后调用）。</summary>
    public void ShowCopied(string text)
    {
        _copiedText = text;
        InvalidateVisual();
    }

    /// <summary>退出「已复制」态（ExitPickMode 时调用）。</summary>
    public void ClearCopied()
    {
        _copiedText = null;
        InvalidateVisual();
    }

    // ------------------------------------------------------------------------
    //  Pointer handling
    // ------------------------------------------------------------------------
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!IsVisible || CopiedFlash) return; // 隐身/已复制态不跟随
        CursorPoint = e.GetPosition(this);
        base.OnPointerMoved(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsVisible || CopiedFlash) return;
        var pt = e.GetCurrentPoint(this);
        if (!pt.Properties.IsLeftButtonPressed) return;
        if (_source is null || _source.Width <= 0 || _cursor is not { } cursor) return;

        var color = SampleAt(cursor);
        ColorPicked?.Invoke(this, color);
        e.Handled = true;
    }

    /// <summary>光标 DIP 坐标 → image 空间物理像素坐标采样（OverlayWindow 既有映射：round(dip × scale)，越界钳制）。</summary>
    public SKColor SampleAt(Point dip)
    {
        if (_source is null || _source.Width <= 0) return default;
        return ColorSampler.Sample(_source,
            (int)Math.Round(dip.X * EffectiveScale),
            (int)Math.Round(dip.Y * EffectiveScale));
    }

    private double EffectiveScale => ScaleFactor > 0 ? ScaleFactor : 1.0;

    /// <summary>每格 DIP 尺寸：12 设备像素 ÷ 缩放系数。</summary>
    private double CellDip => CellDevicePx / EffectiveScale;

    // ------------------------------------------------------------------------
    //  Render
    // ------------------------------------------------------------------------
    public override void Render(DrawingContext ctx)
    {
        // 隐身守卫放最前：渲染线程可能带着旧 dirty 标记进来（窗口关闭/回收期位图可能已释放）
        if (!IsVisible) return;
        if (_source is null || _source.Width <= 0 || _source.Height <= 0) return;
        if (_cursor is not { } cursor) return;

        var radius = ColorSampler.NeighborhoodRadius;
        var cell = CellDip;
        var side = (2 * radius + 1) * cell;
        var colors = ColorSampler.SampleNeighborhood(_source,
            (int)Math.Round(cursor.X * EffectiveScale),
            (int)Math.Round(cursor.Y * EffectiveScale),
            radius);
        if (colors.Length == 0) return;
        var centerColor = colors[radius * (2 * radius + 1) + radius];

        var surface = new Size(Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height));
        var labelSize = MeasureLabel(centerColor);
        var placement = MagnifierLayout.ComputePlacement(cursor, new Size(side, side), labelSize, surface);

        // 网格：最近邻色块
        var origin = placement.Grid.TopLeft;
        for (int row = 0; row < 2 * radius + 1; row++)
        {
            for (int col = 0; col < 2 * radius + 1; col++)
            {
                var c = colors[row * (2 * radius + 1) + col];
                var rect = new Rect(origin.X + col * cell, origin.Y + row * cell, cell, cell);
                ctx.DrawRectangle(CellBrush(c), null, rect);
            }
        }

        // 网格线（1 设备像素粗）
        var gridRect = placement.Grid;
        var halfLine = 1.0 / EffectiveScale / 2;
        for (int i = 1; i < 2 * radius + 1; i++)
        {
            var gx = origin.X + i * cell;
            var gy = origin.Y + i * cell;
            ctx.DrawLine(GridPen, new Point(gx - halfLine, gridRect.Y), new Point(gx - halfLine, gridRect.Bottom));
            ctx.DrawLine(GridPen, new Point(gridRect.X, gy - halfLine), new Point(gridRect.Right, gy - halfLine));
        }

        // 中心格高亮：黑 2px 外框 + 白 1px 内框（深浅底都可见）
        var center = new Rect(
            origin.X + radius * cell - CenterHighlightPad, origin.Y + radius * cell - CenterHighlightPad,
            cell + 2 * CenterHighlightPad, cell + 2 * CenterHighlightPad);
        ctx.DrawRectangle(null, CenterPenOuter, center);
        ctx.DrawRectangle(null, CenterPenInner, center);

        // 浮签
        DrawLabel(ctx, placement.Label, centerColor);
    }

    /// <summary>色块画刷：每帧新建但用 Immutable 变体（无线程仿亲，compositor 并发引用安全）。</summary>
    private static IBrush CellBrush(SKColor c)
        => new ImmutableSolidColorBrush(Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));

    private Size MeasureLabel(SKColor color)
    {
        if (CopiedFlash)
        {
            var t = MakeText(_copiedText!, CopiedBrush, 12);
            return new Size(t.Width + 2 * LabelTextPadding, t.Height + 2 * LabelTextPadding);
        }
        var hex = MakeText(ColorFormatter.ToHex(color), LabelFgBrush, 12);
        var rgb = MakeText(ColorFormatter.ToRgb(color), LabelFgBrush, 11);
        return new Size(
            Math.Max(hex.Width, rgb.Width) + 2 * LabelTextPadding,
            hex.Height + rgb.Height + 3 * LabelTextPadding - 2);
    }

    private void DrawLabel(DrawingContext ctx, Rect labelRect, SKColor color)
    {
        ctx.DrawRectangle(LabelBgBrush, null, labelRect);

        if (CopiedFlash)
        {
            var t = MakeText(_copiedText!, CopiedBrush, 12);
            ctx.DrawText(t, new Point(
                labelRect.X + LabelTextPadding,
                labelRect.Y + labelRect.Height / 2 - t.Height / 2));
            return;
        }

        var hex = MakeText(ColorFormatter.ToHex(color), LabelFgBrush, 12);
        var rgb = MakeText(ColorFormatter.ToRgb(color), LabelFgBrush, 11);
        ctx.DrawText(hex, new Point(labelRect.X + LabelTextPadding, labelRect.Y + LabelTextPadding));
        ctx.DrawText(rgb, new Point(
            labelRect.X + LabelTextPadding,
            labelRect.Y + LabelTextPadding + hex.Height + LabelTextPadding - 2));
    }

    /// <summary>浮签文字（Typeface 与 SelectionAdorner 尺寸标签一致）。</summary>
    private static FormattedText MakeText(string text, IBrush brush, double size)
        => new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush);
}
