using Microsoft.Extensions.Logging;
using Novashot.Core.Clipboard;
using Novashot.Platform.Windows.Interop;
using SkiaSharp;

namespace Novashot.Platform.Windows.Clipboard;

/// <summary>
/// Win32 剪贴板实现（M6c）。写入：DibCodec 转 CF_DIBV5 + CF_DIB 两份载荷同放，
/// 兼容新旧读者（Office/Chrome/聊天软件各自挑认得的格式）；
/// B-1 起像素 ≤4MP（I-1 阈值）时另尽力放注册格式 "PNG"（只认 PNG 字节的读者如 Figma 等；
/// 且 PNG 保 alpha，DIB 按 M6c 定案拍平不透明），超界只放 DIB 双格式。
/// 读取：CF_DIBV5 优先，CF_DIB 兜底；B-1 起再回退注册格式 "PNG"（Chrome/Electron 应用常只放 PNG），
/// 优先序由 Core <see cref="ClipboardImageFormats.SelectReadOrder"/> 决策表给出，畸形载荷解码失败继续试下一候选。
/// 所有 Win32 句柄失误路径都要释放 GlobalFree / CloseClipboard，不留泄漏。
/// </summary>
public sealed class WindowsClipboardService : IClipboardService
{
    private readonly ILogger<WindowsClipboardService>? _logger;

    /// <summary>注册格式 "PNG" 的进程内 ID（同名注册恒同值）；0 = 注册失败，读写两侧均按不可用处理。</summary>
    private static readonly uint PngFormatId = User32.RegisterClipboardFormatW(User32.PngFormatName);

    public WindowsClipboardService(ILogger<WindowsClipboardService>? logger = null)
    {
        _logger = logger;
    }

    public bool TrySetImage(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.Width <= 0 || bitmap.Height <= 0) return false;

        // 载荷在打开剪贴板前准备好，缩短持锁时间。
        // PNG 尽力编码（B-1 写入侧裁决：DIBV5+DIB 是本操作成败判据，PNG 编码失败只降互操作不减功能）。
        // 带 I-1 像素阈值（≤4MP 才编码）：同步编码在 UI 线程，实测（Apple Silicon）
        // 1080p ~72ms / 4K ~278ms / 4K 噪声最坏 ~992ms，超界只放 DIB 双格式。
        byte[] dibV5 = DibCodec.ToDibV5(bitmap);
        byte[] dib = DibCodec.ToDib(bitmap);
        byte[]? png = PngFormatId != 0 && ClipboardImageFormats.ShouldAttemptInteropEncode(bitmap.Width, bitmap.Height)
            ? PngCodec.Encode(bitmap)
            : null;

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
            if (!ok) return false;

            // B-1："PNG" 尽力追加，放置失败不回滚（DIB 双格式已就位，老读者无损）
            if (png is not null && !PutData(PngFormatId, png))
                _logger?.LogWarning("SetClipboardData(PNG) failed, DIB formats remain available");
            return true;
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

    /// <summary>写 CF_UNICODETEXT（UTF-16LE + 结尾 null）。M7b：复制上传 URL。</summary>
    public bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        byte[] payload = System.Text.Encoding.Unicode.GetBytes(text + "\0");
        if (!User32.OpenClipboard(nint.Zero))
        {
            _logger?.LogWarning("OpenClipboard failed (error {Error}) for text", System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            return false;
        }
        try
        {
            if (!User32.EmptyClipboard()) return false;
            return PutData(User32.CF_UNICODETEXT, payload);
        }
        finally
        {
            User32.CloseClipboard();
        }
    }

    public SKBitmap? TryGetImage()
    {
        // 候选序 = Core 决策表（B-1：CF_DIBV5 保真 → CF_DIB 兜底 → 注册格式 "PNG"）。
        // 可用性探测在持锁外做（OpenClipboard 前判断，持锁时间最短）
        var candidates = ClipboardImageFormats.SelectReadOrder(
            dibV5Available: User32.IsClipboardFormatAvailable(User32.CF_DIBV5),
            dibAvailable: User32.IsClipboardFormatAvailable(User32.CF_DIB),
            pngAvailable: PngFormatId != 0 && User32.IsClipboardFormatAvailable(PngFormatId));

        foreach (ClipboardImageFormat candidate in candidates)
        {
            uint format = candidate switch
            {
                ClipboardImageFormat.DibV5 => User32.CF_DIBV5,
                ClipboardImageFormat.Dib => User32.CF_DIB,
                _ => PngFormatId,
            };
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
                    // B-1：畸形载荷解码失败继续试下一候选（M6c 原实现在此整体 null 收场）
                    SKBitmap? decoded = candidate == ClipboardImageFormat.Png
                        ? PngCodec.Decode(payload)
                        : DibCodec.FromDib(payload);
                    if (decoded is not null) return decoded;
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

    /// <summary>
    /// G-11（winshot clipboard.go）：CF_HDROP 文件列表里找第一个图片扩展名的文件。
    /// 资源管理器“复制文件”后可直接粘贴进编辑器。
    /// </summary>
    public string? TryGetImageFilePath()
    {
        if (!User32.IsClipboardFormatAvailable(User32.CF_HDROP)) return null;
        if (!User32.OpenClipboard(nint.Zero)) return null;
        try
        {
            nint hDrop = User32.GetClipboardData(User32.CF_HDROP);
            if (hDrop == nint.Zero) return null;

            // DragQueryFileW(0xFFFFFFFF) 返回文件数；逐个取路径，遇到图片扩展名即返回
            uint count = (uint)User32.DragQueryFileW(hDrop, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < count; i++)
            {
                uint len = (uint)User32.DragQueryFileW(hDrop, i, null, 0);
                if (len == 0) continue;
                var sb = new System.Text.StringBuilder((int)len + 1);
                if (User32.DragQueryFileW(hDrop, i, sb, (uint)sb.Capacity) == 0) continue;
                var path = sb.ToString();
                if (IsImageExtension(Path.GetExtension(path))) return path;
            }
            return null;
        }
        finally
        {
            User32.CloseClipboard();
        }
    }

    /// <summary>图片扩展名判定（winshot 对齐；I-1 起与 Ctrl+O 文件选择器共用 Core 真值）。</summary>
    internal static bool IsImageExtension(string? extension) =>
        Novashot.Core.Utils.ImageFileTypes.IsSupported(extension);
}
