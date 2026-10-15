namespace BCOpendayApp.Services;

/// <summary>Shows simple alerts from view models.</summary>
public interface IDialogService
{
    Task AlertAsync(string title, string message, string cancel = "OK");

    /// <summary>Yes/no question; true when the user picks <paramref name="accept"/>.</summary>
    Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "Cancel");
}
