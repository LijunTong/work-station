using System.Globalization;
using Avalonia.Data.Converters;

namespace WorkStation.App.Converters;

/// <summary>
/// 把绑定值与 ConverterParameter 做大小写不敏感的比较，返回 bool。
/// 设置页里用来给「当前选中的主题色/明暗」加选中样式类。
/// </summary>
public sealed class EqualConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
