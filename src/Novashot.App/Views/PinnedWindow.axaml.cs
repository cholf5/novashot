using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Novashot.App.Utils;
using SkiaSharp;

namespace Novashot.App.Views;

/// <summary>
/// Pin 贴图窗（M7b，flameshot 招牌交互）：无边框置顶小窗显示截图。
/// 拖动移动、滚轮缩放（Ctrl+滚轮 = 粗调）、双击/Esc 关闭；鼠标悬停短暂显示操作提示。
/// 关闭不影响主编辑器。位图所有权归本窗体，Closed 时释放。
/// </summary>
public partial class PinnedWindow : Window
{
    private const double MinScale = 0.1;
    private const double MaxScale = 5.0;
    private const double BaseDpiScale = 96.0 / 72.0; // 像素尺寸 → DIP（96 DPI 基准；多屏混合 DPI 可后续再精化）

    private readonly SKBitmap _bitmap;
    private readonly Bitmap _avaloniaBitmap;
    private double _scale;
    private bool _dragging;
    private Point _dragStartCursor;
    private Point _dragStartWindow;
    private DispatcherTimer? _hintTimer;

    /// <summary>运行时加载器要求：无参公共构造（参数版本为真正入口）。</summary>
    public PinnedWindow() : this(new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Opaque))) { }

    public PinnedWindow(SKBitmap bitmap, PixelPoint? position = null, double initialScale = 1.0)
    {
        _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        _avaloniaBitmap = SkiaAvaloniaConvert.ToAvaloniaBitmap(bitmap);
        _scale = Math.Clamp(initialScale, MinScale, MaxScale);

        InitializeComponent();
        PinImage.Source = _avaloniaBitmap;
        PinImage.Width = bitmap.Width * _scale;
        PinImage.Height = bitmap.Height * _scale;

        Position = position ?? ComputeDefaultPosition();
        ApplySize();

        // 事件接线（AXAML 无 x:Name 事件，代码挂接更直观）
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheel;
        PointerEntered += (_, _) => ShowHintTemporarily();
        DoubleTapped += (_, _) => Close();
        Closed += (_, _) =>
        {
            _hintTimer?.Stop();
            _avaloniaBitmap.Dispose();
            _bitmap.Dispose();
        };
    }

    /// <summary>默认位置：主屏右上角，逐张向左下错开（由 <see cref="PinWindowStatics.NextOffset"/> 提供）。</summary>
    private PixelPoint ComputeDefaultPosition() =>
        new(40 + PinWindowStatics.NextOffset(), 40 + PinWindowStatics.NextOffset());

    private void ApplySize()
    {
        var width = Math.Max(1, _bitmap.Width * _scale);
        var height = Math.Max(1, _bitmap.Height * _scale);
        PinImage.Width = width;
        PinImage.Height = height;
        ClientSize = new Size(width, height);
    }

    // ---- 拖动 ----

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragging = true;
            _dragStartCursor = e.GetPosition(null);
            _dragStartWindow = Position.ToPoint(1.0);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        var delta = e.GetPosition(null) - _dragStartCursor;
        Position = PixelPoint.FromPoint(_dragStartWindow + delta, 1.0);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // ---- 缩放 ----

    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) return;
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 0.25 : 0.1;
        var factor = e.Delta.Y > 0 ? 1 + step : 1 - step;
        _scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        ApplySize();
        e.Handled = true;
    }

    // ---- 提示 ----

    private void ShowHintTemporarily()
    {
        HintBar.IsVisible = true;
        _hintTimer?.Stop();
        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _hintTimer.Tick += (_, _) =>
        {
            HintBar.IsVisible = false;
            _hintTimer.Stop();
        };
        _hintTimer.Start();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}

/// <summary>Pin 窗默认位置错开器：每张窗依次错开 28px，到 20 张后循环。</summary>
public static class PinWindowStatics
{
    private const int Step = 28;
    private const int Cycle = 20;
    private static int _offset;

    public static int NextOffset()
    {
        var value = _offset;
        _offset = (_offset + Step) % (Step * Cycle);
        return value;
    }
}
