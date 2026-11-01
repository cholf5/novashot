using Prtsc.Core.Common;
using Prtsc.Platform.Mac.Interop;

namespace Prtsc.Platform.Mac.Windowing;

/// <summary>
/// macOS overlay 窗口 chrome 调整（AppKit 侧，探针实证 2026-11-01）：
/// <para>AppKit 会把无边框窗口钳制进工作区——Show 请求 (0,0,2560,1440)，落地 (0,96,2560,1314)
/// （visibleFrame 恰为 (0,96,2560,1314)），菜单栏/Dock 露出，背景图被非均匀压扁。</para>
/// <para>三步修：① 原生 setFrame: 铺满整块显示器 frame；② setLevel: 提到 NSStatusWindowLevel(25)
/// （菜单栏 24、Dock 20 之上）；③ setCollectionBehavior: CanJoinAllSpaces|FullScreenAuxiliary，
/// 用户在 Space/全屏应用上也能触发截图。</para>
/// </summary>
public static class MacOverlayChrome
{
    internal const long NSStatusWindowLevel = 25;
    internal const ulong NSWindowCollectionBehaviorCanJoinAllSpaces = 1 << 0;
    internal const ulong NSWindowCollectionBehaviorFullScreenAuxiliary = 1 << 9;

    private static nint? _setFrameSel;
    private static nint? _setLevelSel;
    private static nint? _setCollectionBehaviorSel;

    /// <summary>
    /// Show 之后调用一次：铺满目标显示器 + 提层 + Spaces 行为。
    /// nsWindow 为 Avalonia TopLevel.TryGetPlatformHandle().Handle（NSWindow 指针）。
    /// displayBounds 为该显示器的物理像素几何（DisplayInfo.Bounds），内部换算回点。
    /// </summary>
    public static void Apply(nint nsWindow, DisplayInfo display)
    {
        if (nsWindow == nint.Zero) return;
        EnsureSelectors();

        MacNative.objc_msgSendSetFrame(nsWindow, _setFrameSel!.Value, ComputeFrame(display), true);

        // 提到状态栏层，盖住菜单栏(24)/Dock(20)
        MacNative.objc_msgSend(nsWindow, _setLevelSel!.Value, (nint)NSStatusWindowLevel);

        // AllSpaces + FullScreenAuxiliary：任何 Space/全屏应用之上都能弹 overlay
        var behavior = NSWindowCollectionBehaviorCanJoinAllSpaces | NSWindowCollectionBehaviorFullScreenAuxiliary;
        MacNative.objc_msgSend(nsWindow, _setCollectionBehaviorSel!.Value, (nint)behavior);
    }

    /// <summary>
    /// 显示器物理像素 Bounds → NSScreen 点单位 frame（CG 全局坐标左上原点，与 NSScreen.frame 同构）。
    /// </summary>
    internal static CGRect ComputeFrame(DisplayInfo display)
    {
        var s = display.ScaleFactor;
        return new CGRect(
            display.Bounds.X / s, display.Bounds.Y / s,
            display.Bounds.Width / s, display.Bounds.Height / s);
    }

    private static void EnsureSelectors()
    {
        ObjcIds.EnsureLoaded();
        _setFrameSel ??= MacNative.sel_registerName("setFrame:display:");
        _setLevelSel ??= MacNative.sel_registerName("setLevel:");
        _setCollectionBehaviorSel ??= MacNative.sel_registerName("setCollectionBehavior:");
    }
}
