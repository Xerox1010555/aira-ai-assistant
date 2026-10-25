using System;
using System.Windows;
using System.Windows.Controls;

namespace AiraAssistant.UI.Views;

/// <summary>
/// Tiny modal text prompt (used for "rename session"). Built in code so no
/// extra XAML resource exists for a one-field dialog.
/// </summary>
public static class InputPrompt
{
    public static string? Show(Window? owner, string title, string initial)
    {
        var box = new TextBox
        {
            Text = initial ?? string.Empty,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = "OK", IsDefault = true, Width = 88, Margin = new Thickness(0, 14, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 88, Margin = new Thickness(0, 14, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(box);
        root.Children.Add(buttons);

        var window = new Window
        {
            Title = title,
            Owner = owner,
            Width = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.Height,
            Content = root
        };

        ok.Click += (_, _) => window.DialogResult = true;

        _ = window.ShowDialog();
        return window.DialogResult == true ? box.Text.Trim() : null;
    }
}
