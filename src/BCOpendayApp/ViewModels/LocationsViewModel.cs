using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

public sealed class LocationsViewModel : CatalogViewModelBase
{
    private IReadOnlyList<StopItemViewModel> stops = Array.Empty<StopItemViewModel>();
    private string searchText = string.Empty;

    public LocationsViewModel(
        ILocationService locationService,
        IVisitorProgressService visitorProgress,
        INavigationService navigation,
        IDialogService dialogs)
        : base(locationService, visitorProgress, navigation, dialogs)
    {
    }

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

    public IReadOnlyList<StopItemViewModel> Stops
    {
        get => stops;
        private set => SetProperty(ref stops, value);
    }

    public bool HasNoResults => HasCatalogue && stops.Count == 0;

    protected override void OnCatalogueLoaded() => ApplyFilter();

    private void ApplyFilter()
    {
        var matches = Locations.Where(stop =>
            $"{stop.Name} {stop.Summary} {stop.Highlight}".Contains(searchText, StringComparison.OrdinalIgnoreCase));

        Stops = BuildItems(matches);
        OnPropertyChanged(nameof(HasNoResults));
    }
}
