using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Microsoft.Extensions.Logging.Abstractions;
using Prtsc.App.Localization;
using Prtsc.App.ViewModels;
using Prtsc.App.Views;
using Prtsc.Core.Annotation.Tools;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Common;
using Prtsc.Core.Config;
using Prtsc.Core.Windows;
using SkiaSharp;

namespace Prtsc.App.Tests;

/// <summary>
/// B-6 工具栏按钮自定义：MainWindow 工具坞按 config 过滤的接线（headless 能驱动的范围）。
/// 构造时读 Editor.VisibleTools 决定各 RadioButton/分隔线 IsVisible；config.Update 触发 Changed 后重算；
/// 编辑器打开且当前工具被隐藏时回落 Select。热键不受影响（MapKeyToTool 照旧，M4d 既有测试守着）。
/// </summary>
[Collection("HeadlessUi")]
public class ToolDockVisibilityWiringTests
{
    [Fact]
    public void DefaultConfig_AllToolsVisible_AllSeparatorsVisible()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        session.Dispatch(() =>
        {
            var window = NewWindow(out var store, out var vm, visibleTools: null);
            try
            {
                var (radios, seps) = Inspect(window);
                Assert.Equal(15, radios.Count);
                Assert.All(radios, r => Assert.True(r.IsVisible));
                Assert.All(seps, s => Assert.True(s.IsVisible));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void PartialVisibleTools_HiddenRadiosAndOrphanSeparators()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        session.Dispatch(() =>
        {
            var window = NewWindow(out _, out _, visibleTools: new List<string> { "Select", "Crop" });
            try
            {
                var (radios, seps) = Inspect(window);

                var visibleTags = radios.Where(r => r.IsVisible).Select(r => (string)r.Tag!).ToList();
                Assert.Equal(new[] { "Select", "Crop" }, visibleTags);

                // 组边界线只剩 Select|Crop 之间一条（点亮紧邻 Crop 前的那条）
                Assert.Equal(1, seps.Count(s => s.IsVisible));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void ConfigUpdate_TriggersChanged_ReappliesVisibility()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        session.Dispatch(() =>
        {
            var window = NewWindow(out var store, out _, visibleTools: null);
            try
            {
                store.Update(c => c.Editor.VisibleTools = new List<string> { "Select", "Text", "Number" });

                var (radios, seps) = Inspect(window);
                var visibleTags = radios.Where(r => r.IsVisible).Select(r => (string)r.Tag!).ToList();
                Assert.Equal(new[] { "Select", "Text", "Number" }, visibleTags);
                Assert.Equal(1, seps.Count(s => s.IsVisible));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void CurrentToolHidden_FallsBackToSelect()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        session.Dispatch(() =>
        {
            var window = NewWindow(out var store, out var vm, visibleTools: null);
            try
            {
                vm.EnterEditor(Captured());
                Assert.NotNull(vm.Editor);
                vm.Editor!.CurrentTool = ToolKind.Mosaic;

                store.Update(c => c.Editor.VisibleTools = new List<string> { "Select", "Crop" });

                Assert.Equal(ToolKind.Select, vm.Editor.CurrentTool);
            }
            finally
            {
                window.Close();
                vm.Editor?.Dispose();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void HiddenTool_HotkeyStillWorks()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        session.Dispatch(() =>
        {
            var window = NewWindow(out var store, out var vm, visibleTools: new List<string> { "Select", "Crop" });
            try
            {
                vm.EnterEditor(Captured());

                // 热键口径裁决：隐藏 ≠ 禁用，R 仍切 Rectangle（UI 上无按钮）
                window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, null);

                Assert.Equal(ToolKind.Rectangle, vm.Editor!.CurrentTool);
            }
            finally
            {
                window.Close();
                vm.Editor?.Dispose();
            }
        }, CancellationToken.None);
    }

    private static MainWindow NewWindow(out JsonConfigStore store, out MainWindowViewModel vm,
        List<string>? visibleTools)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "prtsc-tooldock-tests", Guid.NewGuid().ToString("N"));
        store = new JsonConfigStore(NullLogger<JsonConfigStore>.Instance, overrideDirectory: dir);
        if (visibleTools is not null)
            store.Update(c => c.Editor.VisibleTools = visibleTools);

        vm = new MainWindowViewModel(new FakeClipboardService(), store);
        return new MainWindow(
            vm,
            new NoCaptureService(),
            new NoWindowService(),
            store,
            new Prtsc.Core.Library.LibraryStore(NullLogger<Prtsc.Core.Library.LibraryStore>.Instance,
                overrideDirectory: dir),
            NullLogger<MainWindow>.Instance);
    }

    private static (List<RadioButton> Radios, List<Rectangle> Separators) Inspect(MainWindow window)
    {
        var dock = window.FindControl<StackPanel>("ToolDock")!;
        var radios = new List<RadioButton>();
        var seps = new List<Rectangle>();
        foreach (var child in dock.Children)
        {
            if (child is RadioButton rb) radios.Add(rb);
            else if (child is Rectangle r) seps.Add(r);
        }
        return (radios, seps);
    }

    private static CapturedImage Captured(int w = 8, int h = 6)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new CapturedImage(bmp, new PixelRect(0, 0, w, h), 1.0, CaptureSource.Region);
    }

    private sealed class NoCaptureService : ICaptureService
    {
        public IReadOnlyList<DisplayInfo> EnumerateDisplays() => Array.Empty<DisplayInfo>();
        public CapturedImage CaptureFullscreen() => throw new NotSupportedException();
        public CapturedImage CaptureDisplay(int displayIndex) => throw new NotSupportedException();
        public CapturedImage CaptureRegion(PixelRect region) => throw new NotSupportedException();
        public CapturedImage CaptureWindow(nint hwnd) => throw new NotSupportedException();
    }

    private sealed class NoWindowService : IWindowService
    {
        public IReadOnlyList<WindowInfo> EnumerateWindows() => Array.Empty<WindowInfo>();
        public WindowInfo? GetForegroundWindow() => null;
        public void TryBringToForeground(nint hwnd) { }
    }
}
