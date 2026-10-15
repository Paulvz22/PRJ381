using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

public partial class TourPage : BasePage
{
    public TourPage(TourViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
