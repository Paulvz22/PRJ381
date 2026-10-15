using Microsoft.Maui.Storage;

namespace BCOpendayApp.Services;

/// <summary>Stores visitor progress in the device's preferences (survives closing the app).</summary>
public sealed class VisitorProgressService : IVisitorProgressService
{
    // Same keys the original version used, so progress saved on a tester's phone is not lost.
    private const string VisitedPrefix = "visited_";
    private const string FavouritePrefix = "saved_";

    public bool IsVisited(string stopId) => Preferences.Default.Get(VisitedPrefix + stopId, false);

    public void MarkVisited(string stopId) => Preferences.Default.Set(VisitedPrefix + stopId, true);

    public bool IsFavourite(string stopId) => Preferences.Default.Get(FavouritePrefix + stopId, false);

    public void SetFavourite(string stopId, bool value) => Preferences.Default.Set(FavouritePrefix + stopId, value);

    public int CountVisited(IEnumerable<string> stopIds) => stopIds.Count(IsVisited);
}
