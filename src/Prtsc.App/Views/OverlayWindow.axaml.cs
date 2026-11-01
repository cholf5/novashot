using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Prtsc.App.Controls;
using Prtsc.App.Utils;
using SkiaSharp;
using PixelRect = Prtsc.Core.Common.PixelRect;
using DisplayInfo = Prtsc.Core.Common.DisplayInfo;

namespace Prtsc.App.Views;

/// <summary>
/// 单显示器 overlay。占满目标显示器物理边界，显示预抓桌面位图 + <see cref="SelectionAdorner"/>。
///
/// M2-1：拖拽 / 8 handles / 边线 / 移动 / 光标 / clamp
/// M2-2：键盘微调 / Ctrl 锁比例 / Shift 中心对称
/// M2-3a：多屏 N 实例（外部 MainWindow 组织）
/// M2-4：GeometrySettled 去抖
/// M2-5：Enter/双击 → 提交（DIP→物理像素）
/// M3-1：FloatingToolRing 悬浮工具环，跟随选框
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly DisplayInfo _display;
    private SelectionAdorner? _adorner;
    private FloatingToolRing? _ring;

    /// <summary>Confirmed 事件的第二参数是虚拟桌面物理像素坐标下的 region。</summary>
    public event EventHandler<PixelRect>? Confirmed;
    public event EventHandler? Cancelled;

    public OverlayWindow() : this(null!, default!) { }

    public OverlayWindow(SKBitmap desktopBitmap, DisplayInfo display)
    {
        _display = display;

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

        if (_adorner != null)
        {
            _adorner.CommitRequested += (_, _) => TryConfirmSelection();
            _adorner.SelectionChanged += (_, _) => UpdateRingPosition();
            _adorner.GeometrySettled += (_, _) => UpdateRingPosition();
        }
        if (_ring != null)
        {
            _ring.ConfirmRequested += (_, _) => TryConfirmSelection();
            _ring.CancelRequested += (_, _) => Cancel();
        }

        Opened += (_, _) =>
        {
#if MAC_PLATFORM
            // AppKit 会把无边框窗口钳进工作区（探针实证：请求 (0,0,2560,1440) 落地 (0,96,2560,1314)），
            // Show 后用原生 setFrame: 铺满整块显示器 + 提层盖住菜单栏/Dock。
            Prtsc.Platform.Mac.Windowing.MacOverlayChrome.Apply(
                TryGetPlatformHandle()?.Handle ?? 0, _display);
#endif
            _adorner?.Focus();
            Focus();
            UpdateRingPosition();
        };
#if MAC_PLATFORM
        // 激活周期里 AppKit 可能再次钳制，Activated 时幂等重铺
        Activated += (_, _) => Prtsc.Platform.Mac.Windowing.MacOverlayChrome.Apply(
            TryGetPlatformHandle()?.Handle ?? 0, _display);
#endif
        KeyDown += OnKeyDown;
        // 多屏时 overlay 间切换会触发 Deactivated；不再用它取消。用户只能通过 Esc/工具环取消。
    }

    /// <summary>基于当前选框重新定位/显隐悬浮工具环。</summary>
    private void UpdateRingPosition()
    {
        if (_ring is null || _adorner is null) return;

        var sel = _adorner.SelectionRect;
        if (sel is null || sel.Value.Width < 4 || sel.Value.Height < 4)
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
                if (_adorner?.SelectionRect is not null)
                {
                    _adorner.ClearSelection();
                    e.Handled = true;
                    return;
                }
                Cancel();
                e.Handled = true;
                break;

            case Key.Enter:
                TryConfirmSelection();
                e.Handled = true;
                break;
        }
    }

    private void TryConfirmSelection()
    {
        var sel = _adorner?.SelectionRect;
        if (sel is { } dipRect && dipRect.Width >= 1 && dipRect.Height >= 1)
        {
            var phys = DipToPhysical(dipRect, _display);
            Confirm(phys);
        }
        else
        {
            Confirm(_display.Bounds);
        }
    }

    private static PixelRect DipToPhysical(Rect dip, DisplayInfo d)
    {
        var s = d.ScaleFactor;
        var x = (int)Math.Round(d.Bounds.X + dip.X * s);
        var y = (int)Math.Round(d.Bounds.Y + dip.Y * s);
        var w = (int)Math.Round(dip.Width * s);
        var h = (int)Math.Round(dip.Height * s);
        if (w < 1) w = 1;
        if (h < 1) h = 1;
        return new PixelRect(x, y, w, h);
    }

    private void Confirm(PixelRect region)
    {
        Confirmed?.Invoke(this, region);
        Close();
    }

    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
