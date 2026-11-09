using Avalonia;
using Avalonia.Controls;
using Prtsc.Core.Capture;

namespace Prtsc.App.Views;

/// <summary>
/// D-1 延时倒计时角标窗：无边框置顶小窗，只显示剩余秒数 + 取消提示。
/// 位置 = 主显示器 WorkArea 顶部居中（WorkArea 已排除任务栏与 macOS 菜单栏，天然避开角落）。
/// App 负责生命周期：Started 时 Show、Tick 时刷新、Completed/Cancel 时 Close，关闭后 ≥250ms 才抓帧。
/// </summary>
public partial class CountdownBadgeWindow : Window
{
    /// <summary>估算的角标宽度（DIP）：用于在主屏顶部居中，误差可接受。</summary>
    private const double EstimatedWidthDip = 150;

    private const int TopMarginPx = 12;

    /// <summary>运行时加载器要求：无参公共构造。位置在 Show 前算好，避免开屏闪跳。</summary>
    public CountdownBadgeWindow()
    {
        InitializeComponent();
        Position = ComputePosition();
    }

    /// <summary>刷新剩余秒数（Started/Tick 回调，UI 线程）。</summary>
    public void UpdateRemaining(int seconds) => SecondsText.Text = Math.Max(0, seconds).ToString();

    private PixelPoint ComputePosition()
    {
        try
        {
            var displays = AppServices.GetRequiredService<ICaptureService>().EnumerateDisplays();
            var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
            if (primary is not null)
            {
                var workArea = primary.WorkArea; // 已排除任务栏 / macOS 菜单栏
                var widthPx = (int)(EstimatedWidthDip * primary.ScaleFactor);
                var x = workArea.X + Math.Max(0, (workArea.Width - widthPx) / 2);
                return new PixelPoint(x, workArea.Y + TopMarginPx);
            }
        }
        catch
        {
            // 枚举显示器失败（headless/平台异常）：回退默认位置
        }
        return new PixelPoint(100, 40);
    }
}
