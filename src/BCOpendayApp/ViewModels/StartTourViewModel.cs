using BCOpendayApp.AR;
using BCOpendayApp.Infrastructure;
using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

/// <summary>
/// "Start AR Tour" screen: opens the AR building guide, or a tour stop by typing its location code.
/// A future QR scanner only has to put the scanned text into <see cref="Code"/> and run <see cref="OpenCodeCommand"/>.
/// </summary>
public sealed class StartTourViewModel : ViewModelBase
{
    private readonly ILocationService locations;
    private string code = string.Empty;

    public StartTourViewModel(ILocationService locations, INavigationService navigation, IDialogService dialogs)
        : base(navigation, dialogs)
    {
        this.locations = locations;

        OpenArCommand = CreateCommand(() => Navigation.GoToAsync(ArRoutes.Viewer));
        OpenCodeCommand = CreateCommand(OpenCodeAsync);
        ChooseLocationCommand = CreateCommand(() => Navigation.GoToAsync(Routes.Locations));
        HowItWorksCommand = CreateCommand(() => Navigation.GoToAsync(Routes.HowItWorks));
    }

    public string Code
    {
        get => code;
        set => SetProperty(ref code, value ?? string.Empty);
    }

    /// <summary>Opens the camera-based AR building guide (AR/ folder).</summary>
    public AsyncCommand OpenArCommand { get; }

    public AsyncCommand OpenCodeCommand { get; }

    public AsyncCommand ChooseLocationCommand { get; }

    public AsyncCommand HowItWorksCommand { get; }

    private async Task OpenCodeAsync()
    {
        var stop = await locations.GetLocationAsync(CampusCatalog.StopIdFromCode(Code));
        if (stop is null)
        {
            await Dialogs.AlertAsync("Unknown location code", $"Use one of: {CampusCatalog.ValidCodes}.");
            return;
        }

        await Navigation.GoToStopAsync(Routes.StopViewer, stop.Id);
    }
}
