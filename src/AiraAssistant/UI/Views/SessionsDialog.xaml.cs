using System;
using System.Windows;
using AiraAssistant.Infrastructure;
using AiraAssistant.UI.ViewModels;

namespace AiraAssistant.UI.Views;

/// <summary>
/// Sessions browser (v1.1): switch between stored conversations, rename,
/// delete, or start a fresh one. VM closes the dialog via CloseRequested.
/// </summary>
public partial class SessionsDialog : Window
{
    public SessionsDialog(SessionsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += () =>
        {
            DialogResult = true;
            Close();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowGlassHost.Apply(this);
    }
}
