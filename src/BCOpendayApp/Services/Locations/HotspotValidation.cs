using BCOpendayApp.Models;

namespace BCOpendayApp.Services;

public static class HotspotValidation
{
    public static bool IsValid(Hotspot? value)
    {
        if (value is null || !CampusCatalog.StopIds.Contains(value.Id)
            || !CampusCatalog.StopImages.Contains(value.Image)
            || !Text(value.Name, 120) || !Text(value.Summary, 500)
            || !Text(value.Description, 5000) || !Text(value.Highlight, 500)
            || !Text(value.PositionNote, 1000) || value.SortOrder is < 1 or > 100)
            return false;

        // Unknown coordinates must be explicitly absent, never silently default to (0,0).
        return value.HasMapPosition
            || (value.MapSection == "" && value.X is null && value.Y is null);
    }

    private static bool Text(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    public static IReadOnlyList<Hotspot> ValidateBundle(IEnumerable<Hotspot> locations)
    {
        var all = locations.ToList();
        if (all.Count != CampusCatalog.StopIds.Count || all.Any(x => !IsValid(x))
            || all.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != all.Count)
            throw new InvalidDataException("Bundled locations.json must contain all five valid, unique tour stops.");
        return Sort(all);
    }

    public static (IReadOnlyList<Hotspot> Locations, int RemoteCount) Merge(
        IReadOnlyList<Hotspot> bundled, IEnumerable<Hotspot> remote)
    {
        // A malformed/duplicate document must not remove a required offline stop.
        var good = remote.Where(IsValid).GroupBy(x => x.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single());
        return (Sort(bundled.Select(local => good.GetValueOrDefault(local.Id, local))), good.Count);
    }

    private static IReadOnlyList<Hotspot> Sort(IEnumerable<Hotspot> values) =>
        Array.AsReadOnly(values.OrderBy(x => x.SortOrder).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray());
}
