using BCOpendayApp.Infrastructure;
using BCOpendayApp.Services;
using BCOpendayApp.Services.Campus;

namespace BCOpendayApp.ViewModels;

/// <summary>
/// The campus map image (Main / North / West, with zoom) and the campus locations below it.
/// The numbered pins were removed because the map image already shows the layout; to bring them back,
/// apply docs/backups/restore-map-pins.patch.
/// </summary>
public sealed class MapViewModel : CampusLocationsViewModelBase
{
    private IReadOnlyList<LocationItemViewModel> items = Array.Empty<LocationItemViewModel>();
    private CampusMap selectedMap = CampusCatalog.Maps[0];
    private double zoom = 1;

    public MapViewModel(
        ICampusLocationService campus,
        IVisitorProgressService visitorProgress,
        INavigationService navigation,
        IDialogService dialogs)
        : base(campus, visitorProgress, navigation, dialogs)
    {
        SelectSectionCommand = CreateCommand<string>(sectionId =>
        {
            SelectMap(CampusCatalog.Map(sectionId));
            return Task.CompletedTask;
        });

        ResetZoomCommand = CreateCommand(() =>
        {
            Zoom = 1;
            return Task.CompletedTask;
        });
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

    public IReadOnlyList<LocationItemViewModel> Items
    {
        get => items;
        private set => SetProperty(ref items, value);
    }

    public AsyncCommand SelectSectionCommand { get; }

    public AsyncCommand ResetZoomCommand { get; }

    protected override void OnLocationsLoaded() => Items = BuildItems(Locations);

    private void SelectMap(CampusMap map)
    {
        SelectedMap = map;
        Zoom = 1;
        OnPropertiesChanged(nameof(SelectedSectionId), nameof(MapTitle));
    }
}
