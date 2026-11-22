namespace Novashot.Core.Autostart;

/// <summary>
/// 开机自启能力。平台无关；Windows 端走注册表 Run 项，macOS 端走 launchd LaunchAgent。
/// </summary>
public interface IAutostartService
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();
    /// <summary>如果可执行路径变了，把自启配置同步刷新到当前路径。</summary>
    void SyncPath();
}
