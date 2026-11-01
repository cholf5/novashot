using System.Runtime.InteropServices;

namespace Prtsc.Platform.Linux.Interop;

/// <summary>
/// X11 原始信息（interop 层产出、可 Fake），几何一律<b>物理像素</b>。
/// </summary>
/// <param name="Crtc">XRandR CRTC 句柄。</param>
/// <param name="OutputName">连接器名（eDP-1 / HDMI-0 …），作为 DeviceName。</param>
/// <param name="X">CRTC 全局 X（物理像素；X11 支持负坐标）。</param>
/// <param name="Y">CRTC 全局 Y。</param>
/// <param name="Width">物理像素宽。</param>
/// <param name="Height">物理像素高。</param>
/// <param name="MmWidth">物理毫米宽（0 = 未知）。</param>
/// <param name="MmHeight">物理毫米高（0 = 未知）。</param>
/// <param name="IsPrimary">XRandR primary output。</param>
public sealed record XDisplayRaw(
    nint Crtc,
    string OutputName,
    int X,
    int Y,
    int Width,
    int Height,
    int MmWidth,
    int MmHeight,
    bool IsPrimary);

/// <summary>
/// XImage 像素读取抽象：LinuxCaptureService 从这里拿屏幕像素，测试注入纯色实现。
/// GetPixel 返回 0xAARRGGBB（X11 ZPixmap 32bpp 按机器序读出后归一）。
/// </summary>
public interface IXImage : IDisposable
{
    int Width { get; }
    int Height { get; }
    uint GetPixel(int x, int y);
}

/// <summary>
/// Xlib/XRandR 门面。方法语义与 Xlib 一一对应；测试用 <see cref="IXLibApi"/> Fake 注入。
/// 显示器/抓屏/窗口属性全部经由此接口，服务层不做任何 P/Invoke。
/// </summary>
public interface IXLibApi : IDisposable
{
    /// <summary>打开 display（XOpenDisplay(null)）。失败返回 false。</summary>
    bool Open();

    /// <summary>关闭打开的 display。</summary>
    void Close();

    /// <summary>默认 screen 的根窗。未 Open 时为 0。</summary>
    nint RootWindow { get; }

    /// <summary>根窗尺寸（物理像素）。</summary>
    (int Width, int Height) GetRootSize();

    /// <summary>屏幕深度（bpp；抓屏用）。</summary>
    int GetScreenDepth();

    /// <summary>XRandR 枚举活动 CRTC → 显示器列表（含 primary 与毫米尺寸）。</summary>
    IReadOnlyList<XDisplayRaw> EnumerateDisplays();

    /// <summary>
    /// 抓取根窗上的物理像素矩形为 <see cref="IXImage"/>。失败返回 null。
    /// </summary>
    IXImage? GetImage(int x, int y, int width, int height);

    // ---- 窗口 ----

    /// <summary>EWMH _NET_CLIENT_LIST（管理器管理的顶层窗，Z 序）。无 EWMH 时回退 XQueryTree 一级子窗。</summary>
    nint[] ClientWindows();

    /// <summary>窗口属性批量读取；不存在/失败返回 null。</summary>
    XWindowInfo? QueryWindow(nint window);

    /// <summary>窗口 PID（_NET_WM_PID）。未知返回 0。</summary>
    int WindowPid(nint window);

    /// <summary>当前激活窗（_NET_ACTIVE_WINDOW）。无返回 0。</summary>
    nint ActiveWindow();

    /// <summary>把窗口提到前台并聚焦（_NET_ACTIVE_WINDOW client message；EWMH 缺失回退 XRaiseWindow）。</summary>
    bool ActivateWindow(nint window);

    /// <summary>窗口图像：XComposite NameWindowPixmap + XGetImage；合成器不可用时返回 null。</summary>
    IXImage? GetWindowImage(nint window, int width, int height);

    // ---- 热键事件泵 ----

    /// <summary>创建 1×1 input-only 事件窗并 XSelectInput KeyPress。返回窗句柄。</summary>
    nint CreateEventWindow();

    /// <summary>已创建的事件窗（未创建为 0）。供注册方传给 GrabKey。</summary>
    nint EventWindowHandle { get; }

    /// <summary>keysym → 当前 keymap 的 keycode（XKeycodeToKeysym 反查）。无对应返回 0。</summary>
    uint KeycodeToKeysym(uint keysym);

    /// <summary>XGrabKey（any owner events=false, async grab）。False = 键已被占。</summary>
    bool GrabKey(nint eventWindow, uint modifiers, uint keycode);

    /// <summary>阻塞取下一个事件；返回 keycode 与 modifiers，取消等待时返回 null。</summary>
    (uint Keycode, uint Modifiers)? NextKeyEvent(CancellationToken ct);

    /// <summary>从事件线程唤醒并终止泵（内部写 self-pipe）。</summary>
    void WakeEventLoop();

    /// <summary>错误处理：BadAccess 等不崩进程。</summary>
    event EventHandler<string>? XError;
}

/// <summary>窗口批量信息（EWMH + WM_CLASS 归并）。</summary>
public sealed record XWindowInfo(
    nint Handle,
    string Title,
    string WmClassName,
    string WmInstanceName,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsMapped,
    bool IsHidden); // _NET_WM_STATE_HIDDEN
