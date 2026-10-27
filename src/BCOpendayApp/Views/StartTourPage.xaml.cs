using BCOpendayApp.Services;
using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

public partial class StartTourPage : BasePage
{
    public StartTourPage(StartTourViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>Tour is a tab: Back returns to Home instead of closing the app.</summary>
    protected override bool OnBackButtonPressed()
    {
        Dispatcher.Dispatch(() => _ = Shell.Current.GoToAsync("//" + Routes.Home));
        return true;
    }
}
