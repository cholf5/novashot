using Microsoft.Extensions.Logging;
using Prtsc.Core.Clipboard;
using Prtsc.Platform.Windows.Interop;
using SkiaSharp;

namespace Prtsc.Platform.Windows.Clipboard;

/// <summary>
/// Win32 剪贴板实现（M6c）。写入：DibCodec 转 CF_DIBV5 + CF_DIB 两份载荷同放，
/// 兼容新旧读者（Office/Chrome/聊天软件各自挑认得的格式）。读取：CF_DIBV5 优先，CF_DIB 兜底。
/// 所有 Win32 句柄失误路径都要释放 GlobalFree / CloseClipboard，不留泄漏。
/// </summary>
public sealed class WindowsClipboardService : IClipboardService
{
    private readonly ILogger<WindowsClipboardService>? _logger;

    public WindowsClipboardService(ILogger<WindowsClipboardService>? logger = null)
    {
        _logger = logger;
    }

    public bool TrySetImage(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.Width <= 0 || bitmap.Height <= 0) return false;

        // 载荷在打开剪贴板前准备好，缩短持锁时间
        byte[] dibV5 = DibCodec.ToDibV5(bitmap);
        byte[] dib = DibCodec.ToDib(bitmap);

        if (!User32.OpenClipboard(nint.Zero))
        {
            _logger?.LogWarning("OpenClipboard failed (error {Error}), another app may hold it", System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            return false;
        }

        try
        {
            if (!User32.EmptyClipboard())
            {
                _logger?.LogWarning("EmptyClipboard failed (error {Error})", System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                return false;
            }

            bool ok = PutData(User32.CF_DIBV5, dibV5) && PutData(User32.CF_DIB, dib);
            return ok;
        }
        finally
        {
            User32.CloseClipboard();
        }
    }

    private bool PutData(uint format, byte[] payload)
    {
        nint hMem = Kernel32.GlobalAlloc(User32.GMEM_MOVEABLE, payload.Length);
        if (hMem == nint.Zero)
        {
            _logger?.LogWarning("GlobalAlloc({Bytes}) failed for clipboard format {Format}", payload.Length, format);
            return false;
        }

        nint ptr = Kernel32.GlobalLock(hMem);
        if (ptr == nint.Zero)
        {
            Kernel32.GlobalFree(hMem);
            return false;
        }
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(payload, 0, ptr, payload.Length);
        }
        finally
        {
            Kernel32.GlobalUnlock(hMem);
        }

        // 成功后 hMem 归系统所有，不得再 GlobalFree
        nint prev = User32.SetClipboardData(format, hMem);
        if (prev == nint.Zero)
        {
            Kernel32.GlobalFree(hMem);
            _logger?.LogWarning("SetClipboardData(CF format {Format}) failed", format);
            return false;
        }
        return true;
    }

    public SKBitmap? TryGetImage()
    {
        // CF_DIBV5 优先（保真），CF_DIB 兜底（老读者）
        uint[] formats = [User32.CF_DIBV5, User32.CF_DIB];
        foreach (uint format in formats)
        {
            if (!User32.IsClipboardFormatAvailable(format)) continue;
            if (!User32.OpenClipboard(nint.Zero)) return null;
            try
            {
                nint hMem = User32.GetClipboardData(format);
                if (hMem == nint.Zero) continue;

                nint ptr = Kernel32.GlobalLock(hMem);
                if (ptr == nint.Zero) continue;
                try
                {
                    nint size = Kernel32.GlobalSize(hMem);
                    if (size <= 0) continue;
                    var payload = new byte[size];
                    System.Runtime.InteropServices.Marshal.Copy(ptr, payload, 0, (int)size);
                    return DibCodec.FromDib(payload);
                }
                finally
                {
                    Kernel32.GlobalUnlock(hMem);
                }
            }
            finally
            {
                User32.CloseClipboard();
            }
        }
        return null;
    }
}
