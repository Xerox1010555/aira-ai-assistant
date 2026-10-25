using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AiraAssistant.Models;
using AiraAssistant.UI.ViewModels;

namespace AiraAssistant.UI.Converters;

/// <summary>User messages right-align, everything else left-aligns.</summary>
public sealed class MessageAlignmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ChatMessageViewModel m && m.Author == MessageAuthor.User
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
