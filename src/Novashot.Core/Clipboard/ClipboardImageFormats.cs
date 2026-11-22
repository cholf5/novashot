namespace Novashot.Core.Clipboard;

/// <summary>剪贴板位图格式候选。Windows 平台层负责把成员映射到 Win32 格式 ID（CF_* 或 RegisterClipboardFormat）。</summary>
public enum ClipboardImageFormat
{
    DibV5,
    Dib,
    Png,
}

/// <summary>
/// B-1：剪贴板位图格式决策表（纯函数便于单测；平台层只做可用性探测与字节搬运）。
/// 读：CF_DIBV5 &gt; CF_DIB &gt; 注册格式 "PNG"——DIB 保真且按不透明语义传递（M6c 定案），
/// PNG 兜住只放 PNG 字节的浏览器/Electron 源（Chrome 右键复制图片等）。
/// </summary>
public static class ClipboardImageFormats
{
    /// <summary>按读取优先级返回当前可用的候选格式（只含探测到可用的成员；全不可用返回空）。</summary>
    public static IReadOnlyList<ClipboardImageFormat> SelectReadOrder(
        bool dibV5Available, bool dibAvailable, bool pngAvailable)
    {
        var order = new List<ClipboardImageFormat>(3);
        if (dibV5Available) order.Add(ClipboardImageFormat.DibV5);
        if (dibAvailable) order.Add(ClipboardImageFormat.Dib);
        if (pngAvailable) order.Add(ClipboardImageFormat.Png);
        return order;
    }

    /// <summary>
    /// 写侧互操作 PNG 编码像素阈值（I-1）。同步编码在 UI 线程，实测（Apple Silicon）：
    /// 1080p ~72ms / 4K ~278ms / 4K 噪声最坏 ~992ms——超过此界跳过 PNG 只放 DIB 双格式，
    /// 超大图互操作让位于交互延迟（裁决细节见 B-1 卡 Verification ③）。
    /// </summary>
    public const long InteropEncodeMaxPixels = 4_000_000;

    /// <summary>像素 ≤<see cref="InteropEncodeMaxPixels"/> 才同步编码 PNG（true）。乘积按 long 算防 int 溢出回绕。</summary>
    public static bool ShouldAttemptInteropEncode(int width, int height) =>
        (long)width * height <= InteropEncodeMaxPixels;
}
