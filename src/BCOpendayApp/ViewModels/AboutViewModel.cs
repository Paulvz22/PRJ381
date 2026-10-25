using BCOpendayApp.Infrastructure;
using BCOpendayApp.Panorama;
using BCOpendayApp.Scanner;
using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

public sealed class AboutViewModel : ViewModelBase
{
    public AboutViewModel(INavigationService navigation, IDialogService dialogs)
        : base(navigation, dialogs)
    {
        ExploreLocationsCommand = CreateCommand(() => Navigation.GoToAsync(Routes.Locations));
        HowItWorksCommand = CreateCommand(() => Navigation.GoToAsync(Routes.HowItWorks));
        VirtualTourCommand = CreateCommand(() => Navigation.GoToAsync(Routes.Pano));
        PanoramaDraftsCommand = CreateCommand(() => Navigation.GoToAsync(PanoramaRoutes.Drafts));
        StaffScannerCommand = CreateCommand(() => Navigation.GoToAsync(ScannerRoutes.Login));
    }

    public AsyncCommand ExploreLocationsCommand { get; }

    public AsyncCommand HowItWorksCommand { get; }

    /// <summary>Opens the bundled 360° panorama tour.</summary>
    public AsyncCommand VirtualTourCommand { get; }

    /// <summary>Opens the local-only panorama import and draft list.</summary>
    public AsyncCommand PanoramaDraftsCommand { get; }

    /// <summary>Opens the staff sign-in in front of the building scanner.</summary>
    public AsyncCommand StaffScannerCommand { get; }
}
