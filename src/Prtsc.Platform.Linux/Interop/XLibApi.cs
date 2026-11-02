using System.Runtime.InteropServices;
using static Prtsc.Platform.Linux.Interop.XLibNative;

namespace Prtsc.Platform.Linux.Interop;

/// <summary>
/// Xlib/XRandR P/Invoke 声明与真实现。lib 名按 Linux 惯例带 so 后缀（libX11.so.6）。
/// 所有失败路径不抛异常（X11 惯例返回 0/null），由服务层决定语义。
/// </summary>
public sealed unsafe class XLibApi : IXLibApi
{
    private nint _display;
    private nint _eventWindow;
    private int _eventPipeRead = -1;
    private int _eventPipeWrite = -1;
    private readonly Lock _loopLock = new();

    public event EventHandler<string>? XError;

    public bool Open()
    {
        if (_display != nint.Zero) return true;
        _display = XOpenDisplay(null);
        return _display != nint.Zero;
    }

    public void Close()
    {
        if (_display != nint.Zero) XCloseDisplay(_display);
        _display = nint.Zero;
    }

    public nint RootWindow => _display != nint.Zero
        ? XDefaultRootWindow(_display)
        : nint.Zero;

    public (int Width, int Height) GetRootSize()
    {
        if (_display == nint.Zero) return (0, 0);
        if (XGetWindowAttributes(_display, RootWindow, out var attr) == 0) return (0, 0);
        return (attr.Width, attr.Height);
    }

    public int GetScreenDepth() =>
        _display != nint.Zero ? XDefaultDepth(_display, XDefaultScreen(_display)) : 0;

    public IReadOnlyList<XDisplayRaw> EnumerateDisplays()
    {
        var result = new List<XDisplayRaw>();
        if (_display == nint.Zero) return result;

        nint res = XRRGetScreenResourcesCurrent(_display, RootWindow);
        if (res == nint.Zero) return result;
        try
        {
            var resources = Marshal.PtrToStructure<XRRScreenResources>(res);
            nint primary = XRRGetOutputPrimary(_display, RootWindow);

            for (var i = 0; i < resources.NCrtc; i++)
            {
                nint crtc = Marshal.ReadIntPtr(resources.Crtcs, i * IntPtr.Size);
                if (crtc == nint.Zero) continue;
                nint crtcInfo = XRRGetCrtcInfo(_display, res, crtc);
                if (crtcInfo == nint.Zero) continue;
                try
                {
                    var info = Marshal.PtrToStructure<XRCrtcInfo>(crtcInfo);
                    if (info.NOutput <= 0 || info.Width == 0 || info.Height == 0) continue;

                    nint output = Marshal.ReadIntPtr(info.Outputs, 0);
                    var (name, mmW, mmH) = ReadOutputInfo(res, output);
                    var isPrimary = output == primary;

                    result.Add(new XDisplayRaw(crtc, name, info.X, info.Y,
                        info.Width, info.Height, mmW, mmH, isPrimary));
                }
                finally
                {
                    XRRFreeCrtcInfo(crtcInfo);
                }
            }
        }
        finally
        {
            XRRFreeScreenResources(res);
        }
        return result;
    }

    private (string Name, int MmW, int MmH) ReadOutputInfo(nint resources, nint output)
    {
        nint oi = XRRGetOutputInfo(_display, resources, output);
        if (oi == nint.Zero) return ("unknown", 0, 0);
        try
        {
            var info = Marshal.PtrToStructure<XRROutputInfoManaged>(oi);
            var name = info.NameLen is > 0 and <= 64
                ? Marshal.PtrToStringUTF8(oi + XRROutputInfoManaged.NameOffset, info.NameLen) ?? "unknown"
                : "unknown";
            return (name, info.MmWidth, info.MmHeight);
        }
        finally
        {
            XRRFreeOutputInfo(oi);
        }
    }

    public IXImage? GetImage(int x, int y, int width, int height)
    {
        if (_display == nint.Zero || width <= 0 || height <= 0) return null;
        nint image = XGetImage(_display, RootWindow, x, y, (uint)width, (uint)height, AllPlanes, ZPixmap);
        return image == nint.Zero ? null : new XImageHandle(_display, image, width, height);
    }

