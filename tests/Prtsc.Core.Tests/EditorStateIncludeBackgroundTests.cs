using Prtsc.Core.Editor;
using SkiaSharp;

namespace Prtsc.Core.Tests;

/// <summary>
/// G-14（winshot export.includeBackground）：IncludeBackground=false 时导出/复制用
/// 无背景合成状态（Background=None、Padding/Shadow/Inset 归零、边框关）；裁剪保留。
/// </summary>
public class EditorStateIncludeBackgroundTests
{
    private static readonly EditorState Styled = new(
        Background: BackgroundKind.Gradient,
        BackgroundGradient: "linear-gradient(135deg, #667eea 0%, #764ba2 100%)",
        Padding: 40,
        CornerRadius: 12,
        ShadowSize: 20,
        InsetSize: 8,
        BorderWidth: 3,
        BorderColor: SKColors.Black,
        CropRect: new SKRect(2, 2, 50, 50));

    [Fact]
    public void WithoutBackground_StripsFrame_KeepsCrop()
    {
        var stripped = EditorStateStripping.StripBackground(Styled);

        Assert.Equal(BackgroundKind.None, stripped.Background);
        Assert.Equal(0, stripped.Padding);
        Assert.Equal(0f, stripped.ShadowSize);
        Assert.Equal(0f, stripped.InsetSize);
        Assert.Equal(0f, stripped.BorderWidth);
        Assert.Equal(new SKRect(2, 2, 50, 50), stripped.CropRect);   // 裁剪保留
    }

    [Fact]
    public void WithoutBackground_KeepsCornerRadius()
    {
        // winshot 语义里圆角属于内容呈现而非"背景板"；保留圆角让导出图仍有软边
        var stripped = EditorStateStripping.StripBackground(Styled);
        Assert.Equal(12f, stripped.CornerRadius);
    }

    [Fact]
    public void WithBackground_ReturnsEquivalentState()
    {
        var stripped = EditorStateStripping.StripBackground(Styled, includeBackground: true);
        Assert.Equal(Styled, stripped);
    }

    [Fact]
    public void DefaultState_IsUnchanged()
    {
        var stripped = EditorStateStripping.StripBackground(EditorState.Default);
        Assert.Equal(EditorState.Default, stripped);
    }
}
