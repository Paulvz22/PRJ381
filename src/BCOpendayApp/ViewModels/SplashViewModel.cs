using BCOpendayApp.Infrastructure;
using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

public sealed class SplashViewModel : ViewModelBase
{
    private static readonly TimeSpan AutoContinueAfter = TimeSpan.FromSeconds(3);

    private CancellationTokenSource? delay;
    private bool hasEntered;

    public SplashViewModel(INavigationService navigation, IDialogService dialogs)
        : base(navigation, dialogs)
    {
        EnterCommand = CreateCommand(EnterAsync);
    }

    public AsyncCommand EnterCommand { get; }

    /// <summary>Moves on to the home page by itself after a short pause (or immediately via the button).</summary>
    public async Task AutoContinueAsync()
    {
        delay?.Cancel();
        delay = new CancellationTokenSource();

        try
        {
            await Task.Delay(AutoContinueAfter, delay.Token);
            await EnterAsync();
        }
        catch (OperationCanceledException)
        {
            // Page was left or the button was tapped first.
        }
    }

    public override void OnDisappearing() => delay?.Cancel();

    private async Task EnterAsync()
    {
        if (hasEntered)
        {
            return;
        }

        hasEntered = true;
        delay?.Cancel();
        await Navigation.GoToTabAsync(Routes.Home);
    }
}
