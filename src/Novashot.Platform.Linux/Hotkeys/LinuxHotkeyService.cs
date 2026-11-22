using Microsoft.Extensions.Logging;
using Novashot.Core.Hotkeys;
using Novashot.Platform.Linux.Interop;

namespace Novashot.Platform.Linux.Hotkeys;

/// <summary>
/// XGrabKey 实现：专用线程事件泵（XNextEvent 阻塞在 X 连接 fd 上）→ KeyPress 按 keycode+state 反查动作。
/// 注册时把 keysym 解析成 keycode 需要 XKeycodeToKeysym——通过 IXLibApi 扩展（KeycodeToKeysym），
/// Fake 测试用 keycode 直注。modifier 记录原始 spec 解析值（Mod2Mask/LockMask 变体由 XLibApi.GrabKey 处理）。
/// </summary>
public sealed class LinuxHotkeyService : IHotkeyService
{
    private readonly IXLibApi _api;
    private readonly ILogger<LinuxHotkeyService> _logger;
    private readonly Dictionary<uint, HotkeyAction> _byKey = new(); // (keycode<<8|mods) → action 太粗，用 keycode+mods 精确键
    private readonly Dictionary<(uint Keycode, uint Mods), HotkeyAction> _bindings = [];
    private Thread? _pump;
    private CancellationTokenSource? _cts;
    private bool _started;

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public LinuxHotkeyService(IXLibApi api, ILogger<LinuxHotkeyService> logger)
    {
        _api = api;
        _logger = logger;
    }

    public void Start()
    {
        if (_started) return;
        if (!_api.Open())
            throw new InvalidOperationException("XOpenDisplay failed — is DISPLAY set and X server reachable?");

        var window = _api.CreateEventWindow();
        if (window == nint.Zero)
            throw new InvalidOperationException("XCreateSimpleWindow failed for hotkey event window");

        _cts = new CancellationTokenSource();
        _pump = new Thread(() => PumpLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "novashot-x11-hotkeys",
        };
        _pump.Start();
        _started = true;
        _logger.LogDebug("X11 hotkey pump started (event window 0x{Window:X})", window);
    }

    public void RegisterAll(string fullscreen, string region, string window)
    {
        if (!_started)
            throw new InvalidOperationException("Start() must be called before RegisterAll");

        // S-1（review I3）：会有多次 RegisterAll——先解上一轮的抓键，否则旧组合继续触发
        var eventWindow = _api.EventWindowHandle;
        foreach (var (keycode, mods) in _bindings.Keys)
            _api.UngrabKey(eventWindow, mods, keycode);
        _bindings.Clear();

        var specs = new (string Spec, HotkeyAction Action)[]
        {
            (fullscreen, HotkeyAction.Fullscreen),
            (region, HotkeyAction.Region),
            (window, HotkeyAction.Window),
        };

        var failures = new List<string>();
        foreach (var (spec, action) in specs)
        {
            if (!HotkeyParser.TryParse(spec, out var mods, out var vk))
            {
                failures.Add($"{spec} (parse failed)");
                continue;
            }
            var keysym = XKeysymMapper.VkToKeysym(vk);
            if (keysym is null)
            {
                failures.Add($"{spec} (no keysym for VK 0x{vk:X})");
                continue;
            }
            var keycode = _api.KeycodeToKeysym(keysym.Value);
            if (keycode == 0)
            {
                failures.Add($"{spec} (keysym 0x{keysym.Value:X} not on current keymap)");
                continue;
            }
            if (!_api.GrabKey(_api.EventWindowHandle, mods, keycode))
            {
                failures.Add($"{spec} (XGrabKey failed — already grabbed by another app?)");
                continue;
            }
            _bindings[(keycode, mods)] = action;
            _logger.LogInformation("Hotkey registered: {Spec} → keycode {Keycode} mods {Mods:X}", spec, keycode, mods);
        }

        if (failures.Count > 0)
            throw new InvalidOperationException("Some hotkeys failed to register: " + string.Join("; ", failures));
    }

    public void Stop()
    {
        if (!_started) return;
        _cts?.Cancel();
        _api.WakeEventLoop();
        _pump?.Join(2000);
        _pump = null;
        _cts?.Dispose();
        _cts = null;
        foreach (var (keycode, mods) in _bindings.Keys)
            _api.UngrabKey(_api.EventWindowHandle, mods, keycode);
        _bindings.Clear();
        _started = false;
    }

    public void Dispose() => Stop();

    private void PumpLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            (uint Keycode, uint Modifiers)? ev;
            try
            {
                ev = _api.NextKeyEvent(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "X11 hotkey pump crashed");
                return;
            }
            if (ev is not { } key) break; // 取消

            // NumLock(0x10)/CapsLock(0x2)/ScrollLock(0x80 在 mod3/mod5 外的 state 位) 掩掉再匹配
            var state = key.Modifiers & 0xFFu & ~(uint)(XLibNative.Mod2Mask | XLibNative.LockMask);
            if (_bindings.TryGetValue((key.Keycode, state), out var action))
            {
                _logger.LogDebug("Hotkey fired: {Action}", action);
                try
                {
                    HotkeyPressed?.Invoke(this, action);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "HotkeyPressed handler threw");
                }
            }
        }
    }
}
