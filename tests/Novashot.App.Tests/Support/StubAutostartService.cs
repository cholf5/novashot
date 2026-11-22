using Novashot.Core.Autostart;

namespace Novashot.App.Tests;

/// <summary>测试共享的自启服务 stub：记录调用、可注入失败。</summary>
public sealed class StubAutostartService : IAutostartService
{
    public bool IsEnabled { get; set; }
    public int EnableCalls { get; private set; }
    public int DisableCalls { get; private set; }
    public Exception? EnableError { get; set; }

    public void Enable()
    {
        if (EnableError is not null) throw EnableError;
        EnableCalls++;
        IsEnabled = true;
    }

    public void Disable()
    {
        DisableCalls++;
        IsEnabled = false;
    }

    public void SyncPath() { }
}
