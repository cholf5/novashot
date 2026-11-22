using Novashot.Core.Hotkeys;
using Microsoft.Extensions.Logging.Abstractions;
using Novashot.Platform.Linux.Hotkeys;
using Novashot.Platform.Linux.Interop;

namespace Novashot.Linux.Tests;

/// <summary>VK → keysym 映射（HotkeyParser 产出的全集）。</summary>
public class XKeysymMapperTests
{
    [Theory]
    [InlineData(0x2C, 0xFF61u)]   // PrintScreen → XK_Print
    [InlineData(0x41, 0x61u)]     // 'A' → 'a'
    [InlineData(0x5A, 0x7Au)]     // 'Z' → 'z'
    [InlineData(0x31, 0x31u)]     // '1'
    [InlineData(0x70, 0xFFBEu)]   // F1
    [InlineData(0x87, 0xFFD5u)]   // F24
    [InlineData(0x1B, 0xFF1Bu)]   // Esc
    [InlineData(0x25, 0xFF51u)]   // Left
    public void VkToKeysym_MapsKnownVks(uint vk, uint expected) =>
        Assert.Equal(expected, XKeysymMapper.VkToKeysym(vk));

    [Fact]
    public void VkToKeysym_UnknownVk_ReturnsNull() =>
        Assert.Null(XKeysymMapper.VkToKeysym(0xBA)); // OEM 分号不在支持集
}

/// <summary>
/// XGrabKey 注册/分发：合法 spec 注册成功并记 bindings；非法 spec 记日志不抛（与 Win/Mac 同契约）；
/// 事件泵 keycode+mods（NumLock/CapsLock 掩除）命中 → HotkeyPressed（统一截图热键，无动作负载）。
/// </summary>
public class LinuxHotkeyServiceTests
{
    private static (LinuxHotkeyService Svc, FakeXLibApi Api) Make()
    {
        var api = new FakeXLibApi();
        api.Open();
        var svc = new LinuxHotkeyService(api, NullLogger<LinuxHotkeyService>.Instance);
        return (svc, api);
    }

    [Fact]
    public void Register_GrabsKeyAndStoresBinding()
    {
        var (svc, api) = Make();
        svc.Start();
        // PrintScreen=0x2C → keysym 0xFF61；测试 keymap: 0xFF61 → keycode 107
        api.KeysymToKeycode[0xFF61] = 107;

        svc.Register("PrintScreen");

        var grab = Assert.Single(api.GrabbedKeys);
        Assert.Equal(107u, grab.Keycode);
        Assert.Equal((uint)0, grab.Modifiers);
        svc.Dispose();
    }

    [Fact]
    public void Register_UnparseableSpec_LogsAndSkips_NoThrow()
    {
        var (svc, api) = Make();
        svc.Start();

        svc.Register("Bad+Spec+Here"); // 契约：记日志不抛，不抓键

        Assert.Empty(api.GrabbedKeys);
        svc.Dispose();
    }

    [Fact]
    public void Register_BeforeStart_Throws()
    {
        var (svc, _) = Make();
        Assert.Throws<InvalidOperationException>(() => svc.Register("PrintScreen"));
        svc.Dispose();
    }

    [Fact]
    public async Task Pump_DispatchesHotkey_IgnoringNumLockCapsLock()
    {
        var (svc, api) = Make();
        svc.Start();
        api.KeysymToKeycode[0xFF61] = 107;
        svc.Register("PrintScreen");

        var received = new TaskCompletionSource<bool>(TaskCreationOptions.None);
        svc.HotkeyPressed += (_, _) => received.TrySetResult(true);

        // XEvent state 带 NumLock(0x10) 位——应被掩掉仍命中
        api.EmitKeyEvent(107, 0x10);

        await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        svc.Dispose();
    }

/// <summary>
/// S-1（review I3）：设置保存会二次 Register——必须先解上一轮 XGrabKey，
/// 否则旧组合继续触发（重复截图 + 全局吞键到进程退出）。
/// </summary>
[Fact]
public void Register_Twice_UngrabsPreviousBindings()
{
    var (svc, api) = Make();
    svc.Start();
    api.KeysymToKeycode[0xFF61] = 107;

    svc.Register("PrintScreen");
    var grabsAfterFirst = api.GrabbedKeys.Count;

    api.KeysymToKeycode[0xFFC6] = 115; // XK_F9 也要在 fake keymap 里
    svc.Register("F9");

    Assert.True(api.UngrabbedKeys.Count >= grabsAfterFirst,
        $"第二次 Register 必须先解上一轮 {grabsAfterFirst} 个抓键，实际解了 {api.UngrabbedKeys.Count}");
    Assert.All(api.UngrabbedKeys, u => Assert.Equal(107u, u.Keycode));
    svc.Dispose();
}
}
