using System;
using System.Globalization;
using System.Windows.Data;

namespace AiraAssistant.UI.Converters;

/// <summary>
/// When the value is <c>true</c> (we ARE listening) returns the "stop" label;
/// when <c>false</c> returns the "start" label.
/// v1.1: rebuilt as explicit surrogate-pair escapes after the original file
/// shipped with mojibake (encoding-corrupted) glyph strings.
/// 🎙 = U+1F3A4 (D83C DFA4), ⏹ = U+23F9.
/// </summary>
public sealed class BoolToMicLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var listening = value is bool b && b;
        return listening ? "\u23F9" : "\uD83C\uDFA4";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
