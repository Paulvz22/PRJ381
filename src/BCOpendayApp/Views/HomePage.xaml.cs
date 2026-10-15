using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

public partial class HomePage : BasePage
{
    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
