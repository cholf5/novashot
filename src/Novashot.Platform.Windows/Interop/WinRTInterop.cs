using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Novashot.Platform.Windows.Interop;

/// <summary>
/// Windows Runtime interop 桥接：把 WGC (Windows.Graphics.Capture) 与 D3D11 联起来。
///
/// - <see cref="CreateDirect3D11DeviceFromDXGIDevice"/>：从 IDXGIDevice 拿 <c>IDirect3DDevice</c>（WinRT）
/// - <see cref="IGraphicsCaptureItemInterop"/>：从 HWND/HMONITOR 造 <c>GraphicsCaptureItem</c>
/// - <see cref="IDirect3DDxgiInterfaceAccess"/>：从 <c>IDirect3DSurface</c> 里掏出底层 <c>ID3D11Texture2D</c>
/// </summary>
internal static class WinRTInterop
{
    // GraphicsCaptureItem 的 WinRT IID（Windows.Graphics.Capture.IGraphicsCaptureItem）
    public static readonly Guid IGraphicsCaptureItemIID = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    // ID3D11Texture2D 的 IID
    public static readonly Guid ID3D11Texture2DIID = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    [DllImport("d3d11.dll", PreserveSig = false)]
    public static extern void CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice, out nint graphicsDevice);

    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComImport]
    public interface IGraphicsCaptureItemInterop
    {
        void CreateForWindow(nint window, in Guid iid, out nint result);
        void CreateForMonitor(nint monitor, in Guid iid, out nint result);
    }

    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComImport]
    public interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig]
        int GetInterface(in Guid iid, out nint p);
    }

    // combase HSTRING + activation factory helpers（保留 HRESULT，显式检查）
    [DllImport("combase.dll")]
    public static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out nint hstring);

    [DllImport("combase.dll")]
    public static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll")]
    public static extern int RoGetActivationFactory(
        nint activatableClassId, in Guid iid, out nint factory);
}
