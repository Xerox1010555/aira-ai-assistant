using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using AiraAssistant.Services.History;
using AiraAssistant.UI.Views;

namespace AiraAssistant.UI.ViewModels;

/// <summary>
/// Backing model for the Sessions dialog: browse the last 30 conversations,
/// open one into the main chat, start empty, rename or delete.
/// </summary>
public sealed class SessionsViewModel : ViewModelBase
{
    private readonly HistoryStore _history;
    private readonly MainViewModel _main;
    private ConversationSession? _selected;
    private string _statusText = string.Empty;

    public ObservableCollection<ConversationSession> Sessions { get; } = new();

    public ConversationSession? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public ICommand OpenCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand DeleteCommand { get; }

    /// <summary>Raised when the UI should close the dialog (open/new completed).</summary>
    public event Action? CloseRequested;

    public SessionsViewModel(HistoryStore history, MainViewModel main)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _main = main ?? throw new ArgumentNullException(nameof(main));

        OpenCommand = new RelayCommand(Open, () => Selected is not null);
        NewCommand = new RelayCommand(New);
        RenameCommand = new RelayCommand(Rename, () => Selected is not null);
        DeleteCommand = new RelayCommand(Delete, () => Selected is not null);

        Refresh();
    }

    public void Refresh()
    {
        Sessions.Clear();
        foreach (var session in _history.ListSessions(30))
        {
            Sessions.Add(session);
        }
        StatusText = $"{Sessions.Count} session(s) stored locally.";
    }

    private void Open()
    {
        if (Selected is null) return;
        try
        {
            _main.SwitchToSession(Selected.Id);
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = $"Open failed: {ex.Message}";
        }
    }

    private void New()
    {
        _main.StartEmptySession();
        CloseRequested?.Invoke();
    }

    private void Rename()
    {
        if (Selected is null) return;
        var owner = Application.Current?.MainWindow;
        var name = InputPrompt.Show(owner, "Rename session", Selected.Title);
        if (string.IsNullOrWhiteSpace(name)) return;

        _history.RenameSession(Selected.Id, name);
        Refresh();
        Selected = null;
        StatusText = "Renamed.";
    }

    private void Delete()
    {
        if (Selected is null) return;
        var answer = MessageBox.Show(
            Application.Current?.MainWindow,
            "Delete this session and all of its messages permanently?",
            "Aira — Delete session",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        _history.DeleteSession(Selected.Id);
        if (_main.IsCurrentSession(Selected.Id))
        {
            _main.StartEmptySession();
            StatusText = "Deleted the active session; started a new one.";
        }
        else
        {
            StatusText = "Deleted.";
        }
        Refresh();
        Selected = null;
    }
}
