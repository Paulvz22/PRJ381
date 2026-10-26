using BCOpendayApp.Infrastructure;
using BCOpendayApp.Models;
using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

public sealed class TourViewModel : CatalogViewModelBase
{
    private IReadOnlyList<StopItemViewModel> stops = Array.Empty<StopItemViewModel>();
    private Hotspot? nextStop;
    private string progressText = string.Empty;
    private double progress;

    public TourViewModel(
        ILocationService locationService,
        IVisitorProgressService visitorProgress,
        INavigationService navigation,
        IDialogService dialogs)
        : base(locationService, visitorProgress, navigation, dialogs)
    {
        ContinueCommand = CreateCommand(() => nextStop is null ? Task.CompletedTask : OpenStopAsync(nextStop));
        Open360TourCommand = CreateCommand(async () =>
        {
            if (!TourLauncher.Launch())
                await dialogs.AlertAsync("Tour not installed",
                    "Please install the Campus Tour app first.");
        });
    }

    public string Subtitle => Locations.Count == 0
        ? "A self-guided journey through the campus stops."
        : $"A self-guided journey through {Locations.Count} campus stops.";

    public string ProgressText
    {
        get => progressText;
        private set => SetProperty(ref progressText, value);
    }

    public double Progress
    {
        get => progress;
        private set => SetProperty(ref progress, value);
    }

    public bool HasNextStop => nextStop is not null;

    public bool IsTourComplete => HasCatalogue && nextStop is null;

    public string ContinueText => nextStop is null ? string.Empty : $"Continue: {nextStop.Name}";

    public IReadOnlyList<StopItemViewModel> Stops
    {
        get => stops;
        private set => SetProperty(ref stops, value);
    }

    public AsyncCommand ContinueCommand { get; }
    public AsyncCommand Open360TourCommand { get; }

    protected override void OnCatalogueLoaded()
    {
        var completed = VisitorProgress.CountVisited(Locations.Select(stop => stop.Id));
        var total = Locations.Count;

        ProgressText = $"{completed} of {total} completed";
        Progress = total == 0 ? 0 : (double)completed / total;
        nextStop = Locations.FirstOrDefault(stop => !VisitorProgress.IsVisited(stop.Id));
        OnPropertiesChanged(nameof(Subtitle), nameof(HasNextStop), nameof(IsTourComplete), nameof(ContinueText));

        Stops = BuildItems(Locations);
    }
}
