using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Prtsc.Core.Windows;
using PixelRect = Prtsc.Core.Common.PixelRect;

namespace Prtsc.Platform.Mac.Windowing;

/// <summary>
/// CGWindowList 实现的窗口枚举（IWindowService）。
/// 语义对齐 Windows WindowEnumerator：用户可见、可截屏的顶层窗口，Z 序。
/// Mac 优势：CGWindowListCreateImage 天然抓含遮挡内容，无需 TryBringToForeground（no-op + 日志）。
/// </summary>
public sealed class MacWindowService : IWindowService
{
    private readonly IWindowListApi _api;
    private readonly string _selfProcessName;
    private readonly double _mainScale;

    public MacWindowService(IWindowListApi api, string selfProcessName, double mainScale)
    {
        _api = api;
        _selfProcessName = selfProcessName;
        _mainScale = mainScale;
    }

    /// <summary>mainScale 省略时自动取主屏 ScaleFactor。</summary>
    public MacWindowService(IWindowListApi api, double mainScale)
        : this(api, SelfProcessName(), mainScale) { }

    public MacWindowService(IWindowListApi api)
        : this(api, SelfProcessName(), MainScale()) { }

    public IReadOnlyList<WindowInfo> EnumerateWindows()
    {
        var rows = _api.CopyWindowList();
        var result = new List<WindowInfo>(rows.Length);
        foreach (var r in rows)
        {
            // 只列普通窗口层（layer 0）；控制中心/Dock/菜单栏等系统层全排除
            if (r.Layer != 0) continue;
            // 排除自身
            if (string.Equals(r.Owner, _selfProcessName, StringComparison.OrdinalIgnoreCase)) continue;
            // 排除过小窗口与全透明窗口（Windows 端同样排除面积过小的）
            if (r.Bounds.Size.Width < 40 || r.Bounds.Size.Height < 40) continue;
            if (r.Alpha <= 0.01) continue;

            var bounds = new PixelRect(
                (int)Math.Round(r.Bounds.Origin.X * _mainScale),
                (int)Math.Round(r.Bounds.Origin.Y * _mainScale),
                (int)Math.Round(r.Bounds.Size.Width * _mainScale),
                (int)Math.Round(r.Bounds.Size.Height * _mainScale));

            result.Add(new WindowInfo(
                (nint)r.WindowId,
                string.IsNullOrEmpty(r.Title) ? r.Owner : r.Title,
                r.Owner,
                bounds,
                IsMinimized: !r.IsOnscreen));
        }
        return result;
    }

    public WindowInfo? GetForegroundWindow()
    {
        foreach (var w in EnumerateWindows())
        {
            if (!w.IsMinimized) return w;
        }
        return null;
    }

    /// <summary>
    /// no-op：Mac 端 CGWindowListCreateImage 直接抓窗口合成内容（含被遮挡区域），
    /// 不需要目标窗口在前台。保留接口以对齐 Windows 语义。
    /// </summary>
    public void TryBringToForeground(nint hwnd)
    {
        // intentionally no-op
    }

    private static string SelfProcessName() =>
        Process.GetCurrentProcess().ProcessName;

    private static double MainScale()
    {
        var raw = new Capture.CGDisplayApi().EnumerateDisplays();
        var main = Array.Find(raw, r => r.IsMain);
        if (main is null && raw.Length > 0) main = raw[0];
        return main is null ? 1.0 : Capture.MacDisplayGeometry.DeriveScaleFactor(main);
    }
}
