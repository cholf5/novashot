using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Novashot.Platform.Linux.Interop;

/// <summary>
/// Xlib / XRandR / XComposite P/Invoke 声明与 atom 常量。
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe class XLibNative
{
    public const string LibX11 = "libX11.so.6";
    public const string LibXrandr = "libXrandr.so.2";
    public const string LibXcomposite = "libXcomposite.so.1";
    public const string LibC = "libc.so.6";

    // 事件/掩码
    public const int KeyPress = 2;
    public const int ClientMessage = 33;
    public const long KeyPressMask = 1L << 0;
    public const long StructureNotifyMask = 1L << 17;
    public const long SubstructureRedirectMask = 1L << 20;
    public const long SubstructureNotifyMask = 1L << 19;
    public const ulong AllPlanes = ulong.MaxValue;
    public const int ZPixmap = 2;
    public const uint Mod2Mask = 1 << 4;  // NumLock
    public const uint LockMask = 1 << 1;  // CapsLock
    public const int GrabModeAsync = 0;
    public const short PollIn = 0x001;

    // ---- atoms（XInternAtom 缓存） ----
    private static readonly Lock AtomLock = new();
    private static readonly Dictionary<string, nint> Atoms = new();

    public static nint NetClientList => Atom("_NET_CLIENT_LIST");
    public static nint NetActiveWindow => Atom("_NET_ACTIVE_WINDOW");
    public static nint NetWmName => Atom("_NET_WM_NAME");
    public static nint NetWmPid => Atom("_NET_WM_PID");
    public static nint NetWmState => Atom("_NET_WM_STATE");
    public static nint NetWmStateHidden => Atom("_NET_WM_STATE_HIDDEN");
    public static nint WmName => Atom("WM_NAME");
    public static nint WmClass => Atom("WM_CLASS");

    private static nint Atom(string name)
    {
        lock (AtomLock)
        {
            if (Atoms.TryGetValue(name, out var a)) return a;
            a = XInternAtom(IntPtr.Zero, name, false);
            Atoms[name] = a;
            return a;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XEventRaw
    {
        public int Type;
        public fixed long Pad[23];

        /// <summary>把联合体首段重解释为 XKeyEventPart（布局兼容：Type/Series/.../Keycode 顺序一致）。</summary>
        public XKeyEventPart Key
        {
            get
            {
                var h = GCHandle.Alloc(this, GCHandleType.Pinned);
                try
                {
                    return Marshal.PtrToStructure<XKeyEventPart>(h.AddrOfPinnedObject());
                }
                finally
                {
                    h.Free();
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XKeyEventPart
    {
        public int Type;
        public uint Serial;
        public int SendEvent;
        public nint Display;
        public nint Window;
        public nint Root;
        public nint Subwindow;
        public uint Time;
        public int X, Y;
        public int XRoot, YRoot;
        public uint State;
        public uint Keycode;
        public int SameScreen;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XClientMessageEvent
    {
        public int Type;
        public nint Display;   // XSendEvent 会覆写，占位
        public nint Window;
        public nint MessageType;
        public int Format;
        public nint Data0, Data1, Data2, Data3, Data4;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XRROutputInfoManaged
    {
        public nint Timestamp;
        public nint Crtc;
        public int NameLen;
        public int MmWidth;
        public int MmHeight;
        public nint Connection;
        public nint SubpixelOrder;
        public int NCrtcs;
        public nint Crtcs;
        public int NClones;
        public nint Clones;
        public nint Modes;
        public int NPreferred;

        /// <summary>名字字节区跟在结构体之后（Xlib 序列化约定）。</summary>
        public const int NameOffset = 96;
    }

    // ---- Xlib ----

    [DllImport(LibX11)]
    internal static extern nint XOpenDisplay(string? displayName);

    [DllImport(LibX11)]
    internal static extern int XCloseDisplay(nint display);

    [DllImport(LibX11)]
    internal static extern nint XDefaultRootWindow(nint display);

    [DllImport(LibX11)]
    internal static extern int XDefaultScreen(nint display);

    [DllImport(LibX11)]
    internal static extern int XDefaultDepth(nint display, int screen);

    [DllImport(LibX11, SetLastError = false)]
    internal static extern int XGetWindowAttributes(nint display, nint window, out XWindowAttributes attributes);

    [DllImport(LibX11)]
    internal static extern int XGetImage(nint display, nint drawable, int x, int y, uint width, uint height, ulong planeMask, int format);

    [DllImport(LibX11)]
    internal static extern int XDestroyImage(nint image);

    [DllImport(LibX11)]
    internal static extern int XFlush(nint display);

    [DllImport(LibX11)]
    internal static extern int XPending(nint display);

    [DllImport(LibX11)]
    internal static extern int XConnectionNumber(nint display);

    [DllImport(LibX11)]
    internal static extern int XNextEvent(nint display, out XEventRaw eventReturn);

    [DllImport(LibX11)]
    internal static extern int XSendEvent(nint display, nint window, bool propagate, long eventMask, nint eventSend);

    [DllImport(LibX11)]
    internal static extern int XQueryTree(nint display, nint window, out nint rootReturn, out nint parentReturn, out nint childrenReturn, out uint nChildrenReturn);

    [DllImport(LibX11)]
    internal static extern int XFree(nint data);

    [DllImport(LibX11)]
    internal static extern int XRaiseWindow(nint display, nint window);

    [DllImport(LibX11)]
    internal static extern nint XInternAtom(nint display, string atomName, bool onlyIfExists);

    [DllImport(LibX11)]
    internal static extern int XGetWindowProperty(nint display, nint window, nint property,
        long longOffset, long longLength, bool delete, long reqType,
        out nint actualTypeReturn, out int actualFormatReturn,
        out nuint nItemsReturn, out nuint bytesAfterReturn, out nint propReturn);

    [DllImport(LibX11)]
    internal static extern int XCreateSimpleWindow(nint display, nint parent, int x, int y,
        uint width, uint height, uint borderWidth, ulong border, ulong background);

    [DllImport(LibX11)]
    internal static extern int XSelectInput(nint display, nint window, long eventMask);

    [DllImport(LibX11)]
    internal static extern int XGrabKey(nint display, uint keycode, uint modifiers, nint window,
        bool ownerEvents, int pointerMode, int keyboardMode);

    [DllImport(LibX11)]
    internal static extern int XUngrabKey(nint display, uint keycode, uint modifiers, nint window);

    [DllImport(LibX11)]
    internal static extern int XDisplayKeycodes(nint display, out int minKeycodeReturn, out int maxKeycodeReturn);

    [DllImport(LibX11)]
    internal static extern uint XKeycodeToKeysym(nint display, uint keycode, int index);

    // ---- XRandR ----

    [DllImport(LibXrandr)]
    internal static extern nint XRRGetScreenResourcesCurrent(nint display, nint window);

    [DllImport(LibXrandr)]
    internal static extern void XRRFreeScreenResources(nint resources);

    [DllImport(LibXrandr)]
    internal static extern nint XRRGetCrtcInfo(nint display, nint resources, nint crtc);

    [DllImport(LibXrandr)]
    internal static extern void XRRFreeCrtcInfo(nint crtcInfo);

    [DllImport(LibXrandr)]
    internal static extern nint XRRGetOutputInfo(nint display, nint resources, nint output);

    [DllImport(LibXrandr)]
    internal static extern void XRRFreeOutputInfo(nint outputInfo);

    [DllImport(LibXrandr)]
    internal static extern nint XRRGetOutputPrimary(nint display, nint window);

    // ---- XComposite ----

    [DllImport(LibXcomposite)]
    internal static extern int XCompositeNameWindowPixmap(nint display, nint window, out nint pixmapReturn);

    [DllImport(LibX11)]
    internal static extern int XFreePixmap(nint display, nint pixmap);

    // ---- libc ----

    [DllImport(LibC, SetLastError = true)]
    internal static extern int pipe(int* fds);

    [DllImport(LibC, SetLastError = true)]
    internal static extern nint read(int fd, Span<byte> buffer);

    [DllImport(LibC, SetLastError = true)]
    internal static extern nint write(int fd, byte* buffer, nuint count);

    [DllImport(LibC, SetLastError = true, EntryPoint = "poll")]
    internal static extern int LibCPoll(PollFd* fds, uint nfds, int timeout);

    [DllImport(LibC, SetLastError = true)]
    internal static extern int close(int fd);
}
