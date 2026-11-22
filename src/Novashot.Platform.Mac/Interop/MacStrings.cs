using System.Runtime.InteropServices;

namespace Novashot.Platform.Mac.Interop;

/// <summary>
/// 托管字符串 → 需要驻留的 UTF-8 缓冲（objc_msgSend string 参数用）。
/// </summary>
internal static class MacStrings
{
    /// <summary>调用方负责 CoTaskFree。</summary>
    internal static nint ToNative(string s) => Marshal.StringToCoTaskMemUTF8(s);

    internal static void Free(nint p) => Marshal.FreeCoTaskMem(p);
}
