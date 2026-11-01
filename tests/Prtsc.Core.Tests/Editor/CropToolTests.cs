using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Model;
using Prtsc.Core.Annotation.Tools;
using Prtsc.Core.Editor;
using SkiaSharp;

namespace Prtsc.Core.Tests;

/// <summary>
/// CropTool：两角拖拽出裁剪框（预览是普通 SKRect，不是 Annotation）。
/// OnPointerUp 返回 null（不走 ExecuteAdd），结果经 <see cref="CropTool.Committed"/> 查询。
/// </summary>
public class CropToolTests
{
    private static CropTool NewTool() => new(new SKRect(0, 0, 100, 80));

    [Fact]
    public void Drag_ProducesCommittedRect_Standardized()
    {
        var tool = NewTool();
        tool.OnPointerDown(new SKPoint(50, 60), AnnotationStyle.Default);
        tool.OnPointerMove(new SKPoint(10, 20));
        var committed = tool.OnPointerUp(new SKPoint(10, 20));

        Assert.Null(committed); // 不产出标注
        Assert.Equal(new SKRect(10, 20, 50, 60), tool.Committed);
    }

    [Fact]
    public void Drag_TooSmall_NoCommit()
    {
        var tool = NewTool();
        tool.OnPointerDown(new SKPoint(50, 50), AnnotationStyle.Default);
        tool.OnPointerMove(new SKPoint(51, 51));
        tool.OnPointerUp(new SKPoint(51, 51));
        Assert.Null(tool.Committed);
    }

    [Fact]
    public void Drag_ClampsToImageBounds()
    {
        var tool = NewTool();
        tool.OnPointerDown(new SKPoint(90, 70), AnnotationStyle.Default);
        tool.OnPointerUp(new SKPoint(150, 120));
        Assert.Equal(new SKRect(90, 70, 100, 80), tool.Committed);
    }

    [Fact]
    public void Preview_FollowsDrag()
    {
        var tool = NewTool();
        Assert.Null(tool.PreviewRect);
        tool.OnPointerDown(new SKPoint(5, 5), AnnotationStyle.Default);
        tool.OnPointerMove(new SKPoint(30, 40));
        Assert.Equal(new SKRect(5, 5, 30, 40), tool.PreviewRect);
        tool.Cancel();
        Assert.Null(tool.PreviewRect);
        Assert.Null(tool.Committed);
    }

    [Fact]
    public void AppliedToState_SetsCropRect()
    {
        var state = EditorState.Default;
        var next = CropTool.AppliedToState(state, new SKRect(5, 6, 20, 30));
        Assert.Equal(new SKRect(5, 6, 20, 30), next.CropRect);
        // null → 回全图
        var cleared = CropTool.AppliedToState(next, null);
        Assert.Null(cleared.CropRect);
    }
}
