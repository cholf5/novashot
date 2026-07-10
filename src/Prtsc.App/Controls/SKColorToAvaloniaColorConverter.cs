using System.Globalization;
using Avalonia.Data.Converters;
using SkiaSharp;

namespace Prtsc.App.Controls;

/// <summary>
/// <see cref="SKColor"/> → Avalonia <see cref="Avalonia.Media.Color"/>。
/// 供 XAML 里把 Skia 色值当 Brush 用。ConvertBack 不支持。
/// </summary>
public sealed class SKColorToAvaloniaColorConverter : IValueConverter
{
    public static readonly SKColorToAvaloniaColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SKColor c) return Avalonia.Media.Colors.Transparent;
        return Avalonia.Media.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
