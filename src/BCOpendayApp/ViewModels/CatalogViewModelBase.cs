using System.Diagnostics;
using BCOpendayApp.Infrastructure;
using BCOpendayApp.Models;
using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

/// <summary>
/// Shared plumbing for every page that shows tour stops. Loads the catalogue (Firestore, falling back to the
/// bundled locations.json) every time the page appears, exposes a status message + Refresh command, and
/// cancels an unfinished load when the page is left. Override <see cref="OnCatalogueLoaded"/> to react.
/// </summary>
public abstract class CatalogViewModelBase : ViewModelBase
{
    private IReadOnlyList<Hotspot> locations = Array.Empty<Hotspot>();
    private string statusMessage = "Loading campus locations...";
    private bool isLoading;
    private bool hasCatalogue;
    private CancellationTokenSource? load;

    protected CatalogViewModelBase(
        ILocationService locationService,
        IVisitorProgressService visitorProgress,
        INavigationService navigation,
        IDialogService dialogs)
        : base(navigation, dialogs)
    {
        LocationService = locationService;
        VisitorProgress = visitorProgress;

        RefreshCommand = CreateCommand(() => LoadAsync(forceRefresh: true));
        ExploreStopCommand = CreateCommand<Hotspot>(OpenStopAsync);
    }

    protected ILocationService LocationService { get; }

    protected IVisitorProgressService VisitorProgress { get; }

    /// <summary>All tour stops, in tour order. Empty until the first load finishes.</summary>
    public IReadOnlyList<Hotspot> Locations
    {
        get => locations;
        private set => SetProperty(ref locations, value);
    }

    /// <summary>Where the data came from ("Locations loaded from Firestore.", "Using bundled offline locations." ...).</summary>
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
                OnPropertiesChanged(nameof(IsNotLoading), nameof(ShowSpinner));
            }
        }
    }

    public bool IsNotLoading => !isLoading;

    /// <summary>True once there is something to show (pages hide their content until then).</summary>
    public bool HasCatalogue
    {
        get => hasCatalogue;
        private set
        {
            if (SetProperty(ref hasCatalogue, value))
            {
                OnPropertyChanged(nameof(ShowSpinner));
            }
        }
    }

    public bool ShowSpinner => isLoading && !hasCatalogue;

    public AsyncCommand RefreshCommand { get; }

    /// <summary>Opens the stop viewer for the Hotspot passed as CommandParameter.</summary>
    public AsyncCommand ExploreStopCommand { get; }

    public override void OnAppearing() => _ = LoadAsync(forceRefresh: false);

    public override void OnDisappearing() => load?.Cancel();

    /// <summary>Called on the UI thread each time a catalogue has been loaded (also when it came from the cache).</summary>
    protected virtual void OnCatalogueLoaded()
    {
    }

    protected Task OpenStopAsync(Hotspot stop) => Navigation.GoToStopAsync(Routes.StopViewer, stop.Id);

    protected IReadOnlyList<StopItemViewModel> BuildItems(IEnumerable<Hotspot> stops) =>
        stops
            .Select(stop => new StopItemViewModel(stop, VisitorProgress.IsVisited(stop.Id), ExploreStopCommand))
            .ToList();

    /// <summary>Never throws: failures become the status message so the page stays usable.</summary>
    protected async Task LoadAsync(bool forceRefresh)
    {
        load?.Cancel();
        var request = new CancellationTokenSource();
        load = request;
        var token = request.Token;

        IsLoading = true;
        try
        {
            var result = await LocationService.GetLocationsAsync(forceRefresh, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            Locations = result.Locations;
            StatusMessage = result.Message;
            HasCatalogue = true;
            OnCatalogueLoaded();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The page was left, or a newer load replaced this one - not an error.
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            if (!token.IsCancellationRequested)
            {
                StatusMessage = "The bundled catalogue could not be loaded. Check Resources/Raw/locations.json and retry.";
            }
        }
        finally
        {
            if (ReferenceEquals(load, request))
            {
                IsLoading = false;
            }
        }
    }
}
