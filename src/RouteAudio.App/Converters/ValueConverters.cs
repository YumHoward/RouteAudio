using System.Windows.Data;
using System.Windows;
using System.Globalization;

namespace RouteAudio.App.Converters;

/// <summary>字符串为空时可见（用于输入框占位提示）；Invert=true 时为空则隐藏。</summary>
public sealed class StringEmptyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = string.IsNullOrEmpty(value as string);
        if (Invert) isEmpty = !isEmpty;
        return isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool → Visibility；Invert=true 时取反。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
