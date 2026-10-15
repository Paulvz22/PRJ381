using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

public partial class HowItWorksPage : BasePage
{
    public HowItWorksPage(HowItWorksViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
