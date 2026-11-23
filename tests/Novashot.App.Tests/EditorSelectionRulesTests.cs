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
/// Inspector 交互规则（M-Inspector 设计）：画完即选中且工具不切走、
/// Selection 跨工具存活、层序命令进 Undo/Redo。
/// </summary>
public class EditorSelectionRulesTests
{
    private static CapturedImage Captured(int w = 64, int h = 48)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new CapturedImage(bmp, new Novashot.Core.Common.PixelRect(0, 0, w, h), 1.0, CaptureSource.Region);
    }

    private static EditorViewModel Editor()
    {
        var vm = new MainWindowViewModel(new FakeClipboardService());
        vm.EnterEditor(Captured());
        return vm.Editor!;
    }

    private static RectangleAnnotation Rect(float x, float y)
        => new(new SKRect(x, y, x + 10, y + 10), AnnotationStyle.Default);

    // ---- 画完即选中 ----

    [Fact]
    public void ExecuteAdd_SetsSelection_AndKeepsTool()
    {
        var editor = Editor();
        editor.CurrentTool = ToolKind.Rectangle;
        var rect = Rect(0, 0);
        editor.ExecuteAdd(rect);
        Assert.Same(rect, editor.Selection);
        Assert.Equal(ToolKind.Rectangle, editor.CurrentTool); // 不切走
    }

    // ---- Selection 跨工具存活 ----

    [Fact]
    public void SwitchTool_KeepsSelection()
    {
        var editor = Editor();
        editor.ExecuteAdd(Rect(0, 0));
        editor.CurrentTool = ToolKind.Ellipse;
        Assert.NotNull(editor.Selection); // 不再清空
    }

    [Fact]
    public void SelectToolEmptyClick_ClearsSelection_StillWorks()
    {
        var editor = Editor();
        editor.ExecuteAdd(Rect(0, 0));
        var tool = (SelectTool)editor.CreateActiveTool();
        tool.OnPointerDown(new SKPoint(50, 40), editor.CurrentStyle); // 空白处
        Assert.Null(editor.Selection);
    }

    [Fact]
    public void DeleteSelection_ClearsSelection()
    {
        var editor = Editor();
        editor.ExecuteAdd(Rect(0, 0));
        editor.DeleteSelectionCommand.Execute(null);
        Assert.Null(editor.Selection);
        Assert.Empty(editor.Document.Annotations);
    }

    // ---- 层序命令 ----

    [Fact]
    public void BringForward_ThenUndo_RestoresOrder()
    {
        var editor = Editor();
        var a = Rect(0, 0);
        var b = Rect(5, 5);
        editor.ExecuteAdd(a);
        editor.ExecuteAdd(b);
        Assert.Equal(1, editor.Document.IndexOf(b)); // 栈顶

        editor.Selection = a;
        editor.BringForwardCommand.Execute(null);
        Assert.Equal(1, editor.Document.IndexOf(a));

        editor.UndoCommand.Execute(null);
        Assert.Equal(0, editor.Document.IndexOf(a));
    }

    [Fact]
    public void BringForward_OnTopSelection_IsDisabled()
    {
        var editor = Editor();
        editor.ExecuteAdd(Rect(0, 0)); // 唯一且在栈顶
        Assert.False(editor.BringForwardCommand.CanExecute(null));
        Assert.False(editor.BringToFrontCommand.CanExecute(null));
        Assert.False(editor.SendBackwardCommand.CanExecute(null));
        Assert.False(editor.SendToBackCommand.CanExecute(null));
    }

    [Fact]
    public void SendToBack_MovesToIndex0()
    {
        var editor = Editor();
        var a = Rect(0, 0);
        editor.ExecuteAdd(a);
        editor.ExecuteAdd(Rect(5, 5));
        editor.ExecuteAdd(Rect(10, 10));
        editor.Selection = a;
        editor.SendToBackCommand.Execute(null);
        Assert.Equal(0, editor.Document.IndexOf(a));
    }

    [Fact]
    public void BringToFront_MovesToTop()
    {
        var editor = Editor();
        var a = Rect(0, 0);
        editor.ExecuteAdd(a);
        editor.ExecuteAdd(Rect(5, 5));
        editor.Selection = a;
        editor.BringToFrontCommand.Execute(null);
        Assert.Equal(1, editor.Document.IndexOf(a));
    }

    [Fact]
    public void Redo_AfterUndoRestoresReorder()
    {
        var editor = Editor();
        var a = Rect(0, 0);
        editor.ExecuteAdd(a);
        editor.ExecuteAdd(Rect(5, 5));
        editor.Selection = a;
        editor.BringForwardCommand.Execute(null);
        editor.UndoCommand.Execute(null);
        editor.RedoCommand.Execute(null);
        Assert.Equal(1, editor.Document.IndexOf(a));
    }
}
