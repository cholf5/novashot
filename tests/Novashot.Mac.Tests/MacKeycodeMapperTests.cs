using Novashot.Core.Hotkeys;
using Novashot.Platform.Mac.Hotkeys;
using Novashot.Platform.Mac.Interop;
using Xunit;

namespace Novashot.Mac.Tests;

public class MacKeycodeMapperTests
{
    // Windows VK → Mac keycode 关键映射（Mac 键盘上无 PrintScreen，约定映射到 F13）
    [Theory]
    [InlineData(0x2C, (ushort)0x69)] // PrintScreen → F13
    [InlineData(0x41, (ushort)0x00)] // A
    [InlineData(0x5A, (ushort)0x06)] // Z
    [InlineData(0x30, (ushort)0x1D)] // 0
    [InlineData(0x39, (ushort)0x19)] // 9
    [InlineData(0x70, (ushort)0x7A)] // F1
    [InlineData(0x7B, (ushort)0x6F)] // F12
    [InlineData(0x20, (ushort)0x31)] // Space
    [InlineData(0x0D, (ushort)0x24)] // Enter
    [InlineData(0x1B, (ushort)0x35)] // Escape
    [InlineData(0x09, (ushort)0x30)] // Tab
    [InlineData(0x08, (ushort)0x33)] // Backspace
    [InlineData(0x2E, (ushort)0x75)] // Delete
    [InlineData(0x2D, (ushort)0x72)] // Insert → Help
    [InlineData(0x24, (ushort)0x73)] // Home
    [InlineData(0x23, (ushort)0x77)] // End
    [InlineData(0x21, (ushort)0x74)] // PageUp
    [InlineData(0x22, (ushort)0x79)] // PageDown
    [InlineData(0x25, (ushort)0x7B)] // Left
    [InlineData(0x26, (ushort)0x7E)] // Up
    [InlineData(0x27, (ushort)0x7C)] // Right
    [InlineData(0x28, (ushort)0x7D)] // Down
    public void VkToKeycode_KnownKeys_MapCorrectly(uint vk, ushort expected)
    {
        Assert.Equal(expected, MacKeycodeMapper.VkToKeycode(vk));
    }

    [Fact]
    public void VkToKeycode_Unknown_ReturnsNull()
    {
        Assert.Null(MacKeycodeMapper.VkToKeycode(0xF0));
    }

    // MOD_* → Carbon modifiers
    [Fact]
    public void ModsToCarbon_MapsAll()
    {
        Assert.Equal(0u, MacKeycodeMapper.ModsToCarbon(0));
        Assert.Equal(MacNative.controlKey, MacKeycodeMapper.ModsToCarbon(0x2));  // MOD_CONTROL
        Assert.Equal(MacNative.shiftKey, MacKeycodeMapper.ModsToCarbon(0x4));    // MOD_SHIFT
        Assert.Equal(MacNative.optionKey, MacKeycodeMapper.ModsToCarbon(0x1));   // MOD_ALT
        Assert.Equal(MacNative.cmdKey, MacKeycodeMapper.ModsToCarbon(0x8));      // MOD_WIN
        Assert.Equal(MacNative.controlKey | MacNative.shiftKey, MacKeycodeMapper.ModsToCarbon(0x6));
        Assert.Equal(MacNative.cmdKey | MacNative.optionKey | MacNative.controlKey | MacNative.shiftKey,
                     MacKeycodeMapper.ModsToCarbon(0xF));
    }
}
