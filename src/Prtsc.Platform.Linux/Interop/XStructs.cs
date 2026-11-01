using System.Runtime.InteropServices;

namespace Prtsc.Platform.Linux.Interop;

/// <summary>
/// X11 原始数据结构（与 Xlib 头文件对齐，只取 Prtsc 用到的字段）。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XRectangle
{
    public short X;
    public short Y;
    public ushort Width;
    public ushort Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XWindowAttributes
{
    public int X, Y;
    public int Width, Height;
    public int BorderWidth;
    public int Depth;
    public nint Visual;
    public int Class;
    public int BitGravity;
    public int WinGravity;
    public int BackingStore;
    public ulong BackingPlanes;
    public ulong BackingPixel;
    public bool SaveUnder;
    public bool MapInstalled;
    public int MapState;      // IsUnmapped=0 / IsUnviewable=1 / IsViewable=2
    public long AllEventMasks;
    public long YourEventMask;
    public long DoNotPropagateMask;
    public int OverrideRedirect;
    public nint Screen;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XRRScreenResources
{
    public nint Timestamp;
    public nint ConfigTimestamp;
    public int NCrtc;
    public nint Crtcs;      // RRCrtc*
    public int NOutput;
    public nint Outputs;    // RROutput*
    public int NMode;
    public nint Modes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XRCrtcInfo
{
    public nint Timestamp;
    public int X, Y;
    public ushort Width, Height;
    public nint Mode;
    public ushort Rotation;
    public ushort Rotations;
    public int NOutput;
    public nint Outputs;    // RROutput*
    public nint RotationInfo;
    public int NPossible;
    public nint PossibleOutputs;
}
