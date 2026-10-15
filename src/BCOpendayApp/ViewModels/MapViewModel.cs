using BCOpendayApp.Infrastructure;
using BCOpendayApp.Models;
using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

/// <summary>
/// The campus map. Pins are the stops that have a map position (normalised 0-1 image coordinates, NOT GPS);
/// the page draws them with <see cref="MapGeometry"/>. Opened with a stop id, it switches to that stop's map.
/// </summary>
public sealed class MapViewModel : CatalogViewModelBase, IQueryAttributable
{
    private IReadOnlyList<StopItemViewModel> stops = Array.Empty<StopItemViewModel>();
    private IReadOnlyList<Hotspot> pinnedStops = Array.Empty<Hotspot>();
    private CampusMap selectedMap = CampusCatalog.Maps[0];
    private string requestedStopId = string.Empty;
    private string mapStatus = string.Empty;
    private double zoom = 1;

    public MapViewModel(
        ILocationService locationService,
        IVisitorProgressService visitorProgress,
        INavigationService navigation,
        IDialogService dialogs)
        : base(locationService, visitorProgress, navigation, dialogs)
    {
        SelectSectionCommand = CreateCommand<string>(sectionId =>
        {
            SelectMap(CampusCatalog.Map(sectionId));
            RefreshPins();
            return Task.CompletedTask;
        });

        ResetZoomCommand = CreateCommand(() =>
        {
            Zoom = 1;
            return Task.CompletedTask;
        });

        // Tapping a pin opens the information page for that stop.
        OpenInfoCommand = CreateCommand<Hotspot>(stop => Navigation.GoToStopAsync(Routes.StopInfo, stop.Id));
    }

    public CampusMap SelectedMap
    {
        get => selectedMap;
        private set => SetProperty(ref selectedMap, value);
    }

    /// <summary>"main", "north" or "west"; the section buttons highlight from this.</summary>
    public string SelectedSectionId => selectedMap.Id;

    public string MapTitle => selectedMap.Title;

    /// <summary>1 = fit the screen, 3 = three times larger (the page resizes the image).</summary>
    public double Zoom
    {
        get => zoom;
        set => SetProperty(ref zoom, value);
    }

    /// <summary>The stop the page was opened for (e.g. from "Floor Plan"); its pin is highlighted.</summary>
    public string RequestedStopId
    {
        get => requestedStopId;
        private set => SetProperty(ref requestedStopId, value);
    }

    /// <summary>Stops that have a pin on the map currently shown.</summary>
    public IReadOnlyList<Hotspot> PinnedStops
    {
        get => pinnedStops;
        private set => SetProperty(ref pinnedStops, value);
    }

    public string MapStatus
    {
        get => mapStatus;
        private set => SetProperty(ref mapStatus, value);
    }

    /// <summary>Why the stop the visitor asked for has no pin (empty when it has one).</summary>
    public string RequestedStopMessage
    {
        get
        {
            var requested = Locations.FirstOrDefault(stop => stop.Id == requestedStopId);
            return requested is { HasMapPosition: false } ? $"{requested.Name}: {requested.PositionNote}" : string.Empty;
        }
    }

    public bool HasRequestedStopMessage => RequestedStopMessage.Length > 0;

    public string UnplacedMessage
    {
        get
        {
            var unplaced = Locations.Count(stop => !stop.HasMapPosition);
            return unplaced == 0
                ? string.Empty
                : unplaced == 1
                    ? "1 tour stop has no map position. It remains available below; ask campus staff for directions."
                    : $"{unplaced} tour stops have no map position. They remain available below; ask campus staff for directions.";
        }
    }

    public bool HasUnplacedMessage => UnplacedMessage.Length > 0;

    public IReadOnlyList<StopItemViewModel> Stops
    {
        get => stops;
        private set => SetProperty(ref stops, value);
    }

    public AsyncCommand SelectSectionCommand { get; }

    public AsyncCommand ResetZoomCommand { get; }

    public AsyncCommand OpenInfoCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        RequestedStopId = query.TryGetValue(Routes.StopIdParameter, out var value) && value is string text
            ? text.Trim().ToLowerInvariant()
            : string.Empty;

        if (HasCatalogue)
        {
            OnCatalogueLoaded();
        }
    }

    public override void OnDisappearing()
    {
        // The focus request only applies to the visit it was made for.
        RequestedStopId = string.Empty;
        base.OnDisappearing();
    }

    protected override void OnCatalogueLoaded()
    {
        var requested = Locations.FirstOrDefault(stop => stop.Id == requestedStopId);
        if (requested is { HasMapPosition: true } && requested.MapSection != selectedMap.Id)
        {
            SelectMap(CampusCatalog.Map(requested.MapSection));
        }

        Stops = BuildItems(Locations);
        RefreshPins();
        OnPropertiesChanged(
            nameof(RequestedStopMessage),
            nameof(HasRequestedStopMessage),
            nameof(UnplacedMessage),
            nameof(HasUnplacedMessage));
    }

    private void SelectMap(CampusMap map)
    {
        SelectedMap = map;
        Zoom = 1;
        OnPropertiesChanged(nameof(SelectedSectionId), nameof(MapTitle));
    }

    private void RefreshPins()
    {
        PinnedStops = Locations
            .Where(stop => stop.HasMapPosition && stop.MapSection == selectedMap.Id)
            .ToList();

        MapStatus = pinnedStops.Count == 0
            ? "No tour-stop positions are assigned to this map yet."
            : string.Join(" | ", pinnedStops.Select(stop => $"{stop.SortOrder}: {stop.Name}")) + " - reference positions only.";
    }
}
