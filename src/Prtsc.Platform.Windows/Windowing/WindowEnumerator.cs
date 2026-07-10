using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Prtsc.Core.Common;
using Prtsc.Core.Windows;
using Prtsc.Platform.Windows.Interop;

namespace Prtsc.Platform.Windows.Windowing;

/// <summary>
/// 顶层窗口枚举。用 <see cref="User32.EnumWindows"/> 遍历，
/// 应用一组过滤：可见 / 未 cloak / 有标题 / 非本进程 / 面积 &gt;= 40x40。
/// Bounds 优先取 <c>DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)</c>，去除 Win10+ 的窗口投影。
/// </summary>
public sealed class WindowEnumerator : IWindowService
{
    private const int MinDim = 40;

    private readonly ILogger<WindowEnumerator> _logger;
    private readonly int _selfPid = Environment.ProcessId;

    public WindowEnumerator(ILogger<WindowEnumerator> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<WindowInfo> EnumerateWindows()
    {
        var list = new List<WindowInfo>(64);
        var gcHandle = GCHandle.Alloc(list);
        try
        {
            User32.EnumWindows(EnumProc, GCHandle.ToIntPtr(gcHandle));
        }
        finally
        {
            gcHandle.Free();
        }
        _logger.LogDebug("EnumerateWindows -> {N} candidates", list.Count);
        return list;
    }

    public WindowInfo? GetForegroundWindow()
    {
        var hwnd = User32.GetForegroundWindow();
        if (hwnd == 0) return null;
        if (IsOwnedByThisProcess(hwnd)) return null;
        return TryBuild(hwnd);
    }

    public void TryBringToForeground(nint hwnd)
    {
        if (hwnd == 0) return;
        try
        {
            if (User32.IsIconic(hwnd))
                User32.ShowWindow(hwnd, User32.SW_RESTORE);
            User32.SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "TryBringToForeground failed for hwnd=0x{Hwnd:X}", hwnd);
        }
    }

    private bool EnumProc(nint hwnd, nint lParam)
    {
        try
        {
            var listHandle = GCHandle.FromIntPtr(lParam);
            if (listHandle.Target is not List<WindowInfo> list) return true;

            var info = TryBuild(hwnd);
            if (info != null) list.Add(info);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "EnumProc skipped a window");
        }
        return true;
    }

    private WindowInfo? TryBuild(nint hwnd)
    {
        if (!User32.IsWindowVisible(hwnd)) return null;

        // Skip cloaked (虚拟桌面之外、UWP 后台等)
        var hr = DwmApi.DwmGetWindowAttribute(hwnd, DwmApi.DWMWA_CLOAKED, out int cloaked, sizeof(int));
        if (hr == 0 && cloaked != 0) return null;

        // 排除自己
        if (IsOwnedByThisProcess(hwnd)) return null;

        // 无标题窗口一般是浮层/工具窗口，剔除
        var titleLen = User32.GetWindowTextLengthW(hwnd);
        if (titleLen <= 0) return null;

        var buf = new char[Math.Min(titleLen + 1, 512)];
        var copied = User32.GetWindowTextW(hwnd, buf, buf.Length);
        if (copied <= 0) return null;
        var title = new string(buf, 0, copied);

        // 面积过滤（先尝试真实几何）
        var bounds = GetTrueBounds(hwnd);
        if (bounds.IsEmpty || bounds.Width < MinDim || bounds.Height < MinDim) return null;

        // 排除某些系统窗口（按类名）
        if (IsSystemChrome(hwnd)) return null;

        var processName = TryGetProcessName(hwnd);
        var minimized = User32.IsIconic(hwnd);

        return new WindowInfo(hwnd, title, processName, bounds, minimized);
    }

    private static bool IsSystemChrome(nint hwnd)
    {
        var buf = new char[128];
        var len = User32.GetClassNameW(hwnd, buf, buf.Length);
        if (len <= 0) return false;
        var cls = new string(buf, 0, len);
        // 常见需过滤的壳类名
        return cls switch
        {
            "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
                or "Windows.UI.Core.CoreWindow" => true,
            _ => false,
        };
    }

    private static PixelRect GetTrueBounds(nint hwnd)
    {
        // 优先 DWMWA_EXTENDED_FRAME_BOUNDS，去除 Aero 阴影边距
        var hr = DwmApi.DwmGetWindowAttribute(hwnd, DwmApi.DWMWA_EXTENDED_FRAME_BOUNDS,
            out User32.RECT rc, Marshal.SizeOf<User32.RECT>());
        if (hr != 0)
        {
            if (!User32.GetWindowRect(hwnd, out rc)) return default;
        }
        return new PixelRect(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
    }

    private bool IsOwnedByThisProcess(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)_selfPid;
    }

    private static string TryGetProcessName(nint hwnd)
    {
        try
        {
            User32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return string.Empty;
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
