using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace AiraAssistant.Infrastructure;

/// <summary>
/// Central UI-thread marshaling helper. Background services (System.Speech,
/// NAudio, HttpClient continuations) raise events on worker threads; all
/// mutations of view-model state therefore funnel through <see cref="Ui.Post"/>
/// / <see cref="Ui.InvokeAsync{T}"/>. This fixes avoidable cross-thread
/// binding exceptions that the previous build could hit.
/// </summary>
internal static class Ui
{
    private static Dispatcher? Dispatcher => System.Windows.Application.Current?.Dispatcher;

    /// <summary>Runs the action on the UI thread (async, never blocks the caller).</summary>
    public static void Post(Action action)
    {
        if (action is null) return;
        var d = Dispatcher;
        if (d is null || d.CheckAccess())
        {
            action();
        }
        else
        {
            _ = d.BeginInvoke(action);
        }
    }

    /// <summary>Runs the action on the UI thread and completes when it finished.</summary>
    public static Task InvokeAsync(Action action)
    {
        if (action is null) return Task.CompletedTask;
        var d = Dispatcher;
        if (d is null || d.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = d.BeginInvoke(new Action(() =>
        {
            try
            {
                action();
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }));
        return tcs.Task;
    }

    /// <summary>Runs the delegate on the UI thread and returns its value.</summary>
    public static Task<T> InvokeAsync<T>(Func<T> func)
    {
        if (func is null) throw new ArgumentNullException(nameof(func));
        var d = Dispatcher;
        if (d is null || d.CheckAccess())
        {
            return Task.FromResult(func());
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = d.BeginInvoke(new Action(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }));
        return tcs.Task;
    }
}
