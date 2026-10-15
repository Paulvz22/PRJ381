using BCOpendayApp.Scanner.ViewModels;
using BCOpendayApp.Views;

namespace BCOpendayApp.Scanner.Views;

public partial class ScannerLoginPage : BasePage
{
    public ScannerLoginPage(ScannerLoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
