using System.Windows;
using System.Windows.Controls;
using AiraAssistant.Infrastructure;
using AiraAssistant.UI.ViewModels;

namespace AiraAssistant.UI.Views;

/// <summary>
/// In-app settings editor (v1.1). Edits the shared AppConfig object graph
/// and persists it to appsettings.json on Save. Includes the live theme
/// picker (10 VIP themes apply instantly; saved on Save for next launch).
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel _vm;

    /// <summary>Always-on-top value the user saved (applied live by MainWindow).</summary>
    public bool AppliedAlwaysOnTop => _vm.AlwaysOnTop;

    /// <summary>True when provider/model/prompt/voice changed and an app restart is needed.</summary>
    public bool NeedsRestart => _vm.NeedsRestart;

    /// <summary>Theme name at dialog construction (Cancel restores it).</summary>
    private readonly string _originalTheme;

    public SettingsDialog(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        _originalTheme = ThemeEngine.CurrentName;
        DataContext = _vm;
        Closed += (_, _) => _vm.DisposePreview();
    }

    /// <summary>Live theme preview: applying happens while the selection moves.</summary>
    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not string name)
        {
            return;
        }
        try
        {
            ThemeEngine.Apply(name);
        }
        catch
        {
            // Theme is cosmetic — never break the dialog.
        }
    }

    protected override void OnSourceInitialized(System.EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowGlassHost.Apply(this);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_vm.Save(OpenAiKeyBox.Password, AnthropicKeyBox.Password))
            {
                DialogResult = true;
            }
            else
            {
                MessageBox.Show(this,
                    "Settings could not be saved (see logs).",
                    "Aira",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this,
                $"Settings could not be saved:\n{ex.Message}\n\n(Bad API key names or a read-only appsettings.json are the usual causes.)",
                "Aira",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        // Restore the theme the app was using before the dialog opened.
        try { ThemeEngine.Apply(_originalTheme); } catch { /* cosmetic */ }
        DialogResult = false;
    }
}
