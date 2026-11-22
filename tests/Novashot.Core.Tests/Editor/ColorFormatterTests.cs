using Novashot.Core.Editor;
using SkiaSharp;

namespace Novashot.Core.Tests;

/// <summary>
/// E-1 M9：取色结果的两种文本格式。
/// HEX 大小写对齐 EditorConfigMapper.ToCssColor 的既有风格（小写 #rrggbb），
/// 浮签与剪贴板共用同一格式（复制后任意输入框可粘贴）。
/// </summary>
public class ColorFormatterTests
{
    [Theory]
    [InlineData(0x00, 0x00, 0x00, "#000000")]
    [InlineData(0xFF, 0xFF, 0xFF, "#ffffff")]
    [InlineData(0xE5, 0x3E, 0x3E, "#e53e3e")]
    [InlineData(0x0A, 0x0B, 0x0C, "#0a0b0c")]
    public void ToHex_IsLowercaseRrggbb(byte r, byte g, byte b, string expected)
    {
        Assert.Equal(expected, ColorFormatter.ToHex(new SKColor(r, g, b)));
    }

    [Fact]
    public void ToHex_IgnoresAlpha()
    {
        // 屏幕像素一律不透明；半透明输入（如 UI 误传）只取 RGB
        Assert.Equal("#123456", ColorFormatter.ToHex(new SKColor(0x12, 0x34, 0x56, 0x80)));
    }

    [Fact]
    public void ToHex_IsRoundTrippableByCssParsers()
    {
        var color = new SKColor(0xAB, 0xCD, 0xEF);
        Assert.Equal(color, SKColor.Parse(ColorFormatter.ToHex(color)));
    }

    [Theory]
    [InlineData(0x00, 0x00, 0x00, "rgb(0, 0, 0)")]
    [InlineData(0xFF, 0xFF, 0xFF, "rgb(255, 255, 255)")]
    [InlineData(0x10, 0xB9, 0x81, "rgb(16, 185, 129)")]
    public void ToRgb_UsesCssFunctionStyle_WithSpacesAfterCommas(byte r, byte g, byte b, string expected)
    {
        Assert.Equal(expected, ColorFormatter.ToRgb(new SKColor(r, g, b)));
    }

    [Fact]
    public void ToRgb_IgnoresAlpha()
    {
        Assert.Equal("rgb(1, 2, 3)", ColorFormatter.ToRgb(new SKColor(1, 2, 3, 0x40)));
    }
}
