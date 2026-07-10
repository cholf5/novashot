using System.Globalization;
using Avalonia.Data.Converters;

namespace Prtsc.App.Converters;

/// <summary>
/// null → false / non-null → true。用于把可空引用属性映射到 <c>IsVisible</c> / <c>IsEnabled</c>。
/// ConvertBack 不支持。
/// </summary>
public sealed class NullToBooleanConverter : IValueConverter
{
    public static readonly NullToBooleanConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
