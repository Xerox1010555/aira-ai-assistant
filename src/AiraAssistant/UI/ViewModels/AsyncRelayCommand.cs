using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AiraAssistant.UI.ViewModels;

/// <summary>
/// Asynchronous version of <see cref="RelayCommand"/>. Prevents re-entrancy
/// while the underlying task is in flight so the user can't double-click
/// a "Send" button and trigger two AI completions.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private readonly Predicate<object?>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
        : this(_ => executeAsync(), canExecute is null ? null : new Predicate<object?>(_ => canExecute()))
    {
    }

    public AsyncRelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        try
        {
            _isExecuting = true;
            CommandManager.InvalidateRequerySuggested();
            await _executeAsync(parameter).ConfigureAwait(true);
        }
        finally
        {
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
