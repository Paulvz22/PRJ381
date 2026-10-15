using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

public partial class AboutPage : BasePage
{
    public AboutPage(AboutViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
