using BCOpendayApp.Scanner.ViewModels;
using BCOpendayApp.Views;

namespace BCOpendayApp.Scanner.Views;

public partial class ScannerHomePage : BasePage
{
    public ScannerHomePage(ScannerHomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
