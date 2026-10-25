using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AiraAssistant.Models;
using AiraAssistant.UI.ViewModels;

namespace AiraAssistant.UI.Converters;

/// <summary>Selects a background brush based on who authored the message.</summary>
public sealed class MessageBackgroundConverter : IValueConverter
{
    public Brush UserBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x0B, 0x6B, 0xE3));
    public Brush AssistantBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x31));
    public Brush SystemBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ChatMessageViewModel m) return SystemBrush;
        return m.Author switch
        {
            MessageAuthor.User => UserBrush,
            MessageAuthor.Assistant => AssistantBrush,
            _ => SystemBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
