using Novashot.Core.Hotkeys;
using Novashot.Platform.Linux.Interop;

namespace Novashot.Platform.Linux.Hotkeys;

/// <summary>
/// Windows VK code（Core HotkeyParser 产出）→ X11 keysym。
/// X11 键盘有 PrintScreen 键（keysym Print = 0xFF61），无需近似映射。
/// 只列 HotkeyParser 能产出的键（字母/数字/F1-24/方向/特殊键）。
/// </summary>
public static class XKeysymMapper
{
    /// <summary>XK_Print 与 XK_3270_Print 之外的标准 keysym 值（X11 keysymdef.h）。</summary>
    private const uint XkPrint = 0xFF61;
    private const uint XkSpace = 0x0020;
    private const uint XkReturn = 0xFF0D;
    private const uint XkEnter = 0xFF8D;   // KP_Enter
    private const uint XkTab = 0xFF09;
    private const uint XkEscape = 0xFF1B;
    private const uint XkBackspace = 0xFF08;
    private const uint XkDelete = 0xFFFF;
    private const uint XkInsert = 0xFF63;
    private const uint XkHome = 0xFF50;
    private const uint XkEnd = 0xFF57;
    private const uint XkPageUp = 0xFF55;
    private const uint XkPageDown = 0xFF56;
    private const uint XkLeft = 0xFF51;
    private const uint XkUp = 0xFF52;
    private const uint XkRight = 0xFF53;
    private const uint XkDown = 0xFF54;
    private const uint XkF1 = 0xFFBE;      // F1..F35 连续

    /// <summary>映射失败返回 null（调用方记 warning 跳过注册）。</summary>
    public static uint? VkToKeysym(uint vk) => vk switch
    {
        0x2C => XkPrint,          // PrintScreen
        0x20 => XkSpace,
        0x0D => XkReturn,
        0x09 => XkTab,
        0x1B => XkEscape,
        0x08 => XkBackspace,
        0x2E => XkDelete,
        0x2D => XkInsert,
        0x24 => XkHome,
        0x23 => XkEnd,
        0x21 => XkPageUp,
        0x22 => XkPageDown,
        0x25 => XkLeft,
        0x26 => XkUp,
        0x27 => XkRight,
        0x28 => XkDown,
        >= 0x70 and <= 0x87 => XkF1 + (vk - 0x70),  // F1..F24
        >= 0x41 and <= 0x5A => vk + 0x20,            // 'A'-'Z' → 'a'-'z'（X11 小写 keysym）
        >= 0x30 and <= 0x39 => vk,                   // '0'-'9' 同值
        _ => null,
    };
}
