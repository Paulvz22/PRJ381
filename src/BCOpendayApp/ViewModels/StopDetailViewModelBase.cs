using BCOpendayApp.Models;
using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

/// <summary>
/// Base for pages that show one stop. Shell passes the stop id as a query parameter
/// (see <see cref="Routes.StopIdParameter"/>); once the catalogue has loaded this class finds the stop and
/// tells the page.
/// </summary>
public abstract class StopDetailViewModelBase : CatalogViewModelBase, IQueryAttributable
{
    private Hotspot? stop;
    private string stopId = string.Empty;

    protected StopDetailViewModelBase(
        ILocationService locationService,
        IVisitorProgressService visitorProgress,
        INavigationService navigation,
        IDialogService dialogs)
        : base(locationService, visitorProgress, navigation, dialogs)
    {
    }

    protected Hotspot? Stop => stop;

    public bool HasStop => stop is not null;

    /// <summary>True only after loading finished and the requested id matched no stop.</summary>
    public bool IsStopMissing => HasCatalogue && stop is null;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        stopId = query.TryGetValue(Routes.StopIdParameter, out var value) && value is string text
            ? text.Trim().ToLowerInvariant()
            : string.Empty;

        if (HasCatalogue)
        {
            OnCatalogueLoaded();
        }
    }

    protected override void OnCatalogueLoaded() => LoadStop(Locations.FirstOrDefault(s => s.Id == stopId));

    protected void LoadStop(Hotspot? newStop)
    {
        stop = newStop;
        if (newStop is not null)
        {
            stopId = newStop.Id;
        }

        OnPropertiesChanged(nameof(HasStop), nameof(IsStopMissing));
        OnStopLoaded();
    }

    /// <summary>Raise PropertyChanged for everything that depends on the current stop.</summary>
    protected abstract void OnStopLoaded();

    protected string VisitedText(string notVisitedText) =>
        stop is not null && VisitorProgress.IsVisited(stop.Id) ? "✓ Visited" : notVisitedText;
}
