using BCOpendayApp.ViewModels;
using IOSPage = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page;

namespace BCOpendayApp.Views;

/// <summary>
/// Base class for every page. Forwards appear/disappear to the view model (so it can refresh data or
/// stop audio) and keeps content out from under the iPhone notch.
/// </summary>
public abstract class BasePage : ContentPage
{
    protected BasePage()
    {
        IOSPage.SetUseSafeArea(this, true);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        (BindingContext as ViewModelBase)?.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        (BindingContext as ViewModelBase)?.OnDisappearing();
        base.OnDisappearing();
    }
}
