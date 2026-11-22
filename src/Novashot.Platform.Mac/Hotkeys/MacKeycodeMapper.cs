using Novashot.Platform.Mac.Interop;

namespace Novashot.Platform.Mac.Hotkeys;

/// <summary>
/// Windows VK code（Core HotkeyParser 产出）→ Carbon virtual keycode。
/// Mac 键盘没有 PrintScreen 键：约定映射到 F13（Windows 兼容键盘 F13 物理存在；无 F13 时用户可在 config 换键）。
/// 参考 Carbon Event Manager 的 ANSI keycode 表。
/// </summary>
public static class MacKeycodeMapper
{
    /// <summary>映射失败返回 null（该键在 Mac 无对应，调用方记 warning 跳过注册）。</summary>
    public static ushort? VkToKeycode(uint vk) => vk switch
    {
        // 注意：Core HotkeyParser 的 0x2C 唯一对应 PrintScreen（VK_SNAPSHOT），OEM 逗号另用 0xBC
        0x2C => 0x69, // PrintScreen → F13（Mac 无 PrintScreen 键的约定）
        0x29 => 0x6B, // Select → F15（近似）
        0x2A => 0x71, // Execute → F14（近似）

        // 字母（ANSI keycode 顺 QWERTY 行）
        0x41 => 0x00, // A
        0x53 => 0x01, // S
        0x44 => 0x02, // D
        0x46 => 0x03, // F
        0x48 => 0x04, // H
        0x47 => 0x05, // G
        0x5A => 0x06, // Z
        0x58 => 0x07, // X
        0x43 => 0x08, // C
        0x56 => 0x09, // V
        0x42 => 0x0B, // B
        0x51 => 0x0C, // Q
        0x57 => 0x0D, // W
        0x45 => 0x0E, // E
        0x52 => 0x0F, // R
        0x59 => 0x10, // Y
        0x54 => 0x11, // T
        0x4F => 0x1F, // O
        0x55 => 0x20, // U
        0x49 => 0x22, // I
        0x50 => 0x23, // P
        0x4C => 0x25, // L
        0x4A => 0x26, // J
        0x4B => 0x28, // K
        0x4E => 0x2D, // N
        0x4D => 0x2E, // M

        // 数字
        0x31 => 0x12, // 1
        0x32 => 0x13, // 2
        0x33 => 0x14, // 3
        0x34 => 0x15, // 4
        0x35 => 0x17, // 5
        0x36 => 0x16, // 6
        0x37 => 0x1A, // 7
        0x38 => 0x1C, // 8
        0x39 => 0x19, // 9
        0x30 => 0x1D, // 0

        // OEM 与符号（Windows VK_OEM_* 才是真正的符号键；单字符已由 HotkeyParser 归到字母/数字）
        0xBB => 0x18, // VK_OEM_PLUS → =
        0xBC => 0x2B, // VK_OEM_COMMA → ,
        0xBD => 0x1B, // VK_OEM_MINUS → -
        0xBE => 0x2F, // VK_OEM_PERIOD → /
        0xBA => 0x29, // VK_OEM_1 → ;
        0xDB => 0x21, // VK_OEM_4 → [
        0xDD => 0x1E, // VK_OEM_6 → ]
        0xBF => 0x2C, // VK_OEM_2 → /
        0xDC => 0x2A, // VK_OEM_5 → \
        0xDE => 0x27, // VK_OEM_7 → '
        0xC0 => 0x32, // VK_OEM_3 → `
        0xE2 => 0x32, // VK_OEM_102 → 非 US `\`

        // 控制/编辑键
        0x20 => 0x31, // Space
        0x0D => 0x24, // Enter/Return
        0x1B => 0x35, // Escape
        0x09 => 0x30, // Tab
        0x08 => 0x33, // Backspace
        0x2E => 0x75, // Delete（向前删除）
        0x2D => 0x72, // Insert → Help
        0x24 => 0x73, // Home
        0x23 => 0x77, // End
        0x21 => 0x74, // PageUp
        0x22 => 0x79, // PageDown
        0x25 => 0x7B, // Left
        0x26 => 0x7E, // Up
        0x27 => 0x7C, // Right
        0x28 => 0x7D, // Down

        // F1–F24（Mac keycode 不连续，标准 ANSI 表）
        0x70 => 0x7A, // F1
        0x71 => 0x78, // F2
        0x72 => 0x63, // F3
        0x73 => 0x76, // F4
        0x74 => 0x60, // F5
        0x75 => 0x61, // F6
        0x76 => 0x62, // F7
        0x77 => 0x64, // F8
        0x78 => 0x65, // F9
        0x79 => 0x6D, // F10
        0x7A => 0x67, // F11
        0x7B => 0x6F, // F12
        0x7C => 0x69, // F13
        0x7D => 0x6B, // F14
        0x7E => 0x71, // F15

        _ => null,
    };

    /// <summary>Win32 MOD_* 位 → Carbon modifier 位。</summary>
    public static uint ModsToCarbon(uint mods)
    {
        var carbon = 0u;
        if ((mods & 0x1) != 0) carbon |= MacNative.optionKey;  // MOD_ALT
        if ((mods & 0x2) != 0) carbon |= MacNative.controlKey; // MOD_CONTROL
        if ((mods & 0x4) != 0) carbon |= MacNative.shiftKey;   // MOD_SHIFT
        if ((mods & 0x8) != 0) carbon |= MacNative.cmdKey;     // MOD_WIN
        return carbon;
    }
}
