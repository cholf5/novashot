using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Prtsc.App.Assets;
using Prtsc.App.ViewModels;
using Prtsc.Core.Capture;
using Prtsc.Core.Common;
using Prtsc.Core.Windows;

namespace Prtsc.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly ICaptureService _capture;
    private readonly IWindowService _windowService;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow() : this(
        AppServices.GetRequiredService<MainWindowViewModel>(),
        AppServices.GetRequiredService<ICaptureService>(),
        AppServices.GetRequiredService<IWindowService>(),
        AppServices.GetRequiredService<ILogger<MainWindow>>())
    { }

    public MainWindow(MainWindowViewModel vm, ICaptureService capture, IWindowService windowService, ILogger<MainWindow> logger)
    {
        _vm = vm;
        _capture = capture;
        _windowService = windowService;
        _logger = logger;
        DataContext = vm;

        InitializeComponent();

        Icon = AppIconFactory.CreatePlaceholderWindowIcon();
    }

    private async void OnCaptureFullscreenClick(object? sender, RoutedEventArgs e)
    {
        await CaptureFullscreenAsync();
    }

    private async void OnCaptureRegionClick(object? sender, RoutedEventArgs e)
    {
        await CaptureRegionAsync();
    }

    private void OnCaptureWindowClick(object? sender, RoutedEventArgs e)
    {
        _ = CaptureWindowAsync();
    }

    private void OnMinimizeToTrayClick(object? sender, RoutedEventArgs e) => Hide();

    private void OnEditorBackClick(object? sender, RoutedEventArgs e) => _vm.ReturnToCapture();

    /// <summary>
    /// Public API：托盘/热键触发全屏抓取。主窗口先隐藏，等 200ms 让 shadow 消失，再抓帧。
    /// </summary>
    public async Task CaptureFullscreenAsync()
    {
        _logger.LogInformation("Capture Fullscreen requested");
        var wasVisible = IsVisible;
        if (wasVisible)
        {
            Hide();
            await Task.Delay(200);
        }
        try
        {
            var img = _capture.CaptureFullscreen();
            _vm.EnterEditor(img);
            Show();
            Activate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fullscreen capture failed");
            _vm.StatusText = "Capture failed: " + ex.Message;
            if (wasVisible) Show();
        }
    }

    /// <summary>
    /// Region 捕获：每个物理显示器一个 <see cref="OverlayWindow"/>；
    /// 先抓帧再显示 overlay，避免 overlay 自身被拍进画面。
    /// 首个 Confirmed/Cancelled 决定整会话结果，其余 overlay 一并关闭。
    /// M2-3a：每个 overlay 只处理本屏内的选框；跨屏选择留待 M2-3b（共享坐标器）。
    /// </summary>
    public async Task CaptureRegionAsync()
    {
        _logger.LogInformation("Capture Region requested");
        var wasVisible = IsVisible;
        if (wasVisible)
        {
            Hide();
            await Task.Delay(200);
        }

        var overlays = new List<OverlayWindow>();
        var displayShots = new List<CapturedImage>();

        try
        {
            var displays = _capture.EnumerateDisplays();
            _logger.LogInformation("Opening region overlays across {N} display(s)", displays.Count);

            // 先抓全部显示器（overlay 尚未显示，不会被拍到）
            foreach (var d in displays)
                displayShots.Add(_capture.CaptureDisplay(d.Index));

            var completed = false;

            void CloseAll()
            {
                foreach (var o in overlays)
                {
                    try { o.Close(); } catch { /* already closed */ }
                }
            }

            void OnAnyConfirmed(PixelRect region)
            {
                if (completed) return;
                completed = true;
                Dispatcher.UIThread.Post(() =>
                {
                    CloseAll();
                    try
                    {
                        var img = _capture.CaptureRegion(region);
                        _vm.EnterEditor(img);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Region crop failed");
                        _vm.StatusText = "Region capture failed: " + ex.Message;
                    }
                    finally
                    {
                        // 显示器抓帧位图释放
                        foreach (var s in displayShots) s.Dispose();
                    }
                    Show();
                    Activate();
                });
            }

            void OnAnyCancelled()
            {
                if (completed) return;
                completed = true;
                Dispatcher.UIThread.Post(() =>
                {
                    CloseAll();
                    foreach (var s in displayShots) s.Dispose();
                    if (wasVisible) { Show(); Activate(); }
                });
            }

            for (int i = 0; i < displays.Count; i++)
            {
                var display = displays[i];
                var shot = displayShots[i];
                var overlay = new OverlayWindow(shot.Bitmap, display);
                overlay.Confirmed += (_, region) => OnAnyConfirmed(region);
                overlay.Cancelled += (_, _) => OnAnyCancelled();
                overlays.Add(overlay);
            }

            foreach (var o in overlays) o.Show();
            // 焦点给主屏那个（默认第一个 primary），保证 Enter/Esc 有地方接
            var primaryIdx = displays.ToList().FindIndex(d => d.IsPrimary);
            if (primaryIdx < 0) primaryIdx = 0;
            overlays[primaryIdx].Activate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Region capture setup failed");
            foreach (var o in overlays) { try { o.Close(); } catch { } }
            foreach (var s in displayShots) s.Dispose();
            if (wasVisible) Show();
        }
    }

    /// <summary>
    /// Window 捕获：弹出 <see cref="WindowPickerDialog"/> 让用户挑一个顶层窗口，
    /// 主窗口隐藏 → 等 250ms 让候选窗口浮到前台 → <see cref="ICaptureService.CaptureWindow"/> →
    /// 进入编辑器。用户取消 picker 则复原。
    /// </summary>
    public async Task CaptureWindowAsync()
    {
        _logger.LogInformation("Capture Window requested");

        var picker = new WindowPickerDialog(_windowService);
        WindowInfo? target = null;
        if (IsVisible)
        {
            await picker.ShowDialog(this);
            target = picker.Selected;
        }
        else
        {
            // 从托盘/热键触发，无 owner，直接 Show + 等关闭
            var tcs = new TaskCompletionSource();
            picker.Closed += (_, _) => tcs.TrySetResult();
            picker.Show();
            picker.Activate();
            await tcs.Task;
            target = picker.Selected;
        }

        if (target is null)
        {
            _logger.LogDebug("Window picker cancelled");
            return;
        }

        var wasVisible = IsVisible;
        if (wasVisible)
        {
            Hide();
        }

        try
        {
            // 让目标窗口浮到前台并稍等其重绘（尤其 UWP/DirectComposition 需要 tick 一下 DWM）
            _windowService.TryBringToForeground(target.Handle);
            await Task.Delay(250);

            var img = _capture.CaptureWindow(target.Handle);
            _vm.EnterEditor(img);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Window capture failed");
            _vm.StatusText = "Window capture failed: " + ex.Message;
        }
        finally
        {
            if (wasVisible) { Show(); Activate(); }
        }
    }
}
