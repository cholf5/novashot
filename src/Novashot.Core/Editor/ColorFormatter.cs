using SkiaSharp;

namespace Novashot.Core.Editor;

/// <summary>
/// E-1 M9：取色结果的文本格式。浮签显示与剪贴板复制共用同一格式，
/// 保证「所见 = 所粘贴」。HEX 小写对齐 <c>EditorConfigMapper.ToCssColor</c> 的既有风格；
/// alpha 不参与（屏幕像素一律不透明，半透明输入只取 RGB）。
/// </summary>
public static class ColorFormatter
{
    /// <summary>#rrggbb（小写，与 config 既有 CSS 风格一致；可被 SKColor.Parse 回读）。</summary>
    public static string ToHex(SKColor color)
        => $"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}";

    /// <summary>rgb(r, g, b)（CSS 函数式，逗号后带空格）。</summary>
    public static string ToRgb(SKColor color)
        => $"rgb({color.Red}, {color.Green}, {color.Blue})";
}
