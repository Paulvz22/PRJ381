using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

public partial class LocationDetailPage : BasePage
{
    public LocationDetailPage(LocationDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
