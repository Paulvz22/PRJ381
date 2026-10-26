using System.Diagnostics;
using System.Windows.Input;
using BCOpendayApp.Infrastructure;
using BCOpendayApp.Services;
using BCOpendayApp.Services.Campus;

namespace BCOpendayApp.ViewModels;

/// <summary>One campus location as a row: name, AR/360° badges, code and visited tick.</summary>
public sealed class LocationItemViewModel
{
    public LocationItemViewModel(CampusLocation location, bool isVisited, ICommand openCommand)
    {
        Location = location;
        IsVisited = isVisited;
        OpenCommand = openCommand;
    }

    public CampusLocation Location { get; }

    public string Name => Location.Name;

    public string Description => Location.Description;

    public bool HasDescription => Location.Description.Length > 0;

    public bool IsVisited { get; }

    /// <summary>e.g. "AR · 360°  ·  BC-LIBRARY  ·  ✓ Visited".</summary>
    public string Details => string.Join("  ·  ", new[] { Location.Badges, Location.Code, IsVisited ? "✓ Visited" : null }.OfType<string>());

    public ICommand OpenCommand { get; }
}

/// <summary>
/// Shared loading for pages that show the campus locations (Home progress, Locations, Map). The list is built
/// automatically from AR building scans and online 360° views each time the page appears.
/// </summary>
public abstract class CampusLocationsViewModelBase : ViewModelBase
{
    private IReadOnlyList<CampusLocation> locations = Array.Empty<CampusLocation>();
    private string statusMessage = "Loading campus locations...";
    private bool isLoading;
    private bool hasLoaded;

    protected CampusLocationsViewModelBase(
        ICampusLocationService campus,
        IVisitorProgressService visitorProgress,
        INavigationService navigation,
        IDialogService dialogs)
        : base(navigation, dialogs)
    {
        Campus = campus;
        VisitorProgress = visitorProgress;
        RefreshCommand = CreateCommand(() => LoadAsync(forceRefresh: true));
        OpenLocationCommand = CreateCommand<CampusLocation>(OpenLocationAsync);
    }

    protected ICampusLocationService Campus { get; }

    protected IVisitorProgressService VisitorProgress { get; }

    public IReadOnlyList<CampusLocation> Locations
    {
        get => locations;
        private set => SetProperty(ref locations, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public bool IsLoading
    {
        get => isLoading;
        private set
        {
            if (SetProperty(ref isLoading, value))
            {
                OnPropertiesChanged(nameof(ShowSpinner), nameof(HasNoLocations));
            }
        }
    }

    public bool ShowSpinner => isLoading && !hasLoaded;

    public bool HasLocations => locations.Count > 0;

    public bool HasNoLocations => hasLoaded && !isLoading && locations.Count == 0;

    public AsyncCommand RefreshCommand { get; }

    /// <summary>CommandParameter: a <see cref="CampusLocation"/>.</summary>
    public AsyncCommand OpenLocationCommand { get; }

    public override void OnAppearing() => _ = LoadAsync(forceRefresh: false);

    protected virtual Task OpenLocationAsync(CampusLocation location) =>
        Navigation.GoToStopAsync(Routes.LocationDetail, location.Id);

    protected IReadOnlyList<LocationItemViewModel> BuildItems(IEnumerable<CampusLocation> items) =>
        items.Select(location => new LocationItemViewModel(location, VisitorProgress.IsVisited(location.Id), OpenLocationCommand)).ToList();

    /// <summary>Called on the UI thread after every load.</summary>
    protected virtual void OnLocationsLoaded()
    {
    }

    private async Task LoadAsync(bool forceRefresh)
    {
        if (isLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var result = await Campus.GetAllAsync(forceRefresh);
            Locations = result.Locations;
            StatusMessage = result.Locations.Count == 0
                ? "No locations yet. A location appears here as soon as staff scan a building for AR or capture a 360° view."
                : result.Message;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusMessage = "The campus locations could not be loaded. Check your connection and tap Refresh.";
        }
        finally
        {
            hasLoaded = true;
            IsLoading = false;
            OnPropertiesChanged(nameof(HasLocations), nameof(HasNoLocations), nameof(ShowSpinner));
            OnLocationsLoaded();
        }
    }
}
