using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Microsoft.Extensions.Logging.Abstractions;
using Prtsc.App.Views;
using Prtsc.Core.Config;

namespace Prtsc.App.Tests;

/// <summary>
/// S-1 设置中心：结构测试（照 CompositionPanelXamlTests 按名查）。
/// XAML 重排可以，但按名挂的导航/host/录制器/语言分段不能悄悄消失。
/// 用 SnapshotBootstrap 作宿主：SettingsWindow 里 StaticResource KeycapTheme 等需要主题资源在场。
/// </summary>
[Collection("HeadlessUi")]
public class SettingsWindowXamlTests
{
    private const string NavNames = "NavGeneral NavHotkeys NavQuickSave NavExport NavEditor NavCapture";
    private const string HostNames = "GeneralHost HotkeysHost QuickSaveHost ExportHost EditorHost CaptureHost";

    private static SettingsWindow CreateWindow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "prtsc-settings-xaml", Guid.NewGuid().ToString("N"));
        return new SettingsWindow(
            new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: dir),
            new StubAutostartService());
    }

    [Fact]
    public async Task Window_HasSixNavButtonsAndSectionHosts()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var w = CreateWindow();
            foreach (var n in NavNames.Split(' '))
                Assert.NotNull(w.FindControl<ToggleButton>(n));
            foreach (var n in HostNames.Split(' '))
                Assert.NotNull(w.FindControl<ScrollViewer>(n));
            w.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Window_HasFooterButtonsAndHint()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var w = CreateWindow();
            Assert.NotNull(w.FindControl<Button>("SaveButton"));
            Assert.NotNull(w.FindControl<Button>("CancelButton"));
            Assert.NotNull(w.FindControl<TextBlock>("SaveHint"));
            w.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Window_HasHotkeyRecordersAndLanguageSegment()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var w = CreateWindow();
            foreach (var n in new[] { "RecordFullscreen", "RecordRegion", "RecordWindow" })
                Assert.NotNull(w.FindControl<Button>(n));
            foreach (var n in new[] { "LangSystem", "LangEn", "LangZh" })
                Assert.NotNull(w.FindControl<RadioButton>(n));
            w.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Window_DefaultSection_IsGeneral()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var w = CreateWindow();
            Assert.True(w.FindControl<ToggleButton>("NavGeneral")!.IsChecked == true);
            Assert.True(w.FindControl<ScrollViewer>("GeneralHost")!.IsVisible);
            Assert.False(w.FindControl<ScrollViewer>("HotkeysHost")!.IsVisible);
            // 录制按钮显示当前草稿热键串
            var record = w.FindControl<Button>("RecordFullscreen")!;
            Assert.Equal("PrintScreen", (record.Content as string) ?? string.Empty);
            w.Close();
        }, CancellationToken.None);
    }
}
