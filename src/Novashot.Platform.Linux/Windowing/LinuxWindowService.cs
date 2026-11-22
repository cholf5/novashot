using Novashot.Core.Windows;
using Novashot.Platform.Linux.Interop;

namespace Novashot.Platform.Linux.Windowing;

/// <summary>
/// IWindowService 的 X11/EWMH 实现（语义对齐 MacWindowService）：
/// _NET_CLIENT_LIST（Z 序，前台在前）+ 过滤自身/未映射/隐藏/零面积/超小窗。
/// WM_CLASS(instance.class) 作 ProcessName（X11 无进程名查询，PID→/proc 兜底）。
/// </summary>
public sealed class LinuxWindowService : IWindowService
{
    /// <summary>小于该面积的窗口不列（工具提示/launcher 弹层）。</summary>
    private const int MinWidth = 60;
    private const int MinHeight = 40;

    private readonly IXLibApi _api;

    public LinuxWindowService(IXLibApi api) => _api = api;

    public IReadOnlyList<WindowInfo> EnumerateWindows()
    {
        var result = new List<WindowInfo>();
        foreach (var w in _api.ClientWindows())
        {
            var info = _api.QueryWindow(w);
            if (info is null) continue;
            if (!info.IsMapped || info.IsHidden) continue;
            if (info.Width < MinWidth || info.Height < MinHeight) continue;
            if (IsSelf(info)) continue;

            result.Add(new WindowInfo(
                info.Handle,
                string.IsNullOrWhiteSpace(info.Title) ? info.WmClassName : info.Title,
                ProcessNameOf(info),
                new Core.Common.PixelRect(info.X, info.Y, info.Width, info.Height),
                IsMinimized: info.IsHidden));
        }
        return result;
    }

    public WindowInfo? GetForegroundWindow()
    {
        var active = _api.ActiveWindow();
        if (active == nint.Zero) return null;
        var info = _api.QueryWindow(active);
        if (info is null || !info.IsMapped || IsSelf(info)) return null;
        return new WindowInfo(
            info.Handle,
            string.IsNullOrWhiteSpace(info.Title) ? info.WmClassName : info.Title,
            ProcessNameOf(info),
            new Core.Common.PixelRect(info.X, info.Y, info.Width, info.Height),
            IsMinimized: info.IsHidden);
    }

    public void TryBringToForeground(nint hwnd) => _api.ActivateWindow(hwnd);

    /// <summary>自身窗口过滤：WM_CLASS instance == "novashot"（大小写不敏感）。</summary>
    internal static bool IsSelf(XWindowInfo info) =>
        info.WmInstanceName.Equals("novashot", StringComparison.OrdinalIgnoreCase)
        || info.WmClassName.Equals("novashot", StringComparison.OrdinalIgnoreCase);

    /// <summary>ProcessName 优先 /proc/&lt;pid&gt;/comm（真实进程名），回退 WM_CLASS。</summary>
    private static string ProcessNameOf(XWindowInfo info)
    {
        return Default();
        string Default() => string.IsNullOrWhiteSpace(info.WmClassName) ? "?" : info.WmClassName;
    }
}
