using System.Text.RegularExpressions;
using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;

namespace BCOpendayApp.AR.Data;

/// <summary>Rules every building must pass before it is shown or saved (the Firestore rules repeat the key ones).</summary>
public static partial class BuildingValidation
{
    public const int MaxNameLength = 80;
    public const int MaxSummaryLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxFootprintPoints = 64;
    public const double MinRadiusMeters = 3;
    public const double MaxRadiusMeters = 500;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex IdPattern();

    /// <summary>Returns the first problem, or null when the building is valid.</summary>
    public static string? Problem(BuildingAnchor? b)
    {
        if (b is null) return "Missing building.";
        if (!IdPattern().IsMatch(b.Id)) return "Id must be 1-40 lowercase letters, digits or dashes.";
        if (string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > MaxNameLength) return $"Name must be 1-{MaxNameLength} characters.";
        if (b.Summary.Length > MaxSummaryLength) return $"Summary must be at most {MaxSummaryLength} characters.";
        if (b.Description.Length > MaxDescriptionLength) return $"Description must be at most {MaxDescriptionLength} characters.";
        if (b.StopId.Length > 40) return "Linked stop id is too long.";
        if (!IsValidPoint(b.Center)) return "The centre position is invalid.";
        if (!double.IsFinite(b.RadiusMeters) || b.RadiusMeters < MinRadiusMeters || b.RadiusMeters > MaxRadiusMeters)
            return $"Radius must be {MinRadiusMeters}-{MaxRadiusMeters} m.";

        if (b.Footprint.Count > 0)
        {
            if (b.Footprint.Count < 3 || b.Footprint.Count > MaxFootprintPoints)
                return $"An outline needs 3-{MaxFootprintPoints} corners.";
            if (b.Footprint.Any(p => !IsValidPoint(p) || GeoMath.DistanceMeters(b.Center, p) > 1_000))
                return "An outline corner is invalid or more than 1 km from the centre.";

            var local = b.Footprint.Select(p => GeoMath.ToLocal(b.Center, p)).ToList();
            if (GeoMath.IsSelfIntersecting(local)) return "The outline crosses itself (corners out of order).";
            if (Math.Abs(GeoMath.SignedArea(local)) < 4) return "The outline is too small (corners too close together).";
        }

        return null;
    }

    public static bool IsValid(BuildingAnchor? b) => Problem(b) is null;

    public static bool IsValidPoint(GeoPoint p) =>
        double.IsFinite(p.Lat) && double.IsFinite(p.Lng) && p.Lat is >= -90 and <= 90 && p.Lng is >= -180 and <= 180
        && !(p.Lat == 0 && p.Lng == 0);

    /// <summary>"Main Building (Block A)" → "main-building-block-a".</summary>
    public static string SlugFromName(string name)
    {
        var chars = (name ?? "").Trim().ToLowerInvariant()
            .Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-')
            .ToArray();
        var slug = MultiDash().Replace(new string(chars), "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        return slug.Length == 0 ? "building" : slug;
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDash();

    /// <summary>Firestore stores the outline as "lat,lng;lat,lng;..." (simple, and editable in the console).</summary>
    public static string FormatFootprint(IEnumerable<GeoPoint> points) =>
        string.Join(";", points.Select(p => FormattableString.Invariant($"{p.Lat:F7},{p.Lng:F7}")));

    public static IReadOnlyList<GeoPoint> ParseFootprint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<GeoPoint>();
        var result = new List<GeoPoint>();
        foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lng))
            {
                return Array.Empty<GeoPoint>(); // a damaged outline is rejected as a whole by Problem()
            }

            result.Add(new GeoPoint(lat, lng));
        }

        return result;
    }
}
