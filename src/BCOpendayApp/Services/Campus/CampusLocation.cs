using System.Text;
using BCOpendayApp.AR.Models;
using BCOpendayApp.Panorama.Models;
using BCOpendayApp.Panorama.Services;

namespace BCOpendayApp.Services.Campus;

/// <summary>
/// One place on campus a visitor can visit. A location exists as soon as there is an AR building scan for it,
/// a 360° view of it, or both - nothing is typed in by hand.
/// </summary>
public sealed record CampusLocation(
    string Id,
    string Name,
    string Description,
    BuildingAnchor? Building,
    IReadOnlyList<PanoramaManifest> Views)
{
    /// <summary>The code visitors can type on the Tour page, e.g. BC-LIBRARY.</summary>
    public string Code => CampusLocationCatalog.CodePrefix + Id.ToUpperInvariant();

    public bool HasAr => Building is not null;

    public bool HasPanorama => Views.Count > 0;

    /// <summary>The 360° view that opens first (the oldest one of this location).</summary>
    public string? FirstViewId => Views.Count > 0 ? Views[0].Id : null;

    /// <summary>"AR · 360°", "AR" or "360°".</summary>
    public string Badges => string.Join(" · ", new[] { HasAr ? "AR" : null, HasPanorama ? "360°" : null }.OfType<string>());
}

/// <summary>Builds the location list from building scans and online 360° views. Pure - unit-tested.</summary>
public static class CampusLocationCatalog
{
    public const string CodePrefix = "BC-";

    public const int MaxIdLength = 40;

    /// <summary>The location a 360° view belongs to: its LocationId, or (older views) the view itself.</summary>
    public static string LocationIdFor(string viewId, string? locationId) =>
        string.IsNullOrWhiteSpace(locationId) ? viewId : locationId;

    public static string LocationIdFor(PanoramaManifest view) => LocationIdFor(view.Id, view.LocationId);

    public static string LocationIdFor(PanoramaDraft view) => LocationIdFor(view.Id, view.LocationId);

    public static IReadOnlyList<CampusLocation> Build(
        IEnumerable<BuildingAnchor> buildings,
        IEnumerable<PanoramaManifest> views)
    {
        var viewsByLocation = views
            .Where(PanoramaChunks.IsValid)
            .GroupBy(view => LocationIdFor(view), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PanoramaManifest>)group.OrderBy(view => view.UpdatedAtUnixMs).ThenBy(view => view.Id, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        var locations = new Dictionary<string, CampusLocation>(StringComparer.Ordinal);
        foreach (var building in buildings.Where(building => building.IsActive && !string.IsNullOrWhiteSpace(building.Id)))
        {
            if (locations.ContainsKey(building.Id))
            {
                continue;
            }

            var attached = viewsByLocation.GetValueOrDefault(building.Id) ?? Array.Empty<PanoramaManifest>();
            var description = FirstText(building.Description, building.Summary, attached.Select(view => view.Description));
            locations[building.Id] = new CampusLocation(building.Id, building.Name, description, building, attached);
        }

        foreach (var (id, attached) in viewsByLocation)
        {
            if (!locations.ContainsKey(id))
            {
                locations[id] = new CampusLocation(
                    id, attached[0].Name, FirstText(null, null, attached.Select(view => view.Description)), null, attached);
            }
        }

        return locations.Values
            .OrderBy(location => location.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(location => location.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Finds a location by its code. "BC-LIBRARY", "bc-library" and "library" all work.</summary>
    public static CampusLocation? FindByCode(IEnumerable<CampusLocation> locations, string? code)
    {
        var text = (code ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (text.StartsWith(CodePrefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[CodePrefix.Length..];
        }

        return locations.FirstOrDefault(location =>
            string.Equals(location.Id, text, StringComparison.OrdinalIgnoreCase)
            || (location.Building is { StopId.Length: > 0 } building
                && string.Equals(building.StopId, text, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>A new location id from a name: "Main Library!" → "main-library", unique among <paramref name="taken"/>.</summary>
    public static string NewLocationId(string name, IEnumerable<string> taken)
    {
        var builder = new StringBuilder();
        foreach (var character in (name ?? string.Empty).ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > MaxIdLength - 3)
        {
            slug = slug[..(MaxIdLength - 3)].TrimEnd('-');
        }

        if (slug.Length == 0)
        {
            slug = "location";
        }

        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var candidate = slug;
        for (var number = 2; used.Contains(candidate); number++)
        {
            candidate = $"{slug}-{number}";
        }

        return candidate;
    }

    private static string FirstText(string? first, string? second, IEnumerable<string> more) =>
        new[] { first, second }.Concat(more).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))?.Trim() ?? string.Empty;
}
