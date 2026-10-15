using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

public partial class SplashPage : BasePage
{
    private readonly SplashViewModel viewModel;

    public SplashPage(SplashViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.AutoContinueAsync();
    }
}
