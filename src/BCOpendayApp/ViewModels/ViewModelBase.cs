using System.ComponentModel;
using System.Runtime.CompilerServices;
using BCOpendayApp.Infrastructure;
using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

/// <summary>Base class for every view model: change notification, navigation, dialogs, command helpers.</summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    protected ViewModelBase(INavigationService navigation, IDialogService dialogs)
    {
        Navigation = navigation;
        Dialogs = dialogs;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected INavigationService Navigation { get; }

    protected IDialogService Dialogs { get; }

    /// <summary>Called by the page every time it becomes visible (use it to refresh data).</summary>
    public virtual void OnAppearing()
    {
    }

    /// <summary>Called by the page when it is covered or left (use it to stop audio, timers ...).</summary>
    public virtual void OnDisappearing()
    {
    }

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected void OnPropertiesChanged(params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>Creates a command that runs <paramref name="execute"/> once at a time and reports failures to the user.</summary>
    protected AsyncCommand CreateCommand(Func<Task> execute) =>
        new AsyncCommand(execute, ReportCommandFailureAsync);

    /// <summary>Same, for commands that receive a CommandParameter of type <typeparamref name="T"/>.</summary>
    protected AsyncCommand CreateCommand<T>(Func<T, Task> execute) where T : class =>
        new AsyncCommand(
            parameter => parameter is T value ? execute(value) : Task.CompletedTask,
            ReportCommandFailureAsync);

    private Task ReportCommandFailureAsync(Exception exception) =>
        Dialogs.AlertAsync(
            "Unable to complete action",
            "Please try again. If this continues, restart the app.");
}
