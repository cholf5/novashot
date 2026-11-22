using System.Runtime.InteropServices;
using Novashot.Platform.Mac.Interop;
using Xunit;

namespace Novashot.Mac.Tests;

/// <summary>
/// CFStringToString 纯 CF 语义回归（mac 冒烟 bug #4 / 坑 12）：
/// 旧实现走 objc_msgSend(cfstr, ObjcIds.Utf8StringSel)，而 ObjcIds.EnsureLoaded 只在剪贴板/overlay
/// 路径触发；窗口枚举（WindowPickerDialog.Reload → CGWindowListApi.RowFromDict）若是进程里第一个
/// 调用方，selector 为 0 → objc_msgSend NULL sel → 空 IMP → pc=0x0 SIGSEGV。
/// 修复后 CFStringToString 走纯 CF（CFStringGetCString），不依赖任何 ObjC 运行时状态。
/// 用 --filter 单独跑本类（新测试宿主，ObjcIds 未加载）才能体现旧行为的红。
/// </summary>
public class MacNativeStringTests
{
    private static nint CreateCFString(string s)
    {
        var tmp = Marshal.StringToCoTaskMemUTF8(s);
        try
        {
            return MacNative.CFStringCreateWithCString(nint.Zero, tmp, 0x08000100 /* kCFStringEncodingUTF8 */);
        }
        finally
        {
            Marshal.FreeCoTaskMem(tmp);
        }
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("窗口标题")]
    [InlineData("Safari — GitHub · Build 🎉")]
    public void CFStringToString_Roundtrip(string s)
    {
        var cf = CreateCFString(s);
        try
        {
            Assert.Equal(s, MacNative.CFStringToString(cf));
        }
        finally
        {
            MacNative.CFRelease(cf);
        }
    }

    [Fact]
    public void CFStringToString_Null_ReturnsEmpty() =>
        Assert.Equal(string.Empty, MacNative.CFStringToString(nint.Zero));
}
