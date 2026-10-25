using BCOpendayApp.Panorama.ViewModels;
using BCOpendayApp.Views;

namespace BCOpendayApp.Panorama.Views;

public partial class PanoramaDraftsPage : BasePage
{
    public PanoramaDraftsPage(PanoramaDraftsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
