using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

#if WIN_PLATFORM
using System.Runtime.CompilerServices;
using Novashot.Platform.Windows.Windowing;
#endif

namespace Novashot.App.Windowing;

/// <summary>
/// B-9：深色标题栏接线（Windows）。
///
/// Win32 经典 caption 不跟随应用主题，App 在 Windows 上必须逐窗口向 DWM 声明深色
/// （平台细节见 <c>WindowsCaptionTheme</c>）。这里用 Avalonia 的全局
/// <see cref="Window.WindowOpenedEvent"/> 类处理器统一挂接：主窗、设置/云设置/Library、
/// 窗口选择器、更新提示、倒计时角标等每个窗口各有 HWND，一次注册全覆盖，新窗口不需要再记着手写一行。
///
/// 非 Windows TFM 下 <see cref="Attach"/> 是空实现（macOS 原生材质自动跟随标题栏，Linux 视 WM 而定）；
/// 全局钩子仍然注册，这样接线本身可以跨平台测试。
/// </summary>
internal static class CaptionTheme
{
    private static bool _hooked;
    private static int _invocations;
#if WIN_PLATFORM
    private static readonly ConditionalWeakTable<Window, object> Attached = new();
#endif

    /// <summary>测试观测点：全局窗口打开钩子被触发的累计次数（headless 无句柄也计数）。</summary>
    internal static int HookInvocations => Volatile.Read(ref _invocations);

    /// <summary>注册全局窗口打开钩子（幂等；必须在任何窗口 Show 之前调用）。</summary>
    internal static void AttachAll()
    {
        if (_hooked) return;
        _hooked = true;

        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            Interlocked.Increment(ref _invocations);
            Attach(window);
        });
    }

    /// <summary>单个窗口接线（幂等）：立刻写一次，并在运行期明暗切换时补写。</summary>
    internal static void Attach(Window window)
    {
#if WIN_PLATFORM
        if (!Attached.TryAdd(window, string.Empty)) return; // 同窗口重入只挂一次（幂等）
        Apply(window);
        window.ActualThemeVariantChanged += (_, _) => Apply(window);
#endif
    }

#if WIN_PLATFORM
    private static void Apply(Window window)
    {
        var dark = ResolveIsDark(window.ActualThemeVariant, window.RequestedThemeVariant,
            Application.Current?.RequestedThemeVariant);
        WindowsCaptionTheme.Apply(TryGetHandle(window), dark);
    }

    /// <summary>
    /// 明暗裁决：Actual → 窗口 Requested → 应用 Requested → 浅色。
    /// 不能只看 ActualThemeVariant：Avalonia 在 WindowOpened（首次合成之前）时它仍可能是 Default，
    /// 那样会写错一次浅色且再等不到 Changed 事件——深色应用启动后标题栏就会一直白。
    /// </summary>
    internal static bool ResolveIsDark(ThemeVariant? actual, ThemeVariant? requested, ThemeVariant? appRequested)
        => Decide(actual) ?? Decide(requested) ?? Decide(appRequested) ?? false;

    private static bool? Decide(ThemeVariant? variant)
        => variant == ThemeVariant.Dark ? true
         : variant == ThemeVariant.Light ? false
         : null;

    /// <summary>
    /// 取原生 HWND。headless / 尚未建窗（PlatformHandle 为 null）与 headless 的零句柄 STUB
    /// 都返回 0，由平台层统一跳过——测试只锁接线，视觉验收靠真机。
    /// </summary>
    private static nint TryGetHandle(Window window)
    {
        try
        {
            return window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        }
        catch
        {
            return nint.Zero;
        }
    }
#endif
}
