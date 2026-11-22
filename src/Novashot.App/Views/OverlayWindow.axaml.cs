using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Novashot.App.Controls;
using Novashot.App.Localization;
using Novashot.App.Utils;
using Novashot.Core.Clipboard;
using Novashot.Core.Editor;
using Novashot.Core.Windows;
using SkiaSharp;
using PixelRect = Novashot.Core.Common.PixelRect;
using DisplayInfo = Novashot.Core.Common.DisplayInfo;

namespace Novashot.App.Views;

/// <summary>
/// 单显示器 overlay。占满目标显示器物理边界，显示预抓桌面位图 + <see cref="SelectionAdorner"/>。
///
/// M2-1：拖拽 / 8 handles / 边线 / 移动 / 光标 / clamp
/// M2-2：键盘微调 / Ctrl 锁比例 / Shift 中心对称
/// M2-3a：多屏 N 实例（外部 MainWindow 组织）
/// M2-3b：B-3 跨屏——<see cref="SelectionSession"/> 承载权威选框（物理像素 + 活动帧 DIP），
///         本窗口把指针/键盘事件转发进会话、把会话广播映射回本屏 adorner；确认输出物理 region
/// M2-4：GeometrySettled 去抖
/// M2-5：Enter/双击 → 提交（物理像素 region；无选框回退本屏整屏）
/// M3-1：FloatingToolRing 悬浮工具环，跟随选框（跨屏时只显示在选框中心所在屏）
/// E-1 M9：C 进/出取色模式（<see cref="ColorMagnifier"/>）；点击 = 复制 HEX + 记会话历史 +
///         抛 <see cref="StrokePicked"/>（外层经 StylePanel 路径写入描边色）+ 回选框
/// 统一吸附交互（2026-11-23 设计，第二轮意图收敛）：未拖动时悬停顶层窗口自动高亮
///         （SnapHitTester 命中，预览写回 adorner 渲染），空白桌面 = 该屏全屏吸附预览；
///         原地释放 = 确认悬停窗口（OverlayCaptureResult.WindowCapture）或该屏全屏
///         （Region(display bounds)），拖拽 = 既有区域路径。吸附只在无选框状态激活；
///         多屏 N 实例共享同一份窗口缓存。
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly DisplayInfo _display;
    private readonly IClipboardService? _clipboard;
    private readonly ColorHistory? _colorHistory;
    private readonly SelectionSession _session;
    private readonly IReadOnlyList<WindowInfo> _snapWindows;
    private readonly SnapGesture _snapGesture;
    private SelectionAdorner? _adorner;
    private FloatingToolRing? _ring;
    private ColorMagnifier? _magnifier;
    private Border? _defaultHint;
    private Border? _pickerHint;
    private DispatcherTimer? _copiedTimer;
    private WindowInfo? _hoverWindow;    // 当前悬停命中（Enter 确认用；拖拽/捕获期间冻结不刷新）
    private WindowInfo? _pressedWindow;  // 按下瞬间冻结的吸附候选（点击确认用，避免手抖滑出边缘误判）

    /// <summary>确认产出：拖拽选框 = Region（虚拟桌面物理像素）；吸附点击 = WindowCapture。</summary>
    public event EventHandler<OverlayCaptureResult>? Confirmed;
    public event EventHandler? Cancelled;

    /// <summary>E-1 M9：取色点击（含复制/历史记账之后的收尾通知）。外层把该色挂到编辑器描边色。</summary>
    public event EventHandler<SKColor>? StrokePicked;

    /// <summary>取色模式开关（C 键切换；显隐以放大镜 IsVisible 为准）。</summary>
    private bool _picking;

    public OverlayWindow() : this(null!, default!) { }

    public OverlayWindow(SKBitmap desktopBitmap, DisplayInfo display,
        IClipboardService? clipboard = null, ColorHistory? colorHistory = null,
        SelectionSession? session = null, IReadOnlyList<WindowInfo>? snapWindows = null)
    {
        _display = display;
        _clipboard = clipboard;
        _colorHistory = colorHistory;
        // B-3：外部（MainWindow）注入跨屏会话；独立构造（测试/单窗）时退化为单屏会话，行为与旧本地数学等价
        _session = session ?? new SelectionSession(new[] { display });
        // 统一吸附：窗口缓存由 MainWindow 一次枚举后多屏共享；缺省空表 = 不吸附（测试/单窗不受影响）。
        // 手势阈值物理像素 = MinSize(DIP) × scale，与 Session「<MinSize 清框」门限严格分界：
        // 拖出有效选框必已越阈（区域），没成框的释放仍是点击（窗口/无动作），两类结局互斥。
        _snapWindows = snapWindows ?? Array.Empty<WindowInfo>();
        _snapGesture = new SnapGesture(SelectionGeometry.MinSize * display.ScaleFactor);

        InitializeComponent();

        Position = new PixelPoint(display.Bounds.X, display.Bounds.Y);
        Width = display.Bounds.Width / display.ScaleFactor;
        Height = display.Bounds.Height / display.ScaleFactor;

        if (desktopBitmap != null)
        {
            var img = this.FindControl<Image>("BackgroundImage");
            if (img != null)
                img.Source = SkiaAvaloniaConvert.ToAvaloniaBitmap(desktopBitmap);
        }

        _adorner = this.FindControl<SelectionAdorner>("Adorner");
        _ring = this.FindControl<FloatingToolRing>("ToolRing");
        _magnifier = this.FindControl<ColorMagnifier>("ColorMagnifier");
        _defaultHint = this.FindControl<Border>("DefaultHint");
        _pickerHint = this.FindControl<Border>("PickerHint");

        if (_adorner != null)
        {
            _adorner.CommitRequested += (_, _) => TryConfirmSelection();
            _adorner.SelectionChanged += (_, _) => UpdateRingPosition();
            _adorner.GeometrySettled += (_, _) => UpdateRingPosition();
            _adorner.Session = _session;
            _adorner.DisplayIndex = display.Index;
            // 统一吸附接线：adorner 只转发原始指针流，命中/手势/裁决都在本窗口
            _adorner.SnapPointerPressed += OnSnapPressed;
            _adorner.SnapPointerMoved += OnSnapMoved;
            _adorner.SnapPointerReleased += OnSnapReleased;
            _adorner.SnapPointerExited += OnSnapExited;
            _adorner.SnapCaptureLost += OnSnapCaptureLost;
        }
        if (_ring != null)
        {
            _ring.ConfirmRequested += (_, _) => TryConfirmSelection();
            _ring.CancelRequested += (_, _) => Cancel();
        }
        if (_magnifier != null)
        {
            _magnifier.Source = desktopBitmap;
            _magnifier.ScaleFactor = display.ScaleFactor;
            _magnifier.ColorPicked += OnPickColor;
        }

        _copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _copiedTimer.Tick += (_, _) => ExitPickMode();

        // B-3：会话广播 → 本屏 adorner（映射后的矩形可越界，渲染按切片/裁剪规则出图）
        _session.Changed += ApplySessionSelection;

        Opened += (_, _) =>
        {
#if MAC_PLATFORM
            // AppKit 会把无边框窗口钳进工作区（探针实证：请求 (0,0,2560,1440) 落地 (0,96,2560,1314)），
            // Show 后用原生 setFrame: 铺满整块显示器 + 提层盖住菜单栏/Dock。
            Novashot.Platform.Mac.Windowing.MacOverlayChrome.Apply(
                TryGetPlatformHandle()?.Handle ?? 0, _display);
#endif
            _adorner?.Focus();
            Focus();
            UpdateRingPosition();
        };
#if MAC_PLATFORM
        // 激活周期里 AppKit 可能再次钳制，Activated 时幂等重铺
        Activated += (_, _) => Novashot.Platform.Mac.Windowing.MacOverlayChrome.Apply(
            TryGetPlatformHandle()?.Handle ?? 0, _display);
#endif
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        // 多屏时 overlay 间切换会触发 Deactivated；不再用它取消。用户只能通过 Esc/工具环取消。

        // E-1 M9：关闭时立刻摘掉放大镜对抓帧位图的引用——上位者（MainWindow）确认/取消后马上
        // Dispose 抓帧位图，渲染线程若带着旧 dirty 标记先踩进来就是 SIGSEGV（headless 实测复现过）。
        Closed += (_, _) =>
        {
            _copiedTimer?.Stop();
            _session.Changed -= ApplySessionSelection;
            if (_magnifier != null) { _magnifier.IsVisible = false; _magnifier.Source = null; }
        };
    }

    /// <summary>B-3：会话权威选框变化 → 写回本屏 adorner（映射后的矩形）并刷新工具环归属。</summary>
    private void ApplySessionSelection(object? sender, EventArgs e)
    {
        if (_adorner != null)
            _adorner.SelectionRect = _session.SelectionInFrame(_display.Index);
        // 统一吸附：出现权威选框即退出吸附预览（吸附只在无选框状态激活）
        if (_session.ConfirmRegion() is not null)
            ClearSnapPreview();
        UpdateRingPosition();
    }

    /// <summary>基于当前选框重新定位/显隐悬浮工具环。</summary>
    private void UpdateRingPosition()
    {
        if (_ring is null || _adorner is null) return;

        var sel = _adorner.SelectionRect;
        // B-3：跨屏时环只挂在选框中心所在屏；单屏会话恒等本屏，行为不变。
        // 最小尺寸判据用会话物理尺寸而非本屏映射宽（B-3 review Minor-1）：映射宽随宿主屏
        // scale 缩放，高缩放屏上 4–7 物理像素的合法窄选框映射后 <4 会误失环；
        // 释放后 <4 DIP 已被清空，物理门与旧单屏行为在实际可达态上等价
        var phys = _session.ConfirmRegion();
        if (sel is null || phys is null || phys.Value.Width < 4 || phys.Value.Height < 4
            || _session.RingDisplayIndex != _display.Index)
        {
            _ring.IsVisible = false;
            return;
        }

        _ring.IsVisible = true;

        // 测量一次拿到真实尺寸
        _ring.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var ringSize = _ring.DesiredSize;
        var overlaySize = new Size(Width, Height);

        var pos = SelectionGeometry.ComputeToolRingPosition(sel.Value, ringSize, overlaySize, gap: 8);
        Canvas.SetLeft(_ring, pos.X);
        Canvas.SetTop(_ring, pos.Y);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                ExitPickMode(); // 取色模式下 Esc 先退模式；「清除选框 / 取消整个捕获」语义维持既有
                if (_adorner?.SelectionRect is not null)
                {
                    _session.Clear(); // B-3：清的是会话权威态——跨屏时所有 overlay 的选框一并消失
                    e.Handled = true;
                    return;
                }
                Cancel();
                e.Handled = true;
                break;

            case Key.Enter:
                ExitPickMode(); // Enter 语义维持既有（确认选框），顺手退出取色态
                TryConfirmSelection();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// E-1 M9 + review I-1：取色模式开关绑 KeyUp 而非 KeyDown。
    /// Avalonia 12 的 KeyEventArgs 无 IsRepeat/Timestamp（反编译 12.1.0 核实），平台自动重复
    /// （长按 C，~30Hz）只连发 KeyDown——绑 KeyUp 后每次物理释放恰好翻转一次，长按不闪屏。
    /// </summary>
    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.C) return;
        TogglePickMode(); // C 空闲（已核实 OverlayWindow 现只绑 Esc/Enter）→ 取色模式开关
        e.Handled = true;
    }

    // ------------------------------------------------------------------------
    //  统一吸附交互：悬停命中 → 预览；点击/Enter → 窗口确认；拖拽 → 既有区域路径
    //  吸附只在无选框状态激活；手势阈值已与 Session MinSize 清框门限对齐（见构造函数）
    // ------------------------------------------------------------------------
    private void OnSnapPressed(object? sender, Point p)
    {
        if (_picking) return;
        if (_session.ConfirmRegion() is not null)
        {
            _snapGesture.Reset(); // 有选框：按压归选框操作（移动/缩放/重建），不进吸附手势
            return;
        }

        var phys = SelectionGeometry.LocalToPhysical(_display, p);
        _snapGesture.Press(phys.X, phys.Y);
        // 冻结按下瞬间的命中：释放时确认的是按下点所在窗口，不看释放点（手抖滑出边缘不误判）
        _pressedWindow = SnapHitTester.Hit(_snapWindows, phys.X, phys.Y);
    }

    private void OnSnapMoved(object? sender, Point p)
    {
        if (_picking) return;
        var phys = SelectionGeometry.LocalToPhysical(_display, p);

        if (_snapGesture.MarkDragIfBeyond(phys.X, phys.Y))
        {
            _pressedWindow = null; // 实质拖拽：候选作废，交还区域路径（Session 正在建框）
            ClearSnapPreview();
            return;
        }

        // 悬停刷新：只在无选框、未按下时（按下期间的预览是冻结候选，不跟指针走）。
        // 窗口命中 → 窗口预览；空白（未命中）→ 该屏全屏吸附预览（统一交互第二轮）
        if (_snapGesture.CurrentPhase == SnapGesture.Phase.Idle && _session.ConfirmRegion() is null)
        {
            _hoverWindow = SnapHitTester.Hit(_snapWindows, phys.X, phys.Y);
            if (_hoverWindow is { } hit) SetSnapPreview(hit);
            else SetDisplaySnapPreview();
        }
    }

    private void OnSnapReleased(object? sender, Point p)
    {
        if (_picking) return;
        var wasClick = _snapGesture.Release();
        var candidate = _pressedWindow;
        _pressedWindow = null;

        // 点击（未越阈）：有冻结候选 → 确认窗口；无（空白悬停）→ 该屏全屏。
        // 有选框的按压没进手势（wasClick=false），拖拽越阈候选已作废——结局互斥不变。
        if (!wasClick) return;
        if (candidate is not null) ConfirmWindow(candidate);
        else ConfirmDisplay();
    }

    /// <summary>空白悬停的确认产出：该屏整屏（与旧「Enter 无选框回退本屏整屏」同口径）。</summary>
    private void ConfirmDisplay()
    {
        _snapGesture.Reset();
        _hoverWindow = null;
        ClearSnapPreview();
        Confirm(new OverlayCaptureResult.Region(_display.Bounds));
    }

    private void OnSnapExited(object? sender, EventArgs e)
    {
        // 按下捕获期间离开本屏不算悬停离开（预览是冻结候选）；只清空闲态的悬停
        if (_snapGesture.CurrentPhase != SnapGesture.Phase.Idle) return;
        _hoverWindow = null;
        ClearSnapPreview();
    }

    private void OnSnapCaptureLost(object? sender, EventArgs e)
    {
        // Alt-tab 等导致捕获丢失不是点击：复位手势、丢弃冻结候选，不触发任何确认
        _snapGesture.Reset();
        _pressedWindow = null;
    }

    private void ClearSnapPreview()
    {
        if (_adorner is not null) _adorner.SnapPreviewRect = null;
    }

    /// <summary>窗口悬停命中 → adorner 预览（本屏帧映射，可越界；标签用物理像素尺寸=实际截图尺寸）。</summary>
    private void SetSnapPreview(WindowInfo target)
    {
        if (_adorner is null) return;
        // 帧映射走 SelectionGeometry 单源（与 SelectionSession 同一份公式，防两处漂移）
        var frame = SelectionGeometry.PhysicalToLocalDipRect(_display, target.Bounds);
        _adorner.SnapPreviewRect = new SelectionAdorner.SnapPreview(
            frame, $"{target.Bounds.Width} × {target.Bounds.Height}");
    }

    /// <summary>空白悬停 → 该屏全屏吸附预览（整屏边框 + 物理 W×H 标签，视觉语言与窗口预览一致）。</summary>
    private void SetDisplaySnapPreview()
    {
        if (_adorner is null) return;
        var full = SelectionGeometry.PhysicalToLocalDipRect(_display, _display.Bounds);
        _adorner.SnapPreviewRect = new SelectionAdorner.SnapPreview(
            full, $"{_display.Bounds.Width} × {_display.Bounds.Height}");
    }

    // ------------------------------------------------------------------------
    //  E-1 M9 取色模式
    // ------------------------------------------------------------------------
    private void TogglePickMode()
    {
        if (_picking) ExitPickMode();
        else EnterPickMode();
    }

    private void EnterPickMode()
    {
        if (_picking || _magnifier is null) return;
        _picking = true;

        // 统一吸附：取色模式独占指针，吸附预览与候选一并清掉，防残影误确认
        _snapGesture.Reset();
        _pressedWindow = null;
        _hoverWindow = null;
        ClearSnapPreview();

        _magnifier.IsVisible = true;
        if (_adorner != null) _adorner.IsHitTestVisible = false; // 放大镜层独占指针输入
        if (_ring != null) _ring.IsVisible = false;              // 工具环藏起，避免误点确认
        if (_defaultHint != null) _defaultHint.IsVisible = false;
        if (_pickerHint != null) _pickerHint.IsVisible = true;
    }

    private void ExitPickMode()
    {
        if (!_picking) return;
        _picking = false;

        _copiedTimer?.Stop();
        if (_magnifier != null)
        {
            _magnifier.IsVisible = false;
            _magnifier.CursorPoint = null;
            _magnifier.ClearCopied();
        }
        if (_adorner != null) _adorner.IsHitTestVisible = true;
        UpdateRingPosition(); // 恢复工具环显隐
        if (_defaultHint != null) _defaultHint.IsVisible = true;
        if (_pickerHint != null) _pickerHint.IsVisible = false;
    }

    /// <summary>
    /// 放大镜点击取色：复制 HEX（CF_UNICODETEXT）→ 记会话历史 → 抛 <see cref="StrokePicked"/>
    /// （外层经 StylePanel 既有路径写入编辑器描边色）→ 退出取色模式回选框（不确认选框）。
    /// 复制成功时给浮签 900ms「已复制」反馈后再退。
    /// </summary>
    private void OnPickColor(object? sender, SKColor color)
    {
        var copied = _clipboard?.TrySetText(ColorFormatter.ToHex(color)) == true;
        _colorHistory?.Add(color);
        StrokePicked?.Invoke(this, color);

        if (copied && _magnifier != null)
        {
            _magnifier.ShowCopied(Loc.T("Overlay.PickerCopied", ColorFormatter.ToHex(color)));
            _copiedTimer?.Stop();
            _copiedTimer?.Start();
        }
        else
        {
            ExitPickMode();
        }
    }

    private void TryConfirmSelection()
    {
        // B-3：权威选框在会话（物理像素）——有选框走区域，优先级最高
        if (_session.ConfirmRegion() is { } region)
        {
            Confirm(new OverlayCaptureResult.Region(region));
            return;
        }
        // 统一吸附：无选框时悬停命中优先（Enter 确认当前悬停窗口），其次整屏回退（既有语义）
        if (_hoverWindow is { } w)
        {
            ConfirmWindow(w);
            return;
        }
        Confirm(new OverlayCaptureResult.Region(_display.Bounds));
    }

    private void Confirm(OverlayCaptureResult result)
    {
        Confirmed?.Invoke(this, result);
        Close();
    }

    /// <summary>吸附确认窗口：清吸附态后抛 WindowCapture（像素获取/置前时序在 MainWindow）。</summary>
    private void ConfirmWindow(WindowInfo target)
    {
        _snapGesture.Reset();
        _pressedWindow = null;
        _hoverWindow = null;
        ClearSnapPreview();
        Confirm(new OverlayCaptureResult.WindowCapture(target));
    }

    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
