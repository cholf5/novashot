using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Prtsc.App.Controls;

/// <summary>
/// Flameshot 风格的选框交互控件。
/// - 无选框：全区暗化，拖拽即创建
/// - 有选框：4 角 + 4 边中点 handles，边线可拖动，内部可移动
/// - 光标随命中区自动切换
/// - 坐标一律 DIP，Confirm 时由外部换算物理像素
///
/// 交互纯几何逻辑都委派给 <see cref="SelectionGeometry"/> 便于单元测试。
///
/// M2-1：拖拽创建 / 8 handles / 边线拖动 / 内部移动 / 光标 / clamp
/// M2-2：键盘微调 / Ctrl 锁比例 / Shift 中心对称
/// M2-4：GeometrySettled 400ms 去抖
/// </summary>
public sealed class SelectionAdorner : Control
{
    // ---- 视觉常量（DIP）----
    private const double HandleVisual = 8;
    private const double HandleHitPad = 4;   // 命中盒相对视觉扩大量（每边）
    private const double EdgeHitSlop  = 6;   // 边线拖动的允许偏离
    private const double MinSize      = 4;   // 选框最小边

    // ---- 画笔 ----
    private static readonly IBrush DimBrush        = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
    private static readonly IBrush HandleFillBrush = Brushes.White;
    private static readonly IPen   HandleStrokePen = new Pen(Brushes.Black, 1);
    private static readonly IPen   SelectionPen    = new Pen(Brushes.White, 1);
    private static readonly IBrush LabelBgBrush    = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0));
    private static readonly IBrush LabelFgBrush    = Brushes.White;

    // ---- 事件 ----
    public event EventHandler? SelectionChanged;
    /// <summary>双击 / 外部触发确认；overlay 收到后决定 Confirm。</summary>
    public event EventHandler? CommitRequested;
    /// <summary>
    /// 选框静止 400ms 后触发（Flameshot 的 geometrySettled）；后续用来重新定位悬浮工具环。
    /// </summary>
    public event EventHandler? GeometrySettled;

    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(400);
    private readonly DispatcherTimer _settleTimer;

    // ---- 状态 ----
    private Rect? _selection;
    public Rect? SelectionRect
    {
        get => _selection;
        set
        {
            if (Nullable.Equals(_selection, value)) return;
            _selection = value;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            KickSettleTimer();
            InvalidateVisual();
        }
    }

    private DragMode _mode = DragMode.None;
    private SelectionGeometry.Grip _activeGrip;
    private Point _dragStart;
    private Rect  _dragStartRect;

    private enum DragMode { None, Creating, Moving, Resizing }

    public SelectionAdorner()
    {
        Focusable = true;
        IsHitTestVisible = true;
        Cursor = new Cursor(StandardCursorType.Cross);

        _settleTimer = new DispatcherTimer { Interval = SettleDelay };
        _settleTimer.Tick += (_, _) =>
        {
            _settleTimer.Stop();
            if (_selection is not null) GeometrySettled?.Invoke(this, EventArgs.Empty);
        };
    }

    private void KickSettleTimer()
    {
        _settleTimer.Stop();
        if (_selection is null) return;
        _settleTimer.Start();
    }

    // ------------------------------------------------------------------------
    //  Render
    // ------------------------------------------------------------------------
    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;

        if (_selection is not { } sel)
        {
            ctx.DrawRectangle(DimBrush, null, new Rect(0, 0, w, h));
            return;
        }

        sel = SelectionGeometry.ClampAndNormalize(sel, w, h);

        // 环绕暗化——四条外围矩形，避开选框
        ctx.DrawRectangle(DimBrush, null, new Rect(0, 0, w, sel.Y));
        ctx.DrawRectangle(DimBrush, null, new Rect(0, sel.Bottom, w, Math.Max(0, h - sel.Bottom)));
        ctx.DrawRectangle(DimBrush, null, new Rect(0, sel.Y, sel.X, sel.Height));
        ctx.DrawRectangle(DimBrush, null, new Rect(sel.Right, sel.Y, Math.Max(0, w - sel.Right), sel.Height));

        // 选框描边
        ctx.DrawRectangle(null, SelectionPen, sel);

        // 8 handles
        foreach (var c in EnumerateHandleCenters(sel))
        {
            var r = new Rect(c.X - HandleVisual / 2, c.Y - HandleVisual / 2, HandleVisual, HandleVisual);
            ctx.DrawRectangle(HandleFillBrush, HandleStrokePen, r);
        }

        // 尺寸标签
        var label = new FormattedText(
            $"{(int)Math.Round(sel.Width)} × {(int)Math.Round(sel.Height)}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,
            LabelFgBrush);
        var labelSize = new Size(label.Width + 10, label.Height + 4);
        var labelY = sel.Y - labelSize.Height - 4;
        if (labelY < 0) labelY = sel.Y + 4; // 顶到上边就贴到选框内侧
        var labelRect = new Rect(new Point(sel.X, labelY), labelSize);
        ctx.DrawRectangle(LabelBgBrush, null, labelRect);
        ctx.DrawText(label, new Point(labelRect.X + 5, labelRect.Y + 2));
    }

    private static System.Collections.Generic.IEnumerable<Point> EnumerateHandleCenters(Rect r)
    {
        yield return new Point(r.X, r.Y);
        yield return new Point(r.X + r.Width / 2, r.Y);
        yield return new Point(r.Right, r.Y);
        yield return new Point(r.Right, r.Y + r.Height / 2);
        yield return new Point(r.Right, r.Bottom);
        yield return new Point(r.X + r.Width / 2, r.Bottom);
        yield return new Point(r.X, r.Bottom);
        yield return new Point(r.X, r.Y + r.Height / 2);
    }

    // ------------------------------------------------------------------------
    //  Pointer handling
    // ------------------------------------------------------------------------
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);

        if (_mode == DragMode.None)
        {
            Cursor = new Cursor(CursorFor(HitTest(p)));
            base.OnPointerMoved(e);
            return;
        }

        var newSel = ComputeDrag(p, e.KeyModifiers);
        SelectionRect = newSel;
        base.OnPointerMoved(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_selection is not { } sel) return;

        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10.0 : 1.0;
        var dx = 0.0;
        var dy = 0.0;
        switch (e.Key)
        {
            case Key.Left:  dx = -step; break;
            case Key.Right: dx = +step; break;
            case Key.Up:    dy = -step; break;
            case Key.Down:  dy = +step; break;
            default: return;
        }

        var w = Bounds.Width;
        var h = Bounds.Height;

        // Ctrl+方向：调整右下角（缩放）；否则平移整框
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var nw = Math.Clamp(sel.Width + dx, MinSize, Math.Max(MinSize, w - sel.X));
            var nh = Math.Clamp(sel.Height + dy, MinSize, Math.Max(MinSize, h - sel.Y));
            SelectionRect = new Rect(sel.X, sel.Y, nw, nh);
        }
        else
        {
            var nx = Math.Clamp(sel.X + dx, 0, Math.Max(0, w - sel.Width));
            var ny = Math.Clamp(sel.Y + dy, 0, Math.Max(0, h - sel.Height));
            SelectionRect = new Rect(nx, ny, sel.Width, sel.Height);
        }
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);
        if (!pt.Properties.IsLeftButtonPressed) return;

        var p = pt.Position;
        Focus();

        if (e.ClickCount >= 2 && _selection is { } dc && dc.Contains(p))
        {
            CommitRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        var grip = HitTest(p);

        if (grip == SelectionGeometry.Grip.Outside || _selection is null)
        {
            _mode = DragMode.Creating;
            _activeGrip = SelectionGeometry.Grip.SE;
            _dragStart = p;
            _dragStartRect = new Rect(p, new Size(0, 0));
            SelectionRect = _dragStartRect;
        }
        else if (grip == SelectionGeometry.Grip.Inside)
        {
            _mode = DragMode.Moving;
            _activeGrip = SelectionGeometry.Grip.Inside;
            _dragStart = p;
            _dragStartRect = _selection.Value;
        }
        else
        {
            _mode = DragMode.Resizing;
            _activeGrip = grip;
            _dragStart = p;
            _dragStartRect = _selection.Value;
        }

        Cursor = new Cursor(CursorFor(_activeGrip));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_mode == DragMode.None) return;

        // 太小的框视为无效，清空
        if (_selection is { } s && (s.Width < MinSize || s.Height < MinSize))
        {
            SelectionRect = null;
        }

        _mode = DragMode.None;
        e.Pointer.Capture(null);
        Cursor = new Cursor(_selection is null
            ? StandardCursorType.Cross
            : CursorFor(HitTest(e.GetPosition(this))));
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _mode = DragMode.None;
    }

    // ------------------------------------------------------------------------
    //  Hit-testing wrappers（委派到 SelectionGeometry）
    // ------------------------------------------------------------------------
    private SelectionGeometry.Grip HitTest(Point p) =>
        SelectionGeometry.HitTest(_selection, p, HandleVisual / 2 + HandleHitPad, EdgeHitSlop);

    private static StandardCursorType CursorFor(SelectionGeometry.Grip g) => g switch
    {
        SelectionGeometry.Grip.NW or SelectionGeometry.Grip.SE => StandardCursorType.TopLeftCorner,   // 主对角
        SelectionGeometry.Grip.NE or SelectionGeometry.Grip.SW => StandardCursorType.TopRightCorner,  // 副对角
        SelectionGeometry.Grip.N  or SelectionGeometry.Grip.S  => StandardCursorType.SizeNorthSouth,
        SelectionGeometry.Grip.E  or SelectionGeometry.Grip.W  => StandardCursorType.SizeWestEast,
        SelectionGeometry.Grip.Inside                          => StandardCursorType.SizeAll,
        SelectionGeometry.Grip.Outside                         => StandardCursorType.Cross,
        _                                                      => StandardCursorType.Arrow,
    };

    // ------------------------------------------------------------------------
    //  Drag computation
    // ------------------------------------------------------------------------
    private Rect ComputeDrag(Point p, KeyModifiers mods)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        var s = _dragStartRect;

        // 平移直接 clamp 后返回，不参与 Ctrl/Shift
        if (_activeGrip == SelectionGeometry.Grip.Inside)
        {
            var nx = Math.Clamp(s.X + dx, 0, Math.Max(0, w - s.Width));
            var ny = Math.Clamp(s.Y + dy, 0, Math.Max(0, h - s.Height));
            return new Rect(nx, ny, s.Width, s.Height);
        }

        var r = SelectionGeometry.ApplyDrag(s, dx, dy, _activeGrip);

        if (mods.HasFlag(KeyModifiers.Control))
            r = SelectionGeometry.ApplyAspectLock(r, s, _activeGrip);

        if (mods.HasFlag(KeyModifiers.Shift))
            r = SelectionGeometry.ApplyCenterSymmetric(r, s, _activeGrip);

        return SelectionGeometry.ClampAndNormalize(r, w, h);
    }

    // ------------------------------------------------------------------------
    //  Public helpers（供 OverlayWindow 用）
    // ------------------------------------------------------------------------
    public void ClearSelection() => SelectionRect = null;
}
