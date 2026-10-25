using System;
using System.Globalization;
using System.Windows.Data;

namespace AiraAssistant.UI.Converters;

/// <summary>Negates a boolean. Useful for <c>IsEnabled</c> bindings against a flag's inverse.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
