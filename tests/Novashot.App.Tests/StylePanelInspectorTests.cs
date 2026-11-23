using Novashot.App.ViewModels;
using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Capture;
using Novashot.Core.Clipboard;
using SkiaSharp;
using Xunit;

namespace Novashot.App.Tests;

/// <summary>
/// Inspector 双态与几何/效果编辑（M-Inspector）：选中时按对象显示几何/效果组，
/// 未选中时保持默认样式编辑；几何写值走既有 Move/Resize 命令（进 Undo），
/// 效果参数（Mosaic BlockSize / Blur Sigma）就地改不进 Undo。
/// </summary>
public class StylePanelInspectorTests
{
    private static StylePanelViewModel Panel(out EditorViewModel editor)
    {
        var vm = new MainWindowViewModel(new FakeClipboardService());
        var bmp = new SKBitmap(new SKImageInfo(64, 48, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        vm.EnterEditor(new CapturedImage(bmp, new Novashot.Core.Common.PixelRect(0, 0, 64, 48), 1.0, CaptureSource.Region));
        editor = vm.Editor!;
        return editor.StylePanel;
    }

    private static RectangleAnnotation Rect(float x, float y, float w = 10, float h = 10)
        => new(new SKRect(x, y, x + w, y + h), AnnotationStyle.Default);

    // ---- 双态 ----

    [Fact]
    public void NoSelection_HasSelectionFalse_GeometryHidden()
    {
        var panel = Panel(out var editor);
        Assert.False(panel.HasSelection);
        Assert.False(panel.IsGeometryVisible);
        Assert.False(panel.IsEffectVisible);
    }

    [Fact]
    public void BoxSelection_GeometryVisible_SizeEditable()
    {
        var panel = Panel(out var editor);
        editor.ExecuteAdd(Rect(2, 3));
        Assert.True(panel.HasSelection);
        Assert.True(panel.IsGeometryVisible);
        Assert.True(panel.IsSizeEditable);
        Assert.Equal(2f, panel.SelX);
        Assert.Equal(3f, panel.SelY);
        Assert.Equal(10f, panel.SelW);
        Assert.Equal(10f, panel.SelH);
    }

    // ---- 几何写值 ----

    [Fact]
    public void SetSelX_MovesAnnotation_AndIsUndoable()
    {
        var panel = Panel(out var editor);
        var rect = Rect(2, 3);
        editor.ExecuteAdd(rect);
        panel.SelX = 20;
        Assert.Equal(20f, rect.GeometryRect.Left);
        Assert.Equal(3f, rect.GeometryRect.Top); // Y 不动
        editor.UndoCommand.Execute(null);
        Assert.Equal(2f, rect.GeometryRect.Left);
    }

    [Fact]
    public void SetSelY_MovesAnnotation()
    {
        var panel = Panel(out var editor);
        var rect = Rect(2, 3);
        editor.ExecuteAdd(rect);
        panel.SelY = 30;
        Assert.Equal(30f, rect.GeometryRect.Top);
        Assert.Equal(2f, rect.GeometryRect.Left);
    }

    [Fact]
    public void SetSelX_ClampsToCanvas()
    {
        var panel = Panel(out var editor);
        var rect = Rect(2, 3);
        editor.ExecuteAdd(rect);
        panel.SelX = -50;
        Assert.Equal(0f, rect.GeometryRect.Left);
    }

    [Fact]
    public void SetSelW_ResizesViaBottomRight_AndIsUndoable()
    {
        var panel = Panel(out var editor);
        var rect = Rect(2, 3);
        editor.ExecuteAdd(rect);
        panel.SelW = 30;
        Assert.Equal(30f, rect.GeometryRect.Width);
        Assert.Equal(10f, rect.GeometryRect.Height); // H 不动
        editor.UndoCommand.Execute(null);
        Assert.Equal(10f, rect.GeometryRect.Width);
    }

    [Fact]
    public void SetSelH_ResizesHeight()
    {
        var panel = Panel(out var editor);
        var rect = Rect(2, 3);
        editor.ExecuteAdd(rect);
        panel.SelH = 25;
        Assert.Equal(25f, rect.GeometryRect.Height);
        Assert.Equal(10f, rect.GeometryRect.Width);
    }

    [Fact]
    public void SetSelW_TooSmall_ClampsToMin()
    {
        var panel = Panel(out var editor);
        var rect = Rect(2, 3);
        editor.ExecuteAdd(rect);
        panel.SelW = 0.5f;
        Assert.Equal(4f, rect.GeometryRect.Width); // InspectorGeometry.MinSize
    }

    [Fact]
    public void TextSelection_SizeNotEditable_ButXYIs()
    {
        var panel = Panel(out var editor);
        editor.ExecuteAdd(new TextAnnotation(new SKPoint(4, 4), "hi", 24f, AnnotationStyle.Default));
        Assert.True(panel.IsGeometryVisible);
        Assert.False(panel.IsSizeEditable);
        panel.SelX = 8;
        Assert.Equal(8f, ((TextAnnotation)editor.Selection!).GeometryRect.Left);
    }

    [Fact]
    public void SetSelX_NoSelection_Noop()
    {
        var panel = Panel(out _);
        panel.SelX = 42; // 不应抛
        Assert.Equal(0f, panel.SelX);
    }

    // ---- 效果参数 ----

    [Fact]
    public void MosaicSelection_ShowsBlockSize()
    {
        var panel = Panel(out var editor);
        editor.ExecuteAdd(new MosaicAnnotation(
            new SKRect(0, 0, 20, 20), editor.Document.Source, AnnotationStyle.Default));
        Assert.True(panel.IsEffectVisible);
        Assert.Equal(MosaicAnnotation.DefaultBlockSize, panel.EffectStrength);
        Assert.Equal(MosaicAnnotation.MinBlockSize, panel.EffectMin);
        Assert.Equal(MosaicAnnotation.MaxBlockSize, panel.EffectMax);
    }

    [Fact]
    public void SetEffectStrength_Mosaic_WritesBlockSize()
    {
        var panel = Panel(out var editor);
        editor.ExecuteAdd(new MosaicAnnotation(
            new SKRect(0, 0, 20, 20), editor.Document.Source, AnnotationStyle.Default));
        panel.EffectStrength = 24;
        Assert.Equal(24, ((MosaicAnnotation)editor.Selection!).BlockSize);
    }

    [Fact]
    public void SetEffectStrength_Blur_WritesSigma()
    {
        var panel = Panel(out var editor);
        editor.ExecuteAdd(new BlurAnnotation(
            new SKRect(0, 0, 20, 20), editor.Document.Source, AnnotationStyle.Default));
        panel.EffectStrength = 20;
        Assert.Equal(20f, ((BlurAnnotation)editor.Selection!).Sigma);
    }

    [Fact]
    public void BlurSelection_EffectRangeIsSigma()
    {
        var panel = Panel(out var editor);
        editor.ExecuteAdd(new BlurAnnotation(
            new SKRect(0, 0, 20, 20), editor.Document.Source, AnnotationStyle.Default));
        Assert.Equal(BlurAnnotation.MinSigma, panel.EffectMin);
        Assert.Equal(BlurAnnotation.MaxSigma, panel.EffectMax);
    }

    // ---- 面板外改动反映 ----

    [Fact]
    public void GeometryRefresh_AfterOutOfPanelMutation()
    {
        var panel = Panel(out var editor);
        var rect = Rect(2, 3);
        editor.ExecuteAdd(rect);
        rect.MoveBy(new SKPoint(5, 0));
        Assert.Equal(7f, panel.SelX); // Document.Changed → 刷新
    }

    [Fact]
    public void GeometryRefresh_AfterUndo()
    {
        var panel = Panel(out var editor);
        var rect = Rect(2, 3);
        editor.ExecuteAdd(rect);
        panel.SelX = 20;
        editor.UndoCommand.Execute(null);
        Assert.Equal(2f, panel.SelX);
    }
}
