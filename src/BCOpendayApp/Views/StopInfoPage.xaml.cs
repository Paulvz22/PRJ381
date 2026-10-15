using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

public partial class StopInfoPage : BasePage
{
    public StopInfoPage(StopInfoViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
