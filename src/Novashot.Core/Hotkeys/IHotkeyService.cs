namespace Novashot.Core.Hotkeys;

/// <summary>
/// 全局热键服务。UI 层订阅 HotkeyPressed 事件驱动截屏流程。
/// 统一交互（2026-11-23 第二轮意图收敛）：只有一个「截图」热键——打开统一 overlay，
/// 拖拽=区域、悬停点击=窗口、空白点击=该屏全屏；全屏/区域/窗口不再各占一个热键意图。
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>截图热键按下（无动作负载——意图唯一）。</summary>
    event EventHandler? HotkeyPressed;

    /// <summary>启动后台消息泵。可多次调用（幂等）。</summary>
    void Start();

    /// <summary>停止消息泵并注销热键。</summary>
    void Stop();

    /// <summary>用 hotkey 字符串（PrintScreen、Ctrl+PrintScreen 等）注册截图热键。非法 spec 记日志不抛。</summary>
    void Register(string spec);
}
