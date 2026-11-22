using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Novashot.App.Windowing;
using Xunit;

namespace Novashot.App.Tests;

/// <summary>
/// B-9 深色标题栏接线：DWM 视觉只能在真机目检（headless 句柄是零值 STUB），
/// 这里只锁「每个窗口都被钩到」与「无句柄时静默跳过」这两条结构契约。
/// </summary>
[Collection("HeadlessUi")]
public class CaptionThemeTests
{
    [Fact]
    public async Task GlobalHook_FiresForEveryOpenedWindow_AndRegistrationIsIdempotent()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            CaptionTheme.AttachAll();
            CaptionTheme.AttachAll(); // 幂等：重复调用不得二次注册（否则同一窗口写两遍）

            var before = CaptionTheme.HookInvocations;

            var first = new Window();
            first.Show();
            Assert.Equal(before + 1, CaptionTheme.HookInvocations);
            first.Close();

            // 后创建的窗口同样被覆盖（新对话框不需要再手工挂接）
            var second = new Window();
            second.Show();
            Assert.Equal(before + 2, CaptionTheme.HookInvocations);
            second.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Attach_OnUnshownWindow_IsSilentNoOp()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var window = new Window();
            CaptionTheme.Attach(window); // 尚无平台句柄：必须静默跳过，绝不抛
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ActualThemeVariant_IsResolvedByTheTimeWindowOpens()
    {
        // 深色应用启动路径的前提：钩子触发（首次合成之前）时主题已解析为 Dark，
        // 否则第一次就会写错浅色（深色标题栏回归的真实成因之一）
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var window = new Window();
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
            window.Close();
        }, CancellationToken.None);
    }

#if WIN_PLATFORM
    [Theory]
    [InlineData(true, false, false, true)]   // Actual 已解析深色
    [InlineData(false, true, false, true)]   // 窗口 Requested 深色
    [InlineData(false, false, true, true)]   // Actual/窗口都是 Default → 回退到应用深色
    [InlineData(false, false, false, false)] // 全 Default → 浅色
    public void ResolveIsDark_FallsBackUntilAConcreteVariantIsFound(
        bool actualDark, bool requestedDark, bool appDark, bool expected)
    {
        Assert.Equal(expected, CaptionTheme.ResolveIsDark(
            actualDark ? ThemeVariant.Dark : ThemeVariant.Default,
            requestedDark ? ThemeVariant.Dark : ThemeVariant.Default,
            appDark ? ThemeVariant.Dark : ThemeVariant.Default));
    }

    [Fact]
    public void ResolveIsDark_ExplicitLightBeatsAppDark()
    {
        Assert.False(CaptionTheme.ResolveIsDark(
            ThemeVariant.Light, ThemeVariant.Default, ThemeVariant.Dark));
    }

    [Fact]
    public void PlatformApply_ZeroHandle_IsSkipped()
    {
        // headless / 未建窗的零句柄：绝不把 0 传给 DWM
        Assert.False(Novashot.Platform.Windows.Windowing.WindowsCaptionTheme.Apply(nint.Zero, dark: true));
        Assert.False(Novashot.Platform.Windows.Windowing.WindowsCaptionTheme.Apply(nint.Zero, dark: false));
    }
#endif
}
