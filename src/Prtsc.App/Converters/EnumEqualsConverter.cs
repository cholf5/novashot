using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Prtsc.App.Converters;

/// <summary>
/// 通用枚举值相等比较：<c>value?.Equals(parameter) == true</c> → true。
///
/// 主要用途：把 RadioButton.IsChecked 单向绑定到当前 CurrentTool 值，
/// <c>ConverterParameter</c> 传枚举字面量或字符串（都支持）。
/// ConvertBack 一律返回 <see cref="BindingOperations.DoNothing"/>，避免 OneWay 绑定的写回噪音。
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        if (value.Equals(parameter)) return true;
        // XAML 传入的 ConverterParameter 若为字符串，兜底一次名字比较
        if (parameter is string s && value is Enum e)
            return string.Equals(e.ToString(), s, StringComparison.Ordinal);
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
