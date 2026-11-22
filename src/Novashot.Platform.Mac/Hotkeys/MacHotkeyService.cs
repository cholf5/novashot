using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Novashot.Core.Hotkeys;
using Novashot.Platform.Mac.Interop;

namespace Novashot.Platform.Mac.Hotkeys;

/// <summary>
/// Carbon RegisterEventHotKey 实现（无需辅助功能权限）。
/// InstallEventHandler 常驻全局回调 → 统一截图热键（意图唯一）触发 HotkeyPressed。
/// 事件回调在 Carbon 主事件循环线程；Mac 无独立消息泵需求（Avalonia 主线程 runloop 即是），
/// 注册直接在调用线程同步执行。
/// </summary>
public sealed unsafe class MacHotkeyService : IHotkeyService
{
    internal const uint SignatureNovashot = 0x50525453; // 'PRTS'

    /// <summary>统一截图热键的 hotKeyID.id（Register 与回调 trampoline 共用，防两处漂移）。</summary>
    private const uint CaptureHotkeyId = 1;

    private readonly ILogger<MacHotkeyService> _logger;
    private readonly Dictionary<uint, nint> _hotkeyRefs = []; // id → EventHotKeyRef
    private nint _handlerRef;
    private bool _started;
    private GCHandle _selfHandle; // 回调 userData 保活

    public event EventHandler? HotkeyPressed;

    public MacHotkeyService(ILogger<MacHotkeyService> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        // eventType 只订 hotkey pressed（released 不关心）
        var spec = new MacNative.EventTypeSpec
        {
            eventClass = MacNative.kEventClassKeyboard,
            eventKind = MacNative.kEventHotKeyPressed,
        };
        _selfHandle = GCHandle.Alloc(this);
        var self = GCHandle.ToIntPtr(_selfHandle);

        var err = MacNative.InstallEventHandler(
            MacNative.GetApplicationEventTarget(),
            &EventHandlerTrampoline,
            1,
            &spec,
            (void*)self,
            out _handlerRef);
        if (err != 0)
        {
            _selfHandle.Free();
            _started = false;
            throw new InvalidOperationException($"InstallEventHandler failed: {err}");
        }
        _logger.LogDebug("Carbon hotkey handler installed");
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;

        UnregisterAll();
        if (_handlerRef != nint.Zero)
        {
            MacNative.RemoveEventHandler(_handlerRef);
            _handlerRef = nint.Zero;
        }
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    public void Register(string spec)
    {
        if (!_started) throw new InvalidOperationException("Service not started");

        UnregisterAll();

        if (!HotkeyParser.TryParse(spec, out var mods, out var vk))
        {
            _logger.LogWarning("Invalid hotkey spec {Spec}", spec);
            return;
        }

        var keycode = MacKeycodeMapper.VkToKeycode(vk);
        if (keycode is null)
        {
            _logger.LogWarning("VK {Vk:X2} has no macOS keycode equivalent; skip hotkey '{Spec}'", vk, spec);
            return;
        }

        var hotkeyId = new MacNative.EventHotKeyID { signature = SignatureNovashot, id = CaptureHotkeyId };
        var err = MacNative.RegisterEventHotKey(keycode.Value, MacKeycodeMapper.ModsToCarbon(mods), hotkeyId,
            MacNative.GetApplicationEventTarget(), 0, out var hkRef);
        if (err != 0)
        {
            _logger.LogError("RegisterEventHotKey '{Spec}' failed: {Err}", spec, err);
            return;
        }
        _hotkeyRefs[CaptureHotkeyId] = hkRef;
        _logger.LogInformation("Capture hotkey registered: '{Spec}' (keycode 0x{Key:X2})", spec, keycode.Value);
    }

    private void UnregisterAll()
    {
        foreach (var hkRef in _hotkeyRefs.Values)
        {
            MacNative.UnregisterEventHotKey(hkRef);
        }
        _hotkeyRefs.Clear();
    }

    [UnmanagedCallersOnly]
    private static int EventHandlerTrampoline(nint handlerCallToken, nint evt, void* userData)
    {
        var handle = GCHandle.FromIntPtr((nint)userData);
        if (handle.Target is not MacHotkeyService svc) return EventNotHandledErr;

        // kEventParamDirectObject('----') / typeEventHotKeyID('hkid') → EventHotKeyID
        var err = MacNative.GetEventParameter(
            evt,
            KEventParamDirectObject,
            KTypeEventHotKeyId,
            out _,
            (nuint)sizeof(MacNative.EventHotKeyID),
            out _,
            out var hotKeyId);

        if (err != 0 || hotKeyId.signature != SignatureNovashot) return EventNotHandledErr;

        if (hotKeyId.id == CaptureHotkeyId) // 统一截图热键
        {
            svc.HotkeyPressed?.Invoke(svc, EventArgs.Empty);
        }
        return EventNotHandledErr; // 让系统默认处理继续（语义：事件已消费但返回 notHandled 以免吞键）
    }

    private const uint KEventParamDirectObject = 0x2D2D2D2D; // '----'
    private const uint KTypeEventHotKeyId = 0x686B6964;      // 'hkid'
    private const int EventNotHandledErr = -9868;

    public void Dispose() => Stop();
}
