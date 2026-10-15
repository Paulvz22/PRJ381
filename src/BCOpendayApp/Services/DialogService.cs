namespace BCOpendayApp.Services;

public sealed class DialogService : IDialogService
{
    public Task AlertAsync(string title, string message, string cancel = "OK") =>
        Shell.Current.DisplayAlert(title, message, cancel);

    public Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "Cancel") =>
        Shell.Current.DisplayAlert(title, message, accept, cancel);
}
