using BCOpendayApp.Scanner.ViewModels;
using BCOpendayApp.Views;

namespace BCOpendayApp.Scanner.Views;

public partial class ScannerCapturePage : BasePage
{
    public ScannerCapturePage(ScannerCaptureViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
