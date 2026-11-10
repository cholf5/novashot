using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering;
using Avalonia.Threading;

namespace Prtsc.App.Controls;

/// <summary>
/// Flameshot 风格的选框交互控件。
/// - 无选框：全区暗化，拖拽即创建
/// - 有选框：4 角 + 4 边中点 handles，边线可拖动，内部可移动
/// - 光标随命中区自动切换
/// - 坐标一律 DIP，Confirm 时由外部换算物理像素
///
/// B-3 M2-3b：交互纯几何与权威选框状态移入 <see cref="SelectionSession"/>（跨屏共享，
/// 每屏 overlay 的本控件只持有「权威选框映射到本屏帧」的渲染矩形——可越出本屏界，
/// 越界段由窗口裁剪）。本控件职责收敛为：渲染 + 原始指针/键盘事件转发 + 光标反馈。
///
/// M2-2：键盘微调 / Ctrl 锁比例 / Shift 中心对称（数学在 SelectionGeometry/Session）
/// M2-4：GeometrySettled 400ms 去抖
/// </summary>
public sealed class SelectionAdorner : Control, ICustomHitTest
{
    // ---- 视觉常量（DIP）----
    private const double HandleVisual = 8;

    // ---- 画笔 ----
    // E-1 M9：静态渲染资源一律用 Immutable 变体——mutable Brush/Pen 是 AvaloniaObject（线程仿亲），
    // 多 headless session / 多窗口 compositor 并发 AddRef 时会踩「calling thread cannot access」
    //（ColorPickerOverlayTests headless 实测炸过，immutable 无线程仿亲）。
    private static readonly IBrush DimBrush        = new ImmutableSolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
    private static readonly IBrush HandleFillBrush = Brushes.White;
    private static readonly IPen   HandleStrokePen = new ImmutablePen(Brushes.Black, 1);
    private static readonly IPen   SelectionPen    = new ImmutablePen(Brushes.White, 1);
    private static readonly IBrush LabelBgBrush    = new ImmutableSolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0));
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
    // 渲染输入：权威选框映射到本屏帧的矩形（OverlayWindow 经 session 广播写入；可越界）。
    // 会话态不在本控件——指针/键盘事件转发给 Session。
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

    /// <summary>B-3：跨屏会话（OverlayWindow 装配；null 时本控件不响应指针/键盘）。</summary>
    public SelectionSession? Session { get; set; }

    /// <summary>本 overlay 对应的显示器 Index（= DisplayInfo.Index，会话按键寻屏）。</summary>
    public int DisplayIndex { get; set; }

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

    /// <summary>
    /// 整个 Bounds 可命中（对齐 Flameshot 全屏 CaptureWidget 一整层接输入的模式）。
    /// 默认按绘制几何命中时选框内部是透明空洞，内部拖拽（移动选框）会漏到下层背景。
    /// </summary>
    public bool HitTest(Point point) => true;

    // ------------------------------------------------------------------------
    //  Render
    // ------------------------------------------------------------------------
    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var view = new Rect(0, 0, w, h);

        if (_selection is not { } sel)
        {
            ctx.DrawRectangle(DimBrush, null, view);
            return;
        }

        // B-3：sel 是权威选框在本屏帧的映射，可整体越界（选框在另一屏）→ 本屏铺满暗化；
        // 相交时可见切片 vis = sel ∩ view，暗化 = 窗口内切片之外的区域。
        if (!sel.Intersects(view))
        {
            ctx.DrawRectangle(DimBrush, null, view);
            return;
        }
        var vis = SelectionGeometry.ClampAndNormalizeTo(sel, view);

        // 环绕暗化——四条外围矩形，避开可见切片
        ctx.DrawRectangle(DimBrush, null, new Rect(0, 0, w, vis.Y));
        ctx.DrawRectangle(DimBrush, null, new Rect(0, vis.Bottom, w, Math.Max(0, h - vis.Bottom)));
        ctx.DrawRectangle(DimBrush, null, new Rect(0, vis.Y, vis.X, vis.Height));
        ctx.DrawRectangle(DimBrush, null, new Rect(vis.Right, vis.Y, Math.Max(0, w - vis.Right), vis.Height));

        // 选框描边：画真实全局矩形，越界段由窗口裁剪（跨屏时邻屏补齐另外的段）
        ctx.DrawRectangle(null, SelectionPen, sel);

        // 8 handles：画真实全局角点，落在别的屏的手柄不在本屏出现
        foreach (var c in EnumerateHandleCenters(sel))
        {
            var r = new Rect(c.X - HandleVisual / 2, c.Y - HandleVisual / 2, HandleVisual, HandleVisual);
            ctx.DrawRectangle(HandleFillBrush, HandleStrokePen, r);
        }

        // 尺寸标签：数字用真实选框，锚点用可见切片（选框顶缘越出本屏时标签仍贴在屏内）
        var label = new FormattedText(
            $"{(int)Math.Round(sel.Width)} × {(int)Math.Round(sel.Height)}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,
            LabelFgBrush);
        var labelSize = new Size(label.Width + 10, label.Height + 4);
        var labelY = vis.Y - labelSize.Height - 4;
        if (labelY < 0) labelY = vis.Y + 4; // 顶到上边就贴到切片内侧
        var labelRect = new Rect(new Point(vis.X, labelY), labelSize);
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
    //  Pointer handling（事件转发给 Session；权威态在会话，广播经 OverlayWindow 写回 _selection）
    // ------------------------------------------------------------------------
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);

        if (Session is { } s)
        {
            if (s.HasActiveDrag) s.Drag(DisplayIndex, p, e.KeyModifiers);
            else Cursor = new Cursor(CursorFor(HitTestGrip(p)));
            base.OnPointerMoved(e);
            return;
        }
        base.OnPointerMoved(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Session is not { } s || _selection is null) return;
        if (s.Nudge(e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Control), e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
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

        Session?.Press(DisplayIndex, p, e.KeyModifiers);
        Cursor = new Cursor(CursorFor(Session?.ActiveGrip ?? HitTestGrip(p)));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        Session?.Release(DisplayIndex);
        e.Pointer.Capture(null);
        Cursor = new Cursor(_selection is null
            ? StandardCursorType.Cross
            : CursorFor(HitTestGrip(e.GetPosition(this))));
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        // 捕获丢失（如窗口被外部关闭）等价释放：会话态必须退出拖拽，否则后续 Press 被永久忽略。
        // 正常释放后 Capture(null) 也会触发本事件——Release 幂等（_mode==None 直接返回）。
        Session?.Release(DisplayIndex);
    }

    // ------------------------------------------------------------------------
    //  Hit-testing wrappers（委派到 SelectionGeometry）
    // ------------------------------------------------------------------------
    private SelectionGeometry.Grip HitTestGrip(Point p) =>
        SelectionGeometry.HitTest(_selection, p, SelectionGeometry.GripHitHalfPad, SelectionGeometry.EdgeHitSlop);

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
}
