using Prtsc.Platform.Linux.Interop;

namespace Prtsc.Linux.Tests;

/// <summary>
/// Fake X11 API：显示器/像素由测试注入（每屏一色），验证 LinuxCaptureService 的合成/偏移语义。
/// GetImage 返回区域子采样：测试注入整虚拟桌面大图，按请求矩形返回同色（简化：矩形内全色）。
/// </summary>
internal sealed class FakeXLibApi : IXLibApi
{
    public bool Opened { get; private set; }
    public List<XDisplayRaw> Displays { get; set; } = [];

    /// <summary>按全局虚拟桌面坐标 (x, y) 给色。默认全黑。</summary>
    public Func<int, int, uint> PixelSource { get; set; } = (_, _) => 0xFF000000;

    public List<nint> Clients { get; set; } = [];
    public Dictionary<nint, XWindowInfo> WindowInfos { get; } = new();
    public List<(nint Window, uint Modifiers, uint Keycode)> GrabbedKeys { get; } = [];
    public nint EventWindowHandle { get; private set; }
    public bool Activated { get; private set; }

    public event EventHandler<string>? XError;

    public bool Open() { Opened = true; return true; }
    public void Close() => Opened = false;
    public nint RootWindow => Opened ? (nint)0x1000 : nint.Zero;
    public (int Width, int Height) GetRootSize() => Opened ? (3840, 2160) : (0, 0);
    public int GetScreenDepth() => Opened ? 24 : 0;
    public IReadOnlyList<XDisplayRaw> EnumerateDisplays() => Displays;

    public IXImage? GetImage(int x, int y, int width, int height)
    {
        if (!Opened || width <= 0 || height <= 0) return null;
        // 逐像素回调按全局虚拟桌面坐标给色（GetImage 语义 = 根窗上的绝对矩形）
        return new FakeImage(width, height, (lx, ly) => PixelSource(lx + x, ly + y));
    }

    public nint[] ClientWindows() => [.. Clients];

    public XWindowInfo? QueryWindow(nint window) =>
        WindowInfos.TryGetValue(window, out var info) ? info : null;

    public int WindowPid(nint window) =>
        WindowInfos.TryGetValue(window, out var info) ? info.GetHashCode() % 30000 : 0;

    public nint ActiveWindow() =>
        WindowInfos.FirstOrDefault(kv => kv.Value.IsMapped).Key;

    public bool ActivateWindow(nint window)
    {
        if (!WindowInfos.ContainsKey(window)) return false;
        Activated = true;
        return true;
    }

    public IXImage? GetWindowImage(nint window, int width, int height)
    {
        if (!WindowInfos.ContainsKey(window)) return null;
        return new FakeImage(width, height, (_, _) => 0xFF3366CC);
    }

    public nint CreateEventWindow() { EventWindowHandle = (nint)0x2000; return EventWindowHandle; }


    /// <summary>keysym → 注入的 keycode 表（测试直配）。</summary>
    public Dictionary<uint, uint> KeysymToKeycode { get; } = new();

    public uint KeycodeToKeysym(uint keysym) => KeysymToKeycode.TryGetValue(keysym, out var kc) ? kc : 0;

    public bool GrabKey(nint eventWindow, uint modifiers, uint keycode)
    {
        GrabbedKeys.Add((eventWindow, modifiers, keycode));
        return true;
    }

    public List<(nint Window, uint Modifiers, uint Keycode)> UngrabbedKeys { get; } = new();

    public void UngrabKey(nint eventWindow, uint modifiers, uint keycode)
        => UngrabbedKeys.Add((eventWindow, modifiers, keycode));

    public (uint Keycode, uint Modifiers)? NextKeyEvent(CancellationToken ct)
    {
        // Fake 不走 self-pipe：直接从注入队列取（取消时抛 OperationCanceled 由上层处理）
        try
        {
            var (kc, mods) = _events.Take(ct);
            return (kc, mods);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public void WakeEventLoop() { }

    // ---- 测试注入：模拟 X 服务发来 KeyPress ----

    private readonly System.Collections.Concurrent.BlockingCollection<(uint, uint)> _events = new();

    public void EmitKeyEvent(uint keycode, uint modifiers) => _events.Add((keycode, modifiers));

    public void Dispose() { }
}

internal sealed class FakeImage : IXImage
{
    private readonly Func<int, int, uint> _pixel;

    public FakeImage(int width, int height, Func<int, int, uint> pixel)
    {
        Width = width;
        Height = height;
        _pixel = pixel;
    }

    public int Width { get; }
    public int Height { get; }
    public uint GetPixel(int x, int y) => _pixel(x, y);
    public void Dispose() { }
}
