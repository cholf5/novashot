using Prtsc.Core.Notifications;

namespace Prtsc.Core.Tests;

/// <summary>
/// N-1：Null 通知服务（mac/linux 与无通知环境的默认装配）——可反复调用、静默、绝不抛。
/// fire-and-forget 契约：实现自吞一切异常，调用点不 try-catch。
/// </summary>
public class NotificationServiceTests
{
    [Fact]
    public void NullService_Show_DoesNotThrow()
    {
        INotificationService service = new NullNotificationService();
        service.Show("title", "message");
        service.Show("title", "message", filePath: @"C:\tmp\a.png");
        service.Show(string.Empty, string.Empty, null);
    }

    [Fact]
    public void NullService_RepeatedCalls_SilentlySucceed()
    {
        var service = new NullNotificationService();
        for (var i = 0; i < 5; i++) service.Show("t", "m");
    }

    [Fact]
    public void NullService_Instance_IsSameReference()
    {
        Assert.Same(NullNotificationService.Instance, NullNotificationService.Instance);
    }
}
