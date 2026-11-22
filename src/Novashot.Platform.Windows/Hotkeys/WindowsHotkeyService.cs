using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Novashot.Core.Hotkeys;
using Novashot.Platform.Windows.Interop;

namespace Novashot.Platform.Windows.Hotkeys;

/// <summary>
/// 消息专用窗口 + RegisterHotKey 实现。消息泵跑在专属 STA 线程，
/// 通过 Post 到该窗口注册/注销热键，事件回主线程通过 SynchronizationContext（或用户手动 marshal）。
/// </summary>
public sealed class WindowsHotkeyService : IHotkeyService
{
    private const string WndClassName = "NovashotHotkeySink";
    private const uint WM_APP_REGISTER = 0x8001;
    private const uint WM_APP_UNREGISTER_ALL = 0x8002;
    private const uint WM_APP_QUIT = 0x8003;

    private readonly ILogger<WindowsHotkeyService> _logger;
    private readonly ConcurrentDictionary<int, HotkeyAction> _idToAction = new();
    // 仅泵线程（WndProc）读写：真正拿到 OS 注册成功的 id 集合。
    // C1（review 2026-11-02）：解绑必须以此为准——_idToAction 在调用线程被 Clear/重建，
    // 泵侧若按它解绑会把旧注册永久留在 OS 里，新 RegisterHotKey 全部撞 "already registered"。
    private readonly HashSet<int> _registeredIds = new();
    private readonly SynchronizationContext? _syncContext;
    private Thread? _thread;
    private nint _hwnd;
    private User32.WndProc? _wndProcDelegate; // 保活防 GC
    private int _nextId = 1;
    private volatile bool _running;

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public WindowsHotkeyService(ILogger<WindowsHotkeyService> logger)
    {
        _logger = logger;
        _syncContext = SynchronizationContext.Current;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        var ready = new ManualResetEventSlim();
        _thread = new Thread(() => ThreadProc(ready))
        {
            IsBackground = true,
            Name = "NovashotHotkeyPump"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        if (_hwnd != nint.Zero)
            User32.PostMessageW(_hwnd, WM_APP_QUIT, 0, 0);
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _hwnd = nint.Zero;
    }

    public void RegisterAll(string fullscreen, string region, string window)
    {
        if (!_running) throw new InvalidOperationException("Service not started");

        // 先解绑全部
        User32.PostMessageW(_hwnd, WM_APP_UNREGISTER_ALL, 0, 0);
        _idToAction.Clear();

        RegisterOne(HotkeyAction.Fullscreen, fullscreen);
        RegisterOne(HotkeyAction.Region, region);
        RegisterOne(HotkeyAction.Window, window);
    }

    private void RegisterOne(HotkeyAction action, string spec)
    {
        if (!HotkeyParser.TryParse(spec, out var mods, out var vk))
        {
            _logger.LogWarning("Invalid hotkey spec {Spec} for {Action}", spec, action);
            return;
        }
        var id = Interlocked.Increment(ref _nextId);
        _idToAction[id] = action;
        // 消息线程中调用 RegisterHotKey，避免 hwnd 所有权跨线程
        User32.PostMessageW(_hwnd, WM_APP_REGISTER, id, (nint)((long)vk << 32 | mods));
    }

    private void ThreadProc(ManualResetEventSlim ready)
    {
        try
        {
            _wndProcDelegate = WndProc;
            var hInstance = Kernel32.GetModuleHandleW(null);

            var cls = new User32.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<User32.WNDCLASSEXW>(),
                lpfnWndProc = _wndProcDelegate,
                hInstance = hInstance,
                lpszClassName = WndClassName,
            };

            // 允许重复注册同名 class
            var existing = new User32.WNDCLASSEXW { cbSize = (uint)Marshal.SizeOf<User32.WNDCLASSEXW>() };
            if (User32.GetClassInfoExW(hInstance, WndClassName, ref existing) == 0)
                User32.RegisterClassExW(ref cls);

            _hwnd = User32.CreateWindowExW(
                dwExStyle: 0,
                lpClassName: WndClassName,
                lpWindowName: null,
                dwStyle: 0,
                x: 0, y: 0, nWidth: 0, nHeight: 0,
                hWndParent: unchecked((nint)(-3)), // HWND_MESSAGE
                hMenu: nint.Zero,
                hInstance: hInstance,
                lpParam: nint.Zero);

            if (_hwnd == nint.Zero)
                throw new InvalidOperationException($"CreateWindowEx failed, err={Marshal.GetLastWin32Error()}");

            ready.Set();

            while (_running)
            {
                var r = User32.GetMessage(out var msg, nint.Zero, 0, 0);
                if (r <= 0) break;
                User32.TranslateMessage(ref msg);
                User32.DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hotkey pump crashed");
            ready.Set();
        }
        finally
        {
            if (_hwnd != nint.Zero)
            {
                foreach (var id in _registeredIds)
                    User32.UnregisterHotKey(_hwnd, id);
                User32.DestroyWindow(_hwnd);
            }
        }
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case User32.WM_HOTKEY:
                if (_idToAction.TryGetValue((int)wParam, out var action))
                    RaiseHotkey(action);
                return 0;

            case WM_APP_REGISTER:
                {
                    var id = (int)wParam;
                    var packed = (long)lParam;
                    var vk = (uint)(packed >> 32);
                    var mods = (uint)(packed & 0xFFFFFFFF);
                    if (!User32.RegisterHotKey(hWnd, id, mods | User32.MOD_NOREPEAT, vk))
                    {
                        _logger.LogWarning("RegisterHotKey failed id={Id} mods={Mods:X} vk={Vk:X} err={Err}",
                            id, mods, vk, Marshal.GetLastWin32Error());
                        _idToAction.TryRemove(id, out _);
                    }
                    else
                    {
                        _registeredIds.Add(id);
                        _logger.LogInformation("Hotkey registered id={Id} mods={Mods:X} vk={Vk:X}", id, mods, vk);
                    }
                    return 0;
                }

            case WM_APP_UNREGISTER_ALL:
                foreach (var id in _registeredIds)
                    User32.UnregisterHotKey(hWnd, id);
                _registeredIds.Clear();
                return 0;

            case WM_APP_QUIT:
                User32.PostQuitMessage(0);
                return 0;

            case User32.WM_DESTROY:
                User32.PostQuitMessage(0);
                return 0;
        }
        return User32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void RaiseHotkey(HotkeyAction action)
    {
        _logger.LogInformation("Hotkey pressed: {Action}", action);
        if (_syncContext != null)
        {
            _syncContext.Post(_ => HotkeyPressed?.Invoke(this, action), null);
        }
        else
        {
            HotkeyPressed?.Invoke(this, action);
        }
    }

    public void Dispose() => Stop();
}
