using Prtsc.Core.Notifications;

namespace Prtsc.Core.Tests;

/// <summary>
/// N-1 通知决策表（Core 纯函数）：4 事件 × 总开关 的决策表钉死。
/// 本卡只有 <c>StartupConfig.ShowNotification</c> 总开关；事件入参形状是 per-event
/// 关闭的预留扩展位——枚举加值时「全枚举 × 开关」守卫测试兜底，App 层调用点不用改。
/// </summary>
public class NotificationPolicyTests
{
    // —— 决策表：4 事件 × 开/关 ——

    [Theory]
    [InlineData(NotificationEvent.Exported, true, true)]
    [InlineData(NotificationEvent.QuickSaved, true, true)]
    [InlineData(NotificationEvent.Copied, true, true)]
    [InlineData(NotificationEvent.Uploaded, true, true)]
    [InlineData(NotificationEvent.Exported, false, false)]
    [InlineData(NotificationEvent.QuickSaved, false, false)]
    [InlineData(NotificationEvent.Copied, false, false)]
    [InlineData(NotificationEvent.Uploaded, false, false)]
    public void ShouldNotify_FollowsMasterSwitch(NotificationEvent evt, bool setting, bool expected)
    {
        Assert.Equal(expected, NotificationPolicy.ShouldNotify(setting, evt));
    }

    [Fact]
    public void ShouldNotify_AllEnumValues_RespectMasterSwitch()
    {
        // 扩展守卫：将来枚举加值（per-event 语义）后总开关仍必须生效
        foreach (NotificationEvent evt in Enum.GetValues<NotificationEvent>())
        {
            Assert.True(NotificationPolicy.ShouldNotify(setting: true, evt), evt.ToString());
            Assert.False(NotificationPolicy.ShouldNotify(setting: false, evt), evt.ToString());
        }
    }

    [Fact]
    public void NotificationEvent_HasExactlyFourMembers_OnThisCard()
    {
        // 本卡事件面钉死（Exported/QuickSaved/Copied/Uploaded）；扩值须同步上面决策表与 handoff
        Assert.Equal(4, Enum.GetValues<NotificationEvent>().Length);
    }
}
