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
/// XGrabKey 注册/分发：合法 spec 注册成功并记 bindings；解析失败聚合抛错；
/// 事件泵 keycode+mods（NumLock/CapsLock 掩除）命中 → HotkeyPressed。
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
    public void RegisterAll_GrabsKeysAndStoresBindings()
    {
        var (svc, api) = Make();
        svc.Start();
        // PrintScreen=0x2C → keysym 0xFF61；测试 keymap: 0xFF61 → keycode 107
        api.KeysymToKeycode[0xFF61] = 107;

        svc.RegisterAll("PrintScreen", "Ctrl+PrintScreen", "Ctrl+Shift+PrintScreen");

        Assert.Equal(3, api.GrabbedKeys.Count);
        Assert.All(api.GrabbedKeys, g => Assert.Equal(107u, g.Keycode));
        Assert.Equal((uint)0, api.GrabbedKeys[0].Modifiers);
        Assert.Equal((uint)0x2, api.GrabbedKeys[1].Modifiers);  // ControlMask
        Assert.Equal((uint)0x6, api.GrabbedKeys[2].Modifiers);  // Ctrl+Shift
        svc.Dispose();
    }

    [Fact]
    public void RegisterAll_UnparseableSpec_AggregatesFailure()
    {
        var (svc, api) = Make();
        svc.Start();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            svc.RegisterAll("F13", "Ctrl+PrintScreen", "Bad+Spec+Here"));
        Assert.Contains("parse failed", ex.Message);
        svc.Dispose();
    }

    [Fact]
    public void RegisterAll_BeforeStart_Throws()
    {
        var (svc, _) = Make();
        Assert.Throws<InvalidOperationException>(() => svc.RegisterAll("PrintScreen", "Ctrl+PrintScreen", "Ctrl+Shift+PrintScreen"));
        svc.Dispose();
    }

    [Fact]
    public async Task Pump_DispatchesHotkeyAction_IgnoringNumLockCapsLock()
    {
        var (svc, api) = Make();
        svc.Start();
        api.KeysymToKeycode[0xFF61] = 107;
        svc.RegisterAll("PrintScreen", "Ctrl+PrintScreen", "Ctrl+Shift+PrintScreen");

        var received = new TaskCompletionSource<HotkeyAction>(TaskCreationOptions.None);
        svc.HotkeyPressed += (_, a) => received.TrySetResult(a);

        // XEvent state 带 NumLock(0x10) 位——应被掩掉仍命中
        api.EmitKeyEvent(107, 0x10);

        var action = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(HotkeyAction.Fullscreen, action);
        svc.Dispose();
    }

/// <summary>
/// S-1（review I3）：设置保存会二次 RegisterAll——必须先解上一轮 XGrabKey，
/// 否则旧组合继续触发（重复截图 + 全局吞键到进程退出）。
/// </summary>
[Fact]
public void RegisterAll_Twice_UngrabsPreviousBindings()
{
    var (svc, api) = Make();
    svc.Start();
    api.KeysymToKeycode[0xFF61] = 107;

    svc.RegisterAll("PrintScreen", "Ctrl+PrintScreen", "Ctrl+Shift+PrintScreen");
    var grabsAfterFirst = api.GrabbedKeys.Count;

    api.KeysymToKeycode[0xFFC6] = 115; // XK_F9 也要在 fake keymap 里
    svc.RegisterAll("F9", "Ctrl+PrintScreen", "Ctrl+Shift+PrintScreen");

    Assert.True(api.UngrabbedKeys.Count >= grabsAfterFirst,
        $"第二次 RegisterAll 必须先解上一轮 {grabsAfterFirst} 个抓键，实际解了 {api.UngrabbedKeys.Count}");
    Assert.All(api.UngrabbedKeys, u => Assert.Equal(107u, u.Keycode));
    svc.Dispose();
}
}
