using BCOpendayApp.Infrastructure;
using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

public sealed class HowItWorksViewModel : ViewModelBase
{
    public HowItWorksViewModel(INavigationService navigation, IDialogService dialogs)
        : base(navigation, dialogs)
    {
        StartExploringCommand = CreateCommand(() => Navigation.GoToTabAsync(Routes.Tour));
    }

    public AsyncCommand StartExploringCommand { get; }
}
