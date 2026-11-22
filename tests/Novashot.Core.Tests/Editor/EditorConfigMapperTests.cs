using Novashot.Core.Config;
using Novashot.Core.Editor;
using SkiaSharp;

namespace Novashot.Core.Tests;

/// <summary>
/// E-2 画布参数持久化：EditorState ↔ EditorConfig 双向映射。
/// config.editor 段语义 = 「上次使用的画布参数」：FromConfig 出初始态（含 showBackground 激活），
/// ApplyTo 回写（关态清零数值/开关、保留颜色备下次开启）。
/// </summary>
public class EditorConfigMapperTests
{
    private const string GradientCss = "linear-gradient(135deg, #667eea 0%, #764ba2 100%)";

    private static EditorConfig NewConfig() => new()
    {
        Padding = 24,
        CornerRadius = 8,
        ShadowSize = 12,
        BackgroundColor = GradientCss,
        ShowBackground = true,
        Inset = 4,
        InsetBackgroundColor = "#00ff00",
        BorderEnabled = true,
        BorderWeight = 3,
        BorderColor = "#123456",
        BorderType = "outside",
    };

    // ---- FromConfig（config → 初始 EditorState）----

    [Fact]
    public void FromConfig_MapsAppearanceFields()
    {
        var state = EditorConfigMapper.FromConfig(NewConfig());

        Assert.Equal(BackgroundKind.Gradient, state.Background);
        Assert.Equal(GradientCss, state.BackgroundGradient);
        Assert.Equal(24, state.Padding);
        Assert.Equal(8f, state.CornerRadius);
        Assert.Equal(12f, state.ShadowSize);
        Assert.Equal(4f, state.InsetSize);
        Assert.Equal(SKColors.Lime, state.EffectiveInsetColor);
        Assert.Equal(3f, state.BorderWidth);
        Assert.Equal(new SKColor(0x12, 0x34, 0x56), state.EffectiveBorderColor);
        Assert.Equal(BorderStyle.Outside, state.Border);
    }

    [Fact]
    public void FromConfig_SolidColor_ParsesColor()
    {
        var cfg = NewConfig();
        cfg.BackgroundColor = "#ff0000";

        var state = EditorConfigMapper.FromConfig(cfg);

        Assert.Equal(BackgroundKind.SolidColor, state.Background);
        Assert.Equal(SKColors.Red, state.BackgroundColor);
        Assert.Null(state.BackgroundGradient);
    }

    [Fact]
    public void FromConfig_ShowBackgroundFalse_YieldsNoneBackground()
    {
        var cfg = NewConfig();
        cfg.ShowBackground = false;

        var state = EditorConfigMapper.FromConfig(cfg);

        Assert.Equal(BackgroundKind.None, state.Background);
    }

    [Fact]
    public void FromConfig_IncludeBackgroundFalse_StripsFrame()
    {
        var state = EditorConfigMapper.FromConfig(NewConfig(), includeBackground: false);

        Assert.Equal(BackgroundKind.None, state.Background);
        Assert.Equal(0, state.Padding);
        Assert.Equal(0f, state.ShadowSize);
        Assert.Equal(0f, state.BorderWidth);
        // 圆角保留（G-14 语义）
        Assert.Equal(8f, state.CornerRadius);
    }

    // ---- ApplyTo（EditorState → config 回写）----

    [Fact]
    public void ApplyTo_FromConfig_RoundTripsAppearance()
    {
        var original = new EditorState(
            Background: BackgroundKind.Gradient,
            BackgroundGradient: GradientCss,
            Padding: 24,
            CornerRadius: 8,
            ShadowSize: 12,
            InsetSize: 4,
            InsetColor: SKColors.Lime,
            BorderWidth: 3,
            BorderColor: new SKColor(0x12, 0x34, 0x56),
            Border: BorderStyle.Outside);

        var cfg = new EditorConfig();
        EditorConfigMapper.ApplyTo(original, cfg);
        var restored = EditorConfigMapper.FromConfig(cfg);

        Assert.Equal(original.Background, restored.Background);
        Assert.Equal(original.BackgroundGradient, restored.BackgroundGradient);
        Assert.Equal(original.Padding, restored.Padding);
        Assert.Equal(original.CornerRadius, restored.CornerRadius);
        Assert.Equal(original.ShadowSize, restored.ShadowSize);
        Assert.Equal(original.InsetSize, restored.InsetSize);
        Assert.Equal(original.EffectiveInsetColor, restored.EffectiveInsetColor);
        Assert.Equal(original.BorderWidth, restored.BorderWidth);
        Assert.Equal(original.EffectiveBorderColor, restored.EffectiveBorderColor);
        Assert.Equal(original.Border, restored.Border);
    }

    [Fact]
    public void ApplyTo_SolidColor_WritesRgbHex()
    {
        var cfg = new EditorConfig();

        EditorConfigMapper.ApplyTo(
            new EditorState(Background: BackgroundKind.SolidColor, BackgroundColor: SKColors.Red), cfg);

        Assert.True(cfg.ShowBackground);
        Assert.Equal("#ff0000", cfg.BackgroundColor);
    }

    [Fact]
    public void ApplyTo_AlphaColor_WritesCssRgbaHex()
    {
        var cfg = new EditorConfig();
        var color = new SKColor(255, 0, 0, 128);

        EditorConfigMapper.ApplyTo(
            new EditorState(Background: BackgroundKind.SolidColor, BackgroundColor: color), cfg);

        // CSS #rrggbbaa：alpha 在后
        Assert.Equal("#ff000080", cfg.BackgroundColor);
        Assert.Equal(color, GradientParser.ParseColor(cfg.BackgroundColor));
    }

    [Fact]
    public void ApplyTo_DefaultColor_FallsBackToWhite()
    {
        var cfg = new EditorConfig();

        EditorConfigMapper.ApplyTo(new EditorState(Background: BackgroundKind.SolidColor), cfg);

        Assert.Equal("#ffffff", cfg.BackgroundColor);
    }

    [Fact]
    public void ApplyTo_NoneBackground_ClearsShowBackground_KeepsColor()
    {
        var cfg = NewConfig();

        EditorConfigMapper.ApplyTo(new EditorState(Background: BackgroundKind.None, Padding: 10), cfg);

        Assert.False(cfg.ShowBackground);
        Assert.Equal(GradientCss, cfg.BackgroundColor); // 颜色保留，切回时还在
        Assert.Equal(10, cfg.Padding);
    }

    [Fact]
    public void ApplyTo_EffectsOff_ClearsValues_KeepsColors()
    {
        var cfg = NewConfig();

        EditorConfigMapper.ApplyTo(new EditorState(Background: BackgroundKind.None), cfg);

        Assert.Equal(0, cfg.ShadowSize);  // 阴影无开关，0=关
        Assert.Equal(0, cfg.Inset);
        Assert.Equal("#00ff00", cfg.InsetBackgroundColor); // 保留
        Assert.False(cfg.BorderEnabled);
        Assert.Equal(3, cfg.BorderWeight);   // 保留
        Assert.Equal("#123456", cfg.BorderColor);
        Assert.Equal("outside", cfg.BorderType);
    }

    [Fact]
    public void ApplyTo_GradientNull_KeepsExistingConfigGradient()
    {
        var cfg = NewConfig();

        EditorConfigMapper.ApplyTo(new EditorState(Background: BackgroundKind.Gradient, BackgroundGradient: null), cfg);

        Assert.True(cfg.ShowBackground);
        Assert.Equal(GradientCss, cfg.BackgroundColor); // 畸形兜底：不抹掉可用值
    }
}
