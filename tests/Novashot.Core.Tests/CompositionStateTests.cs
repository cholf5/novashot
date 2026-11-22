using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Editor;
using Novashot.Core.Utils;
using SkiaSharp;

namespace Novashot.Core.Tests;

/// <summary>
/// 带合成状态（背景/padding/圆角/阴影/内衬/边框）的 Composition 管线语义测试。
/// 用 CPU 光栅断言像素级性质；所有渲染都是 1:1（无视口缩放）。
/// </summary>
public class CompositionStateTests
{
    private static SKBitmap SolidSource(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(color);
        return bmp;
    }

    private static AnnotationDocument PlainDoc(int w = 40, int h = 30)
    {
        var bmp = SolidSource(w, h, SKColors.DimGray);
        return new AnnotationDocument(bmp);
    }

    private static SKBitmap Render(AnnotationDocument doc, EditorState state, bool opaqueCanvas = true)
    {
        // opaqueCanvas=false 时不清底（保留 alpha=0），用来测透明背景
        var result = new SKBitmap(new SKImageInfo(doc.Width + 2 * 96, doc.Height + 2 * 96,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        if (opaqueCanvas) canvas.Clear(SKColors.Magenta); // 哨兵色：未被管线触碰的区域
        Composition.RenderScene(canvas, doc, state);
        canvas.Flush();
        return result;
    }

    private static SKBitmap RenderExact(AnnotationDocument doc, EditorState state)
    {
        var size = Composition.Measure(doc, state);
        var result = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        Composition.RenderScene(canvas, doc, state);
        canvas.Flush();
        return result;
    }

    [Fact]
    public void Default_state_matches_bare_rendering_pixel_for_pixel()
    {
        var doc = PlainDoc();
        using var bare = new SKBitmap(new SKImageInfo(doc.Width, doc.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bare))
            Composition.RenderScene(c, doc);
        using var viaState = RenderExact(doc, EditorState.Default);

        for (int y = 0; y < doc.Height; y += 3)
            for (int x = 0; x < doc.Width; x += 3)
                Assert.Equal(bare.GetPixel(x, y), viaState.GetPixel(x, y));
    }

    [Fact]
    public void Measure_includes_padding()
    {
        var doc = PlainDoc(40, 30);
        var size = Composition.Measure(doc, new EditorState { Padding = 12 });
        Assert.Equal(64, size.Width);
        Assert.Equal(54, size.Height);
    }

    [Fact]
    public void Solid_background_fills_content_outside_image()
    {
        var doc = PlainDoc(40, 30);
        var state = new EditorState
        {
            Background = BackgroundKind.SolidColor,
            BackgroundColor = SKColors.Lime,
            Padding = 10,
        };
        using var result = RenderExact(doc, state);

        Assert.Equal(SKColors.Lime, result.GetPixel(2, 2));           // 左上角落
        Assert.Equal(SKColors.Lime, result.GetPixel(58, 47));          // 右下角落（位图 60×50）
        Assert.Equal(SKColors.DimGray, result.GetPixel(25, 25));       // 图像中心
    }

    [Fact]
    public void Background_none_leaves_transparent_corners()
    {
        var doc = PlainDoc(40, 30);
        var state = new EditorState { Padding = 10 }; // 无背景
        using var result = RenderExact(doc, state);

        var corner = result.GetPixel(2, 2);
        Assert.Equal(0, corner.Alpha);                                  // 完全透明
        Assert.Equal(SKColors.DimGray, result.GetPixel(25, 25));
    }

    [Fact]
    public void Gradient_background_interpolates_between_corners()
    {
        var doc = PlainDoc(40, 30);
        var state = new EditorState
        {
            Background = BackgroundKind.Gradient,
            BackgroundGradient = GradientPresets.All[0].Value, // Sunset 135deg
            Padding = 10,
        };
        using var result = RenderExact(doc, state);

        // 135deg：起点左上（#f093fb 粉紫）→ 终点右下（#f5576c 红粉）
        var tl = result.GetPixel(1, 1);
        var br = result.GetPixel(58, 47);
        Assert.True(tl.Red > tl.Green, $"top-left should be pinkish, got {tl}");
        Assert.True(br.Blue < tl.Blue, $"bottom-right blue should drop along Sunset gradient: {tl} -> {br}");
        // 中间像素由插值而来：介于两端之间（宽松判断——只是确认不是角落同色）
        var mid = result.GetPixel(30, 27);
        Assert.NotEqual(tl, mid);
    }

    [Fact]
    public void Corner_radius_clips_image_corner_showing_background()
    {
        var doc = PlainDoc(40, 30);
        var state = new EditorState
        {
            Background = BackgroundKind.SolidColor,
            BackgroundColor = SKColors.Lime,
            Padding = 10,
            CornerRadius = 8f,
        };
        using var result = RenderExact(doc, state);

        // 图像直角 (10,10)（内容空间）处被裁掉 → 露出背景；半径 8 的圆弧覆盖到约 (11,11)
        Assert.Equal(SKColors.Lime, result.GetPixel(11, 11));
        // 图像中心不受影响
        Assert.Equal(SKColors.DimGray, result.GetPixel(25, 25));
    }

    [Fact]
    public void Outside_border_covers_edge_beyond_image()
    {
        var doc = PlainDoc(40, 30);
        var state = new EditorState
        {
            Background = BackgroundKind.SolidColor,
            BackgroundColor = SKColors.Lime,
            Padding = 10,
            BorderWidth = 6f,
            BorderColor = SKColors.Blue,
            Border = BorderStyle.Outside,
        };
        using var result = RenderExact(doc, state);

        // Outside：stroke 中心在图像边线外 3px 处（边线 -3）
        Assert.Equal(SKColors.Blue, result.GetPixel(7, 25));   // 左边线 x=10 - 3
        Assert.Equal(SKColors.Blue, result.GetPixel(53, 25));  // 右边线 x=50 + 3
        Assert.Equal(SKColors.DimGray, result.GetPixel(25, 25)); // 中心不变
    }

    [Fact]
    public void Inside_border_covers_innermost_edge_pixels()
    {
        var doc = PlainDoc(40, 30);
        var state = new EditorState
        {
            BorderWidth = 4f,
            BorderColor = SKColors.Blue,
            Border = BorderStyle.Inside,
        };
        using var result = RenderExact(doc, state);

        // Inside：stroke 中心在边线内 2px；覆盖到边线内 4px
        Assert.Equal(SKColors.Blue, result.GetPixel(2, 15));   // 左边内 2px
        Assert.Equal(SKColors.DimGray, result.GetPixel(10, 15)); // 内 10px 不受影响
    }

    [Fact]
    public void Inset_draws_color_ring_inside_edge()
    {
        var doc = PlainDoc(40, 30);
        var state = new EditorState
        {
            InsetSize = 4f,
            InsetColor = SKColors.Red,
        };
        using var result = RenderExact(doc, state);

        Assert.Equal(SKColors.Red, result.GetPixel(2, 15));    // 环带中心：边线内 2px
        Assert.Equal(SKColors.DimGray, result.GetPixel(10, 15));
    }

    [Fact]
    public void Shadow_darkens_padding_near_image()
    {
        var doc = PlainDoc(40, 30);
        var bgOnly = new EditorState
        {
            Background = BackgroundKind.SolidColor,
            BackgroundColor = SKColors.White,
            Padding = 20,
        };
        var withShadow = bgOnly with { ShadowSize = 12f };

        using var without = RenderExact(doc, bgOnly);
        using var with = RenderExact(doc, withShadow);

        // 紧贴图像左边（padding 20，图像左边 x=20 → 取 x=18，padding 区）应被阴影压暗
        var a = without.GetPixel(18, 25);
        var b = with.GetPixel(18, 25);
        Assert.True(b.Red < a.Red, $"shadow should darken: {a} -> {b}");
        // 远离图像（x=2）基本不受影响
        var farA = without.GetPixel(2, 25);
        var farB = with.GetPixel(2, 25);
        Assert.True(farB.Red > b.Red, $"far corner should be brighter than near edge: {farB} vs {b}");
    }

    [Fact]
    public void State_is_clamped_on_render()
    {
        var doc = PlainDoc(40, 30);
        var state = new EditorState { Padding = 9999, ShadowSize = 999f }.Clamped();
        var size = Composition.Measure(doc, state);
        // Padding clamp 到 96
        Assert.Equal(40 + 2 * EditorClampMax, size.Width);
    }

    private const int EditorClampMax = 96;
}
