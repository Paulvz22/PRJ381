using BCOpendayApp.Services;
using BCOpendayApp.Services.Campus;

namespace BCOpendayApp.ViewModels;

/// <summary>
/// All campus locations with search. Opened with mode=pano (from "Choose from locations" on the Tour page) it
/// lists only locations with a 360° view, and tapping one opens that view straight away.
/// </summary>
public sealed class LocationsViewModel : CampusLocationsViewModelBase, IQueryAttributable
{
    public const string ModeParameter = "mode";
    public const string PanoramaMode = "pano";

    private IReadOnlyList<LocationItemViewModel> items = Array.Empty<LocationItemViewModel>();
    private string searchText = string.Empty;
    private bool panoramaMode;

    public LocationsViewModel(
        ICampusLocationService campus,
        IVisitorProgressService visitorProgress,
        INavigationService navigation,
        IDialogService dialogs)
        : base(campus, visitorProgress, navigation, dialogs)
    {
    }

    public string Subtitle => panoramaMode
        ? "Choose a place to open its 360° view."
        : "Every place on campus that has an AR scan or a 360° view.";

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public IReadOnlyList<LocationItemViewModel> Items
    {
        get => items;
        private set => SetProperty(ref items, value);
    }

    public bool HasNoResults => HasLocations && items.Count == 0;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        panoramaMode = query.TryGetValue(ModeParameter, out var mode) && mode as string == PanoramaMode;
        OnPropertyChanged(nameof(Subtitle));
        ApplyFilter();
    }

    protected override void OnLocationsLoaded() => ApplyFilter();

    protected override Task OpenLocationAsync(CampusLocation location) =>
        panoramaMode && location.FirstViewId is { } viewId
            ? Navigation.GoToStopAsync(Routes.Pano, viewId)
            : base.OpenLocationAsync(location);

    private void ApplyFilter()
    {
        var matches = Locations
            .Where(location => !panoramaMode || location.HasPanorama)
            .Where(location => $"{location.Name} {location.Description} {location.Code}"
                .Contains(searchText, StringComparison.OrdinalIgnoreCase));

        Items = BuildItems(matches);
        OnPropertyChanged(nameof(HasNoResults));
    }
}
