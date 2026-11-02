using Prtsc.Core.Editor;
using Prtsc.Core.Utils;
using SkiaSharp;

namespace Prtsc.Core.Tests;

public class GradientParserTests
{
    [Fact]
    public void Parses_two_stop_gradient()
    {
        var ok = GradientParser.TryParse("linear-gradient(135deg, #f093fb 0%, #f5576c 100%)", out var spec);
        Assert.True(ok);
        Assert.Equal(135f, spec.AngleDeg, 3);
        Assert.Equal(2, spec.Stops.Count);
        Assert.Equal(0f, spec.Stops[0].Pos, 3);
        Assert.Equal(100f, spec.Stops[1].Pos, 3);
        Assert.Equal(new SKColor(0xf0, 0x93, 0xfb), spec.Stops[0].Color);
        Assert.Equal(new SKColor(0xf5, 0x57, 0x6c), spec.Stops[1].Color);
    }

    [Fact]
    public void Parses_three_stop_gradient()
    {
        var ok = GradientParser.TryParse("linear-gradient(135deg, #0f0c29 0%, #302b63 50%, #24243e 100%)", out var spec);
        Assert.True(ok);
        Assert.Equal(3, spec.Stops.Count);
        Assert.Equal(50f, spec.Stops[1].Pos, 3);
        Assert.Equal(new SKColor(0x30, 0x2b, 0x63), spec.Stops[1].Color);
    }

    [Fact]
    public void Parses_short_hex()
    {
        var ok = GradientParser.TryParse("linear-gradient(90deg, #f00 0%, #00f 100%)", out var spec);
        Assert.True(ok);
        Assert.Equal(new SKColor(255, 0, 0), spec.Stops[0].Color);
        Assert.Equal(new SKColor(0, 0, 255), spec.Stops[1].Color);
    }

    [Fact]
    public void Omits_positions_get_backfilled()
    {
        // 位置省略：首 0 / 尾 100
        var ok = GradientParser.TryParse("linear-gradient(45deg, #ff0000, #0000ff)", out var spec);
        Assert.True(ok);
        Assert.Equal(0f, spec.Stops[0].Pos, 3);
        Assert.Equal(100f, spec.Stops[1].Pos, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a gradient")]
    [InlineData("linear-gradient(45deg)")]              // 无色标
    [InlineData("linear-gradient(45deg, #fff)")]        // 单色标
    [InlineData("linear-gradient(turn, #fff 0%, #000 100%)")] // 角度畸形
    [InlineData("linear-gradient(45deg, red 0%, blue 100%)")] // 非十六进制色
    [InlineData("linear-gradient(45deg, #fff 0, #000 100%)")] // 位置缺 %
    public void Malformed_input_returns_false(string css)
    {
        Assert.False(GradientParser.TryParse(css, out _));
    }

    [Fact]
    public void All_24_presets_parse()
    {
        Assert.Equal(24, GradientPresets.All.Count);
        foreach (var preset in GradientPresets.All)
        {
            Assert.True(GradientParser.TryParse(preset.Value, out var spec),
                $"preset {preset.Name} failed to parse: {preset.Value}");
            Assert.True(spec.AngleDeg is >= 0f and <= 360f);
            Assert.True(spec.Stops.Count >= 2);
        }
    }

    [Fact]
    public void ParseColor_eight_digit_css_alpha_last()
    {
        // CSS #rrggbbaa 契约：alpha 在后。SKColor.Parse 原生 8 位按 #aarrggbb（alpha 在前）读，
        // ParseColor 需自行纠正，否则 E-2 回写的带 alpha 颜色通道错位。
        var color = GradientParser.ParseColor("#ff000080");
        Assert.Equal(new SKColor(255, 0, 0, 128), color);
    }

    [Fact]
    public void ParseColor_garbage_returns_null()
    {
        Assert.Null(GradientParser.ParseColor(null));
        Assert.Null(GradientParser.ParseColor(""));
        Assert.Null(GradientParser.ParseColor("red"));
        Assert.Null(GradientParser.ParseColor("#gg0000"));
    }
}
