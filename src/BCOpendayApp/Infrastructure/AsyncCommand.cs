using System.Diagnostics;
using System.Windows.Input;

namespace BCOpendayApp.Infrastructure;

/// <summary>
/// An ICommand for async work. While it is running it reports CanExecute = false, so a bound Button
/// disables itself and a double-tap cannot start the action twice. Exceptions never escape (an
/// unhandled exception in an async void would crash the app); they go to <c>onError</c> instead.
/// </summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> execute;
    private readonly Func<Exception, Task>? onError;
    private bool isRunning;

    public AsyncCommand(Func<Task> execute, Func<Exception, Task>? onError = null)
        : this(_ => execute(), onError)
    {
    }

    public AsyncCommand(Func<object?, Task> execute, Func<Exception, Task>? onError = null)
    {
        this.execute = execute;
        this.onError = onError;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !isRunning;

    public async void Execute(object? parameter)
    {
        if (isRunning)
        {
            return;
        }

        SetRunning(true);
        try
        {
            await execute(parameter);
        }
        catch (OperationCanceledException)
        {
            // Cancelled on purpose (e.g. audio stopped) - not an error.
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            await ReportAsync(ex);
        }
        finally
        {
            SetRunning(false);
        }
    }

    private async Task ReportAsync(Exception ex)
    {
        if (onError is null)
        {
            return;
        }

        try
        {
            await onError(ex);
        }
        catch (Exception reportingFailure)
        {
            Debug.WriteLine(reportingFailure);
        }
    }

    private void SetRunning(bool value)
    {
        isRunning = value;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
