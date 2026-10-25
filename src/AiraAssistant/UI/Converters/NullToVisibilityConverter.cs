using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AiraAssistant.UI.Converters;

/// <summary>
/// Collapses when the bound string is null OR blank, otherwise Visible.
/// v1.1: the old converter only checked <c>null</c>, so an empty partial
/// transcript still reserved a visible row in the composer.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
