using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Prtsc.App.Localization;
using Prtsc.Core.Capture;
using Prtsc.Core.Common;
using Prtsc.Core.Config;
// 捕获语义的 PixelRect 是 Core 的物理像素矩形；Avalonia.PixelRect 是 DIP 坐标，二者不可混用
using PixelRect = Prtsc.Core.Common.PixelRect;

namespace Prtsc.App.Views;

/// <summary>
/// G-26 Capture Launcher（flameshot 式独立启动器）：模式单选 + X/Y/W/H 数值框 + 延时下拉 + 开始截图。
/// 代码后置直连（视图状态为主，不设 VM，对齐 PinnedWindow 风格）。
/// 「开始截图」→ 自关（关窗 200ms 防残影）→ 走 App 的统一延时+捕获咽喉点（LaunchCaptureAsync）。
/// </summary>
public partial class CaptureLauncherWindow : Window
{
    /// <summary>关窗后到触发的间隙：与 MainWindow Hide+Delay(200) 同源，让桌面合成器撤掉本窗。</summary>
    private const int CloseToTriggerGapMs = 200;

    private readonly ICaptureService _capture;
    private readonly IConfigStore _store;

    public CaptureLauncherWindow()
        : this(AppServices.GetRequiredService<ICaptureService>(),
               AppServices.GetRequiredService<IConfigStore>()) { }

    public CaptureLauncherWindow(ICaptureService capture, IConfigStore store)
    {
        _capture = capture;
        _store = store;
        InitializeComponent();
        Prefill();
    }

    /// <summary>
    /// 预填：主显示器 bounds（物理像素）+ config 默认延时（钳制后映射下拉下标）。
    /// 决策：不持久化「上次值」——launcher 本来就是精确输入场景，主屏尺寸是最合理起点。
    /// </summary>
    private void Prefill()
    {
        DisplayInfo? primary = null;
        try
        {
            var displays = _capture.EnumerateDisplays();
            primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
        }
        catch
        {
            // 枚举失败（headless/平台异常）：留 AXAML 默认值
        }

        if (primary is not null)
        {
            XBox.Text = primary.Bounds.X.ToString();
            YBox.Text = primary.Bounds.Y.ToString();
            WBox.Text = primary.Bounds.Width.ToString();
            HBox.Text = primary.Bounds.Height.ToString();
        }

        DelayBox.SelectedIndex = CaptureDelayOptions.IndexOf(_store.Current.Capture.DelaySeconds);
    }

    private bool IsFullscreenMode => ModeFullscreen.IsChecked == true;

    /// <summary>模式切换：全屏时隐藏几何区（保留输入，切回不清空）。</summary>
    private void OnModeClick(object? sender, RoutedEventArgs e)
    {
        GeometryHost.IsVisible = !IsFullscreenMode;
        HideError();
    }

    private async void OnStartClick(object? sender, RoutedEventArgs e)
    {
        HideError();

        var delay = CaptureDelayOptions.AllowedSeconds[
            Math.Clamp(DelayBox.SelectedIndex, 0, CaptureDelayOptions.AllowedSeconds.Length - 1)];

        PixelRect? region = null;
        if (!IsFullscreenMode)
        {
            if (!RegionRectSpec.TryParse(XBox.Text, YBox.Text, WBox.Text, HBox.Text, out var rect, out var error))
            {
                ShowError(error == RegionRectErrorKind.NonPositiveSize
                    ? Loc.T("Launcher.Error.Size")
                    : Loc.T("Launcher.Error.Parse"));
                return;
            }

            // 越界钳进虚拟桌面（不弹窗）；空 rect（W/H 已 >0，理论上不会发生）兜底报错
            var bounds = RegionRectSpec.VirtualDesktopBounds(SafeDisplays());
            var clamped = RegionRectSpec.ClampToBounds(rect, bounds);
            if (clamped.IsEmpty)
            {
                ShowError(Loc.T("Launcher.Error.Parse"));
                return;
            }
            region = clamped;
        }

        var fullscreen = IsFullscreenMode;
        Close();
        await Task.Delay(CloseToTriggerGapMs);

        if (Application.Current is App app)
            await app.LaunchCaptureAsync(fullscreen, region, delay);
    }

    private DisplayInfo[] SafeDisplays()
    {
        try { return _capture.EnumerateDisplays().ToArray(); }
        catch { return []; }
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.IsVisible = true;
        // 无声提示：不抢焦点不弹窗，光标留在起点方便改
        Dispatcher.UIThread.Post(() => XBox.Focus());
    }

    private void HideError() => ErrorText.IsVisible = false;
}
