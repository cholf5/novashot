using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using Novashot.App.Views;
using Novashot.Core.Config;

namespace Novashot.App.Tests;

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
        var dir = Path.Combine(Path.GetTempPath(), "novashot-settings-xaml", Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task Window_EditorSection_HasFifteenToolCheckboxes_SelectDisabled()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var w = CreateWindow();
            w.Show(); // 可视树需要 Show 后才存在（下面按可视树找 ItemsControl/CheckBox）
            // 走 Click 路由触发 OnNavClick（IsChecked 直改不会触发导航）
            w.FindControl<ToggleButton>("NavEditor")!.RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            var editorHost = w.FindControl<ScrollViewer>("EditorHost")!;
            Assert.True(editorHost.IsVisible);

            // B-6：编辑器分区有 15 个工具勾选框（含 1 个 Select 禁改项），已与 VM ToolItems 绑定。
            // ItemsControl 嵌在内层 StackPanel 里，直接从 Window 逻辑树递归找
            w.UpdateLayout();
            var itemsControls = new List<ItemsControl>();
            CollectItemsControls(w, itemsControls);
            var toolItems = itemsControls.FirstOrDefault(ic => ic.ItemCount == 15);
            Assert.NotNull(toolItems);

            var checks = new List<CheckBox>();
            CollectCheckBoxes((Avalonia.Visual)toolItems!, checks);
            Assert.Equal(15, checks.Count);
            Assert.Equal(1, checks.Count(c => !c.IsEnabled)); // Select 恒可见禁改
            w.Close();
        }, CancellationToken.None);
    }

    private static void CollectItemsControls(Visual parent, List<ItemsControl> acc)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is ItemsControl ic) acc.Add(ic);
            CollectItemsControls(child, acc);
        }
    }

    private static void CollectCheckBoxes(Visual parent, List<CheckBox> acc)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is CheckBox cb) acc.Add(cb);
            CollectCheckBoxes(child, acc);
        }
    }
}
