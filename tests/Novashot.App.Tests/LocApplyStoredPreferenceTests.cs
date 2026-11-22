using Avalonia;
using Avalonia.Headless;
using Novashot.App.Localization;

namespace Novashot.App.Tests;

/// <summary>
/// S-1 修复（review I4）：语言副作用收敛到 Loc.ApplyStoredPreference——
/// config 里的语言值合法且与当前不同才切换。设置保存 → store.Changed → 这里。
/// </summary>
[Collection("HeadlessUi")]
public class LocApplyStoredPreferenceTests
{
    [Fact]
    public async Task ApplyStoredPreference_SwitchesOnlyOnValidNewValue()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var app = Application.Current!;
            Loc.Initialize("en", app);
            var before = Loc.Current;

            Loc.ApplyStoredPreference("bogus", app); // 非法值不动
            Assert.Equal(before, Loc.Current);

            Loc.ApplyStoredPreference("zh-CN", app);
            Assert.Equal(Loc.ChineseSimplified, Loc.Current);
            Assert.Equal("zh-CN", Loc.Preference);

            Loc.ApplyStoredPreference("zh-CN", app); // 同值不重刷
            Assert.Equal(Loc.ChineseSimplified, Loc.Current);

            Loc.ApplyStoredPreference("system", app);
            Assert.Equal("system", Loc.Preference);
        }, CancellationToken.None);
    }
}
