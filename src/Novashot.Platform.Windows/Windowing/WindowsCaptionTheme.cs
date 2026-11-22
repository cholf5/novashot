using Novashot.Platform.Windows.Interop;

namespace Novashot.Platform.Windows.Windowing;

/// <summary>
/// B-9：Windows 深色标题栏。
///
/// 两层缺一不可：
/// ① Win32 经典 caption 永远不跟随应用主题，必须显式向 DWM 声明
///    <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c>（属性 20；1809–1903 回落 19）；
/// ② 属性写入 ≠ 视觉生效——DWM 把 caption 重组合成推迟到下一次 frame 事件，
///    写完必须显式 <c>SetWindowPos(SWP_FRAMECHANGED)</c> 强制重算。
///
/// mac/Linux 走各自平台层，不经过这里（macOS 原生深色材质自动跟随标题栏）。
/// </summary>
public static class WindowsCaptionTheme
{
    /// <summary>B-9：设置标题栏明暗。返回是否至少一个 DWM 属性写入成功。</summary>
    public static bool Apply(nint hwnd, bool dark)
    {
        // 无窗口句柄（headless / 尚未建窗）与 DWM 不可用环境（精简/服务端镜像）静默跳过
        if (!OperatingSystem.IsWindows() || hwnd == nint.Zero) return false;

        // Light 必须显式写 0：同一 HWND 重建/主题来回切时残留值会留一个深色标题栏
        var value = dark ? 1 : 0;
        var applied = TrySetAttribute(hwnd, DwmApi.DWMWA_USE_IMMERSIVE_DARK_MODE, value)
                   || TrySetAttribute(hwnd, DwmApi.DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, value);

        if (applied) ForceFrameRecompute(hwnd);
        return applied;
    }

    private static bool TrySetAttribute(nint hwnd, uint attribute, int value)
    {
        try
        {
            return DwmApi.DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == 0;
        }
        catch
        {
            // dwmapi 缺席/被禁用：保持系统默认标题栏，绝不影响窗口本身
            return false;
        }
    }

    /// <summary>B-9：属性写入后强制 DWM 重算非客户区（没有它 = 赌 DWM 下一次 frame 的时机）。</summary>
    private static void ForceFrameRecompute(nint hwnd)
    {
        try
        {
            User32.SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0,
                User32.SWP_NOSIZE | User32.SWP_NOMOVE | User32.SWP_NOZORDER
                | User32.SWP_NOACTIVATE | User32.SWP_FRAMECHANGED);
        }
        catch
        {
            // 尽力而为：失败只意味着本次可能保留旧 caption 合成
        }
    }
}
