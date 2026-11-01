using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Prtsc.Core.Editor;

namespace Prtsc.App.Converters;

/// <summary>
/// CSS <c>linear-gradient(...)</c> 字符串 → Avalonia <see cref="LinearGradientBrush"/>
/// （RelativeMapping）。供 24 渐变预设格子做背景预览。解析失败 → 深灰纯色。
/// </summary>
public sealed class CssGradientToBrushConverter : IValueConverter
{
    public static readonly CssGradientToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string css || !GradientParser.TryParse(css, out var spec) || spec.Stops.Count < 2)
            return new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));

        var rad = (float)(spec.AngleDeg * Math.PI / 180.0);
        var dx = Math.Sin(rad);
        var dy = -Math.Cos(rad);
        var half = 0.5;
        var start = new RelativePoint(0.5 - dx * half, 0.5 - dy * half, RelativeUnit.Relative);
        var end = new RelativePoint(0.5 + dx * half, 0.5 + dy * half, RelativeUnit.Relative);

        var brush = new LinearGradientBrush
        {
            // Avalonia 12：相对/绝对映射由端点的 RelativeUnit 决定，无独立 MappingMode
            StartPoint = start,
            EndPoint = end,
        };
        foreach (var st in spec.Stops)
        {
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(st.Color.Alpha, st.Color.Red, st.Color.Green, st.Color.Blue),
                Math.Clamp(st.Pos / 100.0, 0.0, 1.0)));
        }
        return brush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
