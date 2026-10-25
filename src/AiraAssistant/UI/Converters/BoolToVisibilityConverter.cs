using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AiraAssistant.UI.Converters;

/// <summary>Converts <c>true</c> to <see cref="Visibility.Visible"/> and <c>false</c> to <see cref="Visibility.Collapsed"/>.</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public Visibility TrueValue { get; set; } = Visibility.Visible;
    public Visibility FalseValue { get; set; } = Visibility.Collapsed;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool v && v;
        return b ? TrueValue : FalseValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility vis && vis == TrueValue;
}
