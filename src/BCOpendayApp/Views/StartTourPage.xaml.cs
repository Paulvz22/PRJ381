using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

public partial class StartTourPage : BasePage
{
    public StartTourPage(StartTourViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
