using Novashot.App.Localization;
using Novashot.App.ViewModels;
using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Annotation.Tools;
using Novashot.Core.Capture;
using Novashot.Core.Clipboard;
using SkiaSharp;
using Xunit;

namespace Novashot.App.Tests;

/// <summary>
/// 左侧元素列表 VM：Document 增删/层序变化 → 列表同步（栈顶在顶）、
/// Selection ↔ 行高亮双向联动、点行 = 选中 + 切 Select 工具。
/// </summary>
public class ElementListViewModelTests
{
    private static EditorViewModel Editor()
    {
        var vm = new MainWindowViewModel(new FakeClipboardService());
        var bmp = new SKBitmap(new SKImageInfo(64, 48, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        vm.EnterEditor(new CapturedImage(bmp, new Novashot.Core.Common.PixelRect(0, 0, 64, 48), 1.0, CaptureSource.Region));
        return vm.Editor!;
    }

    private static RectangleAnnotation Rect(float x, float y)
        => new(new SKRect(x, y, x + 10, y + 10), AnnotationStyle.Default);

    [Fact]
    public void ExecuteAdd_AppendsItem_TopFirst()
    {
        var editor = Editor();
        var a = Rect(0, 0);
        var b = Rect(5, 5);
        editor.ExecuteAdd(a);
        editor.ExecuteAdd(b);
        Assert.Equal(2, editor.ElementList.Count);
        Assert.Same(b, editor.ElementList[0].Annotation); // 栈顶在列表顶
        Assert.Same(a, editor.ElementList[1].Annotation);
        Assert.True(editor.HasElements);
    }

    [Fact]
    public void Remove_SyncsList()
    {
        var editor = Editor();
        editor.ExecuteAdd(Rect(0, 0));
        editor.DeleteSelectionCommand.Execute(null);
        Assert.Empty(editor.ElementList);
        Assert.False(editor.HasElements);
    }

    [Fact]
    public void Reorder_SyncsListOrder()
    {
        var editor = Editor();
        var a = Rect(0, 0);
        editor.ExecuteAdd(a);
        editor.ExecuteAdd(Rect(5, 5));
        editor.Selection = a;
        editor.BringForwardCommand.Execute(null);
        Assert.Same(a, editor.ElementList[0].Annotation); // 新栈顶排到列表顶
    }

    [Fact]
    public void SelectionChange_SyncsHighlight()
    {
        var editor = Editor();
        var a = Rect(0, 0);
        editor.ExecuteAdd(a);
        editor.Selection = null;
        Assert.False(editor.ElementList[0].IsSelected);
        editor.Selection = a;
        Assert.True(editor.ElementList[0].IsSelected);
    }

    [Fact]
    public void Summary_ByType()
    {
        var editor = Editor();
        editor.ExecuteAdd(Rect(0, 0));
        editor.ExecuteAdd(new TextAnnotation(new SKPoint(0, 20), "hello world 12345", 24f, AnnotationStyle.Default));
        editor.ExecuteAdd(new NumberAnnotation(new SKPoint(5, 5), 3, AnnotationStyle.Default));

        Assert.Equal(Loc.T("Kind.Rectangle"), editor.ElementList[2].Summary);
        Assert.Equal(12, editor.ElementList[1].Summary.Length); // 截 12 字
        Assert.StartsWith("hello world", editor.ElementList[1].Summary);
        Assert.Equal("#3", editor.ElementList[0].Summary);
    }

    [Fact]
    public void IconKey_MapsPerType()
    {
        var editor = Editor();
        editor.ExecuteAdd(Rect(0, 0));
        editor.ExecuteAdd(new MosaicAnnotation(new SKRect(0, 0, 10, 10), editor.Document.Source, AnnotationStyle.Default));
        Assert.Equal("Icon.Mosaic", editor.ElementList[0].IconKey);
        Assert.Equal("Icon.Rect", editor.ElementList[1].IconKey);
    }

    [Fact]
    public void SelectFromListCommand_Selects_AndSwitchesToSelectTool()
    {
        var editor = Editor();
        var a = Rect(0, 0);
        editor.ExecuteAdd(a);
        editor.CurrentTool = ToolKind.Mosaic; // ExecuteAdd 不切工具，此时仍在 Mosaic
        editor.SelectFromListCommand.Execute(editor.ElementList[0]);
        Assert.Same(a, editor.Selection);
        Assert.Equal(ToolKind.Select, editor.CurrentTool);
    }

    [Fact]
    public void SelectFromListCommand_Null_Noop()
    {
        var editor = Editor();
        editor.CurrentTool = ToolKind.Mosaic;
        editor.SelectFromListCommand.Execute(null);
        Assert.Null(editor.Selection);
        Assert.Equal(ToolKind.Mosaic, editor.CurrentTool);
    }
}
