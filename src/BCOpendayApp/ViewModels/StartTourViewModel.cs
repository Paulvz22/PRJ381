using BCOpendayApp.AR;
using BCOpendayApp.Infrastructure;
using BCOpendayApp.Panorama;
using BCOpendayApp.Scanner;
using BCOpendayApp.Scanner.Services;
using BCOpendayApp.Services;
using BCOpendayApp.Services.Campus;

namespace BCOpendayApp.ViewModels;

/// <summary>
/// The Tour tab: open the AR camera or the 360° views, jump to a location's 360° view by its code or from the
/// list, and (staff) start a new AR building scan or 360° capture.
/// A future QR scanner only has to put the scanned text into <see cref="Code"/> and run <see cref="OpenCodeCommand"/>.
/// </summary>
public sealed class StartTourViewModel : ViewModelBase
{
    private readonly ICampusLocationService campus;
    private readonly IScannerAuthService staff;
    private string code = string.Empty;

    public StartTourViewModel(
        ICampusLocationService campus,
        IScannerAuthService staff,
        INavigationService navigation,
        IDialogService dialogs)
        : base(navigation, dialogs)
    {
        this.campus = campus;
        this.staff = staff;

        OpenArCommand = CreateCommand(() => Navigation.GoToAsync(ArRoutes.Viewer));
        OpenPanoramaCommand = CreateCommand(() => Navigation.GoToAsync(Routes.Pano));
        OpenCodeCommand = CreateCommand(OpenCodeAsync);
        ChooseLocationCommand = CreateCommand(() =>
            Navigation.GoToAsync($"{Routes.Locations}?{LocationsViewModel.ModeParameter}={LocationsViewModel.PanoramaMode}"));
        NewArScanCommand = CreateCommand(() => Navigation.GoToAsync(staff.IsUnlocked ? ScannerRoutes.Capture : ScannerRoutes.Login));
        NewPanoramaCommand = CreateCommand(() => Navigation.GoToAsync(staff.IsUnlocked ? PanoramaRoutes.Capture : ScannerRoutes.Login));
    }

    public string Code
    {
        get => code;
        set => SetProperty(ref code, value ?? string.Empty);
    }

    public AsyncCommand OpenArCommand { get; }

    public AsyncCommand OpenPanoramaCommand { get; }

    public AsyncCommand OpenCodeCommand { get; }

    public AsyncCommand ChooseLocationCommand { get; }

    public AsyncCommand NewArScanCommand { get; }

    public AsyncCommand NewPanoramaCommand { get; }

    private async Task OpenCodeAsync()
    {
        var list = await campus.GetAllAsync();
        var location = CampusLocationCatalog.FindByCode(list.Locations, Code);
        if (location is null)
        {
            var examples = string.Join(", ", list.Locations.Take(3).Select(item => item.Code));
            await Dialogs.AlertAsync(
                "Unknown location code",
                examples.Length > 0 ? $"Codes look like {examples}. See Choose from locations for all of them." : "There are no locations yet.");
            return;
        }

        if (location.FirstViewId is { } viewId)
        {
            await Navigation.GoToStopAsync(Routes.Pano, viewId);
        }
        else
        {
            // AR scan only: show the location page, which offers the AR camera.
            await Navigation.GoToStopAsync(Routes.LocationDetail, location.Id);
        }
    }
}
