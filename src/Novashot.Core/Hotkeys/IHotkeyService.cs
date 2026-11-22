namespace Novashot.Core.Hotkeys;

public enum HotkeyAction
{
    Fullscreen,
    Region,
    Window,
}

/// <summary>
/// 全局热键服务。UI 层订阅 HotkeyPressed 事件驱动截屏流程。
/// </summary>
public interface IHotkeyService : IDisposable
{
    event EventHandler<HotkeyAction>? HotkeyPressed;

    /// <summary>启动后台消息泵。可多次调用（幂等）。</summary>
    void Start();

    /// <summary>停止消息泵并注销全部热键。</summary>
    void Stop();

    /// <summary>用 hotkey 字符串（Ctrl+PrintScreen 等）注册全部三个动作。</summary>
    void RegisterAll(string fullscreen, string region, string window);
}
