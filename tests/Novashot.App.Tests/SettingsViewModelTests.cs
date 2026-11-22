using Microsoft.Extensions.Logging.Abstractions;
using Novashot.App.Localization;
using Novashot.App.ViewModels;
using Novashot.Core.Autostart;
using Novashot.Core.Config;

namespace Novashot.App.Tests;

/// <summary>
/// S-1 设置中心 VM：整窗草稿 + 原子 Save。
/// 热键/语言副作用由 App 订阅 store.Changed 统一生效（不在此测）；自启由 VM 直调。
/// </summary>
public class SettingsViewModelTests
{
    private static (SettingsViewModel Vm, JsonConfigStore Store, StubAutostartService Autostart) Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novashot-settings-tests", Guid.NewGuid().ToString("N"));
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: dir);
        var fake = new StubAutostartService();
        return (new SettingsViewModel(store, fake), store, fake);
    }

    [Fact]
    public void Save_PersistsAllSections()
    {
        var (vm, store, _) = Create();
        vm.HotkeyCapture = "Ctrl+Shift+F9";
        vm.LaunchOnStartup = true;
        vm.MinimizeToTray = true;
        vm.CloseToTray = false;
        vm.ShowNotification = false;
        vm.CheckOnStartup = false;
        vm.FeedUrl = "  https://github.com/cholf5/novashot/releases  ";
        vm.LanguageIndex = 2;
        vm.QuickSaveFolder = "/tmp/shots";
        vm.QuickSavePatternIndex = 1;
        vm.ExportFormatIndex = 1;
        vm.JpegQuality = 80;
        vm.ExportIncludeBackground = false;
        vm.AutoCopyToClipboard = false;
        vm.IncludeCursor = false;
        vm.CaptureDelayIndex = 2;
        vm.EditorPadding = 55;
        vm.EditorCornerRadius = 18;
        vm.EditorShadowSize = 30;
        vm.EditorOutputRatioIndex = 4;
        vm.EditorShowBackground = false;
        vm.EditorBackgroundColor = "#123456";
        vm.EditorInset = 8;
        vm.EditorAutoBackground = false;
        vm.EditorInsetBackgroundColor = "#abcdef";
        vm.EditorShapeCornerRadius = 6;
        vm.BorderEnabled = true;
        vm.BorderWeight = 5;
        vm.BorderColor = "#fedcba";
        vm.BorderOpacity = 50;
        vm.BorderTypeIndex = 2;

        Assert.True(vm.Save());

        var c = store.Current;
        Assert.Equal("Ctrl+Shift+F9", c.Hotkeys.Capture);
        Assert.True(c.Startup.LaunchOnStartup);
        Assert.True(c.Startup.MinimizeToTray);
        Assert.False(c.Startup.CloseToTray);
        Assert.False(c.Startup.ShowNotification);
        Assert.False(c.Update.CheckOnStartup);
        Assert.Equal("https://github.com/cholf5/novashot/releases", c.Update.FeedUrl); // C-1：草稿 Trim 后落盘
        Assert.Equal("zh-CN", c.Language);
        Assert.Equal("/tmp/shots", c.QuickSave.Folder);
        Assert.Equal("date", c.QuickSave.Pattern);
        Assert.Equal("jpeg", c.Export.DefaultFormat);
        Assert.Equal(80, c.Export.JpegQuality);
        Assert.False(c.Export.IncludeBackground);
        Assert.False(c.Export.AutoCopyToClipboard);
        Assert.False(c.Capture.IncludeCursor);
        Assert.Equal(5, c.Capture.DelaySeconds); // D-1：默认延时（下标 2 → 5 秒）
        Assert.Equal(55, c.Editor.Padding);
        Assert.Equal(18, c.Editor.CornerRadius);
        Assert.Equal(30, c.Editor.ShadowSize);
        Assert.Equal("16:9", c.Editor.OutputRatio);
        Assert.False(c.Editor.ShowBackground);
        Assert.Equal("#123456", c.Editor.BackgroundColor);
        Assert.Equal(8, c.Editor.Inset);
        Assert.False(c.Editor.AutoBackground);
        Assert.Equal("#abcdef", c.Editor.InsetBackgroundColor);
        Assert.Equal(6, c.Editor.ShapeCornerRadius);
        Assert.True(c.Editor.BorderEnabled);
        Assert.Equal(5, c.Editor.BorderWeight);
        Assert.Equal("#fedcba", c.Editor.BorderColor);
        Assert.Equal(50, c.Editor.BorderOpacity);
        Assert.Equal("inside", c.Editor.BorderType);
    }

    [Fact]
    public void Save_RejectsInvalidHotkey()
    {
        var (vm, store, fake) = Create();
        vm.HotkeyCapture = "Ctrl+";

        Assert.False(vm.Save());
        Assert.Equal(Loc.T("Settings.Hint.HotkeyInvalid"), vm.SaveHint);
        Assert.Equal("PrintScreen", store.Current.Hotkeys.Capture); // 未过校验不落盘
        Assert.Equal(0, fake.EnableCalls);
    }

    [Fact]
    public void Save_TogglesAutostartOnlyOnChange()
    {
        var (vm, _, fake) = Create();
        vm.LaunchOnStartup = true;
        Assert.True(vm.Save());
        Assert.Equal(1, fake.EnableCalls);
        Assert.True(fake.IsEnabled);

        Assert.True(vm.Save());
        Assert.Equal(1, fake.EnableCalls); // 未变不再调

        vm.LaunchOnStartup = false;
        Assert.True(vm.Save());
        Assert.Equal(1, fake.DisableCalls);
        Assert.False(fake.IsEnabled);
    }

    [Fact]
    public void Save_AutostartFailure_Aborts()
    {
        var (vm, store, fake) = Create();
        fake.EnableError = new InvalidOperationException("boom");
        vm.LaunchOnStartup = true;

        Assert.False(vm.Save());
        Assert.False(string.IsNullOrEmpty(vm.SaveHint));
        Assert.False(store.Current.Startup.LaunchOnStartup); // config 不落盘
        Assert.Equal("PrintScreen", store.Current.Hotkeys.Capture);
    }

    [Theory]
    [InlineData(0, "system")]
    [InlineData(1, "en")]
    [InlineData(2, "zh-CN")]
    public void Save_MapsLanguageIndex(int index, string expected)
    {
        var (vm, store, _) = Create();
        vm.LanguageIndex = index;

        Assert.True(vm.Save());
        Assert.Equal(expected, store.Current.Language);
    }

    [Fact]
    public void ExportFormatIndex_TogglesJpegQualityVisibility()
    {
        var (vm, _, _) = Create();
        Assert.False(vm.IsJpegQualityVisible);
        vm.ExportFormatIndex = 1;
        Assert.True(vm.IsJpegQualityVisible);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 3)]
    [InlineData(2, 5)]
    public void Save_MapsCaptureDelayIndex(int index, int expectedSeconds)
    {
        var (vm, store, _) = Create();
        vm.CaptureDelayIndex = index;

        Assert.True(vm.Save());
        Assert.Equal(expectedSeconds, store.Current.Capture.DelaySeconds);
    }

    [Fact]
    public void LoadFromConfig_ReflectsCurrentValues()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novashot-settings-tests", Guid.NewGuid().ToString("N"));
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: dir);
        store.Update(c =>
        {
            c.Hotkeys.Capture = "Ctrl+Alt+R";
            c.Export.JpegQuality = 88;
            c.Export.DefaultFormat = "jpeg";
            c.Language = "zh-CN";
            c.QuickSave.Pattern = "increment";
            c.Editor.OutputRatio = "4:3";
            c.Editor.BorderType = "outside";
            c.Capture.IncludeCursor = false;
            c.Capture.DelaySeconds = 3;
        });

        var vm = new SettingsViewModel(store, new StubAutostartService());
        Assert.Equal("Ctrl+Alt+R", vm.HotkeyCapture);
        Assert.Equal(88, vm.JpegQuality);
        Assert.Equal(1, vm.ExportFormatIndex);
        Assert.Equal(2, vm.LanguageIndex);
        Assert.Equal(2, vm.QuickSavePatternIndex);
        Assert.Equal(2, vm.EditorOutputRatioIndex);
        Assert.Equal(0, vm.BorderTypeIndex);
        Assert.False(vm.IncludeCursor);
        Assert.Equal(1, vm.CaptureDelayIndex); // D-1：3 秒 → 下标 1
    }

    // —— B-6 工具栏按钮可见性 ——

    [Fact]
    public void ToolItems_DefaultConfig_AllChecked_SelectFixed()
    {
        var (vm, _, _) = Create();

        Assert.Equal(15, vm.ToolItems.Count);
        Assert.All(vm.ToolItems, i => Assert.True(i.IsChecked));
        var select = vm.ToolItems.Single(i => i.Kind == Novashot.Core.Annotation.Tools.ToolKind.Select);
        Assert.True(select.IsFixed);
        Assert.All(vm.ToolItems.Where(i => !i.IsFixed), i => Assert.False(i.IsFixed));
    }

    [Fact]
    public void ToolItems_LoadFromPartialConfig_ReflectsVisibility()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novashot-settings-tests", Guid.NewGuid().ToString("N"));
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: dir);
        store.Update(c => c.Editor.VisibleTools = new List<string> { "select", "Nonexistent", "crop" });

        var vm = new SettingsViewModel(store, new StubAutostartService());

        Assert.True(vm.ToolItems.Single(i => i.Kind == Novashot.Core.Annotation.Tools.ToolKind.Select).IsChecked);
        Assert.True(vm.ToolItems.Single(i => i.Kind == Novashot.Core.Annotation.Tools.ToolKind.Crop).IsChecked);
        Assert.False(vm.ToolItems.Single(i => i.Kind == Novashot.Core.Annotation.Tools.ToolKind.Rectangle).IsChecked);
    }

    [Fact]
    public void Save_PartialSelection_WritesVisibleTools()
    {
        var (vm, store, _) = Create();
        vm.ToolItems.Single(i => i.Kind == Novashot.Core.Annotation.Tools.ToolKind.Rectangle).IsChecked = false;
        vm.ToolItems.Single(i => i.Kind == Novashot.Core.Annotation.Tools.ToolKind.Mosaic).IsChecked = false;

        Assert.True(vm.Save());

        var resolved = Novashot.Core.Annotation.Tools.ToolVisibility.Resolve(store.Current.Editor.VisibleTools);
        Assert.Equal(13, resolved.Count); // 15 - 2（Select 恒在）
        Assert.DoesNotContain(Novashot.Core.Annotation.Tools.ToolKind.Rectangle, resolved);
        Assert.DoesNotContain(Novashot.Core.Annotation.Tools.ToolKind.Mosaic, resolved);
    }

    [Fact]
    public void Save_AllChecked_WritesEmptyList_DefaultAll()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novashot-settings-tests", Guid.NewGuid().ToString("N"));
        var store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: dir);
        store.Update(c => c.Editor.VisibleTools = new List<string> { "Select", "Crop" });

        var vm = new SettingsViewModel(store, new StubAutostartService());
        foreach (var item in vm.ToolItems) item.IsChecked = true;

        Assert.True(vm.Save());
        Assert.Empty(store.Current.Editor.VisibleTools); // 全选 → 规范空表（向前兼容新增工具）
    }
}
