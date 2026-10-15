using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

public partial class StopGalleryPage : BasePage
{
    public StopGalleryPage(GalleryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
