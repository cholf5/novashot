using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Novashot.App.Converters;

/// <summary>
/// "Icon.Rect" 这类资源 key → <see cref="StreamGeometry"/>。
/// 图标几何是静态资源（不随主题/语言变），DynamicResource 又不支持绑定 key，
/// 所以走运行时资源查找。
/// </summary>
public sealed class IconKeyToGeometryConverter : IValueConverter
{
    public static readonly IconKeyToGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key
           && Application.Current?.TryGetResource(key, ThemeVariant.Default, out var geo) == true
           && geo is StreamGeometry g
            ? g
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
