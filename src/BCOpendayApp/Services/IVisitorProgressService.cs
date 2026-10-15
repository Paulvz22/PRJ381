namespace BCOpendayApp.Services;

/// <summary>Per-device visitor state: which stops were visited and which are favourites.</summary>
public interface IVisitorProgressService
{
    bool IsVisited(string stopId);
    void MarkVisited(string stopId);

    bool IsFavourite(string stopId);
    void SetFavourite(string stopId, bool value);

    /// <summary>How many of the given stops have been visited.</summary>
    int CountVisited(IEnumerable<string> stopIds);
}
