using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

public partial class StopViewerPage : BasePage
{
    public StopViewerPage(StopViewerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