    public nint[] ClientWindows()
    {
        if (_display == nint.Zero) return [];
        var list = ReadWindowArray(NetClientList, RootWindow);
        if (list.Length > 0) return list;

        // EWMH 缺失（极简 WM）：回退根窗一级子窗
        if (XQueryTree(_display, RootWindow, out _, out _, out var children, out var count) != 0 && count > 0)
        {
            var result = new nint[count];
            for (var i = 0; i < count; i++) result[i] = Marshal.ReadIntPtr(children, i * IntPtr.Size);
            XFree(children);
            Array.Reverse(result); // QueryTree 是底→顶，改成前台在前
            return result;
        }
        return [];
    }

    public XWindowInfo? QueryWindow(nint window)
    {
        if (_display == nint.Zero || window == nint.Zero) return null;
        if (XGetWindowAttributes(_display, window, out var attr) == 0) return null;

        var title = ReadTextProperty(window, NetWmName) ?? ReadTextProperty(window, WmName) ?? string.Empty;
        var (cls, instance) = ReadWmClass(window);
        var stateHidden = ReadHiddenState(window);

        return new XWindowInfo(window, title, cls, instance,
            attr.X, attr.Y, attr.Width, attr.Height,
            attr.MapState == 2, stateHidden);
    }

    public int WindowPid(nint window)
    {
        if (_display == nint.Zero || window == nint.Zero) return 0;
        var pid = ReadCardinalProperty(window, NetWmPid);
        return pid is > 0 and <= int.MaxValue ? (int)pid : 0;
    }
    public nint ActiveWindow()
    {
        if (_display == nint.Zero) return nint.Zero;
        var data = ReadWindowProperty(RootWindow, NetActiveWindow, 1);
        if (data is not { Length: >= 8 }) return nint.Zero;
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            return Marshal.ReadIntPtr(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    public bool ActivateWindow(nint window)
    {
        if (_display == nint.Zero || window == nint.Zero) return false;

        var wmName = ReadTextProperty(window, NetWmName);
        if (wmName != null)
        {
            // EWMH：发 _NET_ACTIVE_WINDOW client message（sourceIndication=2 应用请求）
            var ev = new XClientMessageEvent
            {
                Type = ClientMessage,
                Window = window,
                MessageType = NetActiveWindow,
                Format = 32,
            };
            ev.Data0 = 2; ev.Data1 = 0; ev.Data2 = 0; ev.Data3 = 0; ev.Data4 = 0;
            nint p = Marshal.AllocHGlobal(Marshal.SizeOf<XClientMessageEvent>());
            try
            {
                Marshal.StructureToPtr(ev, p, false);
                XSendEvent(_display, RootWindow, false,
                    SubstructureRedirectMask | SubstructureNotifyMask, p);
                XFlush(_display);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        XRaiseWindow(_display, window);
        XFlush(_display);
        return true;
    }

    public IXImage? GetWindowImage(nint window, int width, int height)
    {
        if (_display == nint.Zero || window == nint.Zero || width <= 0 || height <= 0) return null;
        // 优先 XComposite NameWindowPixmap（可抓被遮挡/加阴影前的内容）；失败回退直接抓窗口矩形
        nint pixmap = nint.Zero;
        try
        {
            if (XCompositeNameWindowPixmap(_display, window, out pixmap) != 0 && pixmap != nint.Zero)
            {
                var img = XGetImage(_display, pixmap, 0, 0, (uint)width, (uint)height, AllPlanes, ZPixmap);
                return img == nint.Zero ? null : new PixmapImageHandle(_display, img, pixmap, width, height);
            }
        }
        catch (DllNotFoundException)
        {
            throw; // libXcomposite 缺失属于部署问题，不静默
        }
        finally
        {
            if (pixmap != nint.Zero) XFreePixmap(_display, pixmap);
        }

        var direct = XGetImage(_display, window, 0, 0, (uint)width, (uint)height, AllPlanes, ZPixmap);
        return direct == nint.Zero ? null : new XImageHandle(_display, direct, width, height);
    }

    public nint CreateEventWindow()
    {
        if (_display == nint.Zero) return nint.Zero;
        _eventWindow = XCreateSimpleWindow(_display, RootWindow, -2, -2, 1, 1, 0, 0u, 0u);
        XSelectInput(_display, _eventWindow, KeyPressMask | StructureNotifyMask);
        XFlush(_display);

        // self-pipe：Stop/Dispose 时唤醒阻塞的 NextKeyEvent
        if (_eventPipeRead < 0)
        {
            var fds = new int[2];
            fixed (int* p = fds)
                if (pipe(p) == 0)
                {
                    _eventPipeRead = fds[0];
                    _eventPipeWrite = fds[1];
                }
        }
        return _eventWindow;
    }

    public bool GrabKey(nint eventWindow, uint modifiers, uint keycode)
    {
        if (_display == nint.Zero || eventWindow == nint.Zero || keycode == 0) return false;
        // XGrabKey 对 NumLock/CapsLock 修饰变体也要抓（否则开 NumLock 时热键失效，经典坑）
        var ok = XGrabKey(_display, keycode, modifiers, eventWindow, false, GrabModeAsync, GrabModeAsync);
        if (modifiers is not (0 or Mod2Mask or LockMask))
        {
            XGrabKey(_display, keycode, modifiers | Mod2Mask, eventWindow, false, GrabModeAsync, GrabModeAsync);
            XGrabKey(_display, keycode, modifiers | LockMask, eventWindow, false, GrabModeAsync, GrabModeAsync);
            XGrabKey(_display, keycode, modifiers | Mod2Mask | LockMask, eventWindow, false, GrabModeAsync, GrabModeAsync);
        }
        XFlush(_display);
        return ok == 0;
    }

    public void UngrabKey(nint eventWindow, uint modifiers, uint keycode)
    {
        if (_display == nint.Zero || eventWindow == nint.Zero || keycode == 0) return;
        // 与 GrabKey 对称：base 必解；仅当当初抓过 NumLock/CapsLock 变体时才多解三个
        XUngrabKey(_display, keycode, modifiers, eventWindow);
        if (modifiers is not (0 or Mod2Mask or LockMask))
        {
            XUngrabKey(_display, keycode, modifiers | Mod2Mask, eventWindow);
            XUngrabKey(_display, keycode, modifiers | LockMask, eventWindow);
            XUngrabKey(_display, keycode, modifiers | Mod2Mask | LockMask, eventWindow);
        }
        XFlush(_display);
    }

    public (uint Keycode, uint Modifiers)? NextKeyEvent(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 先查 self-pipe（非阻塞），有取消信号立即返回
            if (_eventPipeRead >= 0)
            {
                var probe = new PollFd
                {
                    Fd = _eventPipeRead,
                    Events = PollIn,
                };
                {
                    if (LibCPoll(&probe, 1, 0) > 0)
                    {
                        Span<byte> drain = stackalloc byte[16];
                        _ = read(_eventPipeRead, drain);
                        return null;
                    }
                }
            }

            if (_display == nint.Zero) return null;
            if (XPending(_display) == 0)
            {
                // 阻塞等 X fd 或取消管道，10s 心跳防泄漏
                var both = stackalloc PollFd[2];
                both[0] = new PollFd { Fd = XConnectionNumber(_display), Events = PollIn };
                both[1] = new PollFd { Fd = _eventPipeRead, Events = PollIn };
                var n = LibCPoll(both, _eventPipeRead >= 0 ? 2u : 1u, 10_000);
                if (n <= 0) continue;
                if (_eventPipeRead >= 0 && (both[1].Revents & PollIn) != 0)
                {
                    Span<byte> drain = stackalloc byte[16];
                    _ = read(_eventPipeRead, drain);
                    return null;
                }
            }

            while (XPending(_display) > 0)
            {
                var e = default(XEventRaw);
                XNextEvent(_display, out e);
                if (e.Type != KeyPress) continue;
                var ke = e.Key;
                return (ke.Keycode, ke.State);
            }
        }
        return null;
    }

    public void WakeEventLoop()
    {
        if (_eventPipeWrite < 0) return;
        var one = stackalloc byte[1];
        one[0] = 1;
        _ = write(_eventPipeWrite, one, 1);
    }

    public nint EventWindowHandle => _eventWindow;

    /// <summary>keysym → keycode 反查：遍历 min_keycode..max_keycode 的第一列 keysym。缓存 per-keysym。</summary>
    public uint KeycodeToKeysym(uint keysym)
    {
        if (_display == nint.Zero || keysym == 0) return 0;
        lock (_keycodeCache)
        {
            if (_keycodeCache.TryGetValue(keysym, out var cached)) return cached;

            if (XDisplayKeycodes(_display, out var min, out var max) == 0) return 0;
            for (var kc = min; kc <= max; kc++)
            {
                // 第一列（unshifted）匹配即可（Shift 修饰由 modifier 位表达）
                var ks = XKeycodeToKeysym(_display, (uint)kc, 0);
                if (ks == keysym)
                {
                    _keycodeCache[keysym] = (uint)kc;
                    return (uint)kc;
                }
            }
            _keycodeCache[keysym] = 0;
            return 0;
        }
    }

    private readonly Dictionary<uint, uint> _keycodeCache = [];

    // ---- 属性读取 helpers ----

    private nint[] ReadWindowArray(nint atom, nint window)
    {
        var data = ReadWindowProperty(window, atom, int.MaxValue);
        if (data is null || data.Length < IntPtr.Size) return [];
        var count = data.Length / IntPtr.Size;
        var result = new nint[count];
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            for (var i = 0; i < count; i++)
                result[i] = Marshal.ReadIntPtr(handle.AddrOfPinnedObject(), i * IntPtr.Size);
        }
        finally
        {
            handle.Free();
        }
        return result;
    }

    private string? ReadTextProperty(nint window, nint atom)
    {
        var data = ReadWindowProperty(window, atom, int.MaxValue);
        if (data is null || data.Length == 0) return null;
        // _NET_WM_NAME 是 UTF-8；WM_NAME 可能是 COMPOUND_TEXT，尽力 UTF-8 解
        var s = System.Text.Encoding.UTF8.GetString(data);
        return s.TrimEnd('\0');
    }

    private (string Class, string Instance) ReadWmClass(nint window)
    {
        var data = ReadWindowProperty(window, WmClass, int.MaxValue);
        if (data is null) return ("", "");
        var parts = System.Text.Encoding.Latin1.GetString(data).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return (parts.Length > 1 ? parts[1] : parts.FirstOrDefault() ?? "",
                parts.Length > 0 ? parts[0] : "");
    }

    private bool ReadHiddenState(nint window)
    {
        var data = ReadWindowProperty(window, NetWmState, int.MaxValue);
        if (data is null) return false;
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            for (var i = 0; i + IntPtr.Size <= data.Length; i += IntPtr.Size)
                if (Marshal.ReadIntPtr(handle.AddrOfPinnedObject(), i) == NetWmStateHidden)
                    return true;
            return false;
        }
        finally
        {
            handle.Free();
        }
    }

    private byte[]? ReadWindowProperty(nint window, nint atom, int maxBytes)
    {
        if (XGetWindowProperty(_display, window, atom, 0, maxBytes / 4, false, 0,
                out var actualType, out var actualFormat, out var nItems, out var bytesAfter, out var prop) != 0
            || prop == nint.Zero)
        {
            if (prop != nint.Zero) XFree(prop);
            return null;
        }
        try
        {
            var size = checked((int)nItems * (actualFormat / 8));
            if (size <= 0) return [];
            var data = new byte[size];
            Marshal.Copy(prop, data, 0, size);
            return data;
        }
        finally
        {
            XFree(prop);
        }
    }

    /// <summary>读 32bit cardianl 属性（_NET_WM_PID 等）。缺失返回 null。</summary>
    private uint? ReadCardinalProperty(nint window, nint atom)
    {
        var data = ReadWindowProperty(window, atom, 4);
        if (data is not { Length: >= 4 }) return null;
        return BitConverter.ToUInt32(data, 0);
    }

    public void Dispose()
    {
        WakeEventLoop();
        Close();
        if (_eventPipeRead >= 0) close(_eventPipeRead);
        if (_eventPipeWrite >= 0) close(_eventPipeWrite);
        _eventPipeRead = -1;
        _eventPipeWrite = -1;
    }
}
