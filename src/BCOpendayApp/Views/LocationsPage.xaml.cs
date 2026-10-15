using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

public partial class LocationsPage : BasePage
{
    public LocationsPage(LocationsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
