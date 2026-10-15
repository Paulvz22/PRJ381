using BCOpendayApp.Infrastructure;
using BCOpendayApp.Scanner.Services;
using BCOpendayApp.Services;
using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Scanner.ViewModels;

/// <summary>Staff-only gate in front of the building scanner.</summary>
public sealed class ScannerLoginViewModel : ViewModelBase
{
    private readonly IScannerAuthService auth;
    private string email;
    private string password = string.Empty;
    private string errorMessage = string.Empty;
    private bool isBusy;

    public ScannerLoginViewModel(IScannerAuthService auth, INavigationService navigation, IDialogService dialogs)
        : base(navigation, dialogs)
    {
        this.auth = auth;
        email = auth.RememberedEmail;
        SignInCommand = CreateCommand(SignInAsync);
        CancelCommand = CreateCommand(() => Navigation.GoBackAsync());
    }

    public string Email
    {
        get => email;
        set => SetProperty(ref email, value ?? string.Empty);
    }

    public string Password
    {
        get => password;
        set => SetProperty(ref password, value ?? string.Empty);
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (SetProperty(ref errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => errorMessage.Length > 0;

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public AsyncCommand SignInCommand { get; }

    public AsyncCommand CancelCommand { get; }

    public override void OnAppearing()
    {
        Password = string.Empty;
        ErrorMessage = string.Empty;
    }

    private async Task SignInAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var problem = await auth.SignInAsync(Email, Password);
            Password = string.Empty;
            if (problem is not null)
            {
                ErrorMessage = problem;
                return;
            }

            // Replace the login page with the scanner home so "back" leaves the scanner.
            await Navigation.GoToAsync($"../{ScannerRoutes.Home}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
