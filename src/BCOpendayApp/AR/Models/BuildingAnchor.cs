namespace BCOpendayApp.AR.Models;

/// <summary>A latitude/longitude pair in degrees (WGS-84, the same as GPS).</summary>
public readonly record struct GeoPoint(double Lat, double Lng);

/// <summary>
/// A scanned campus building: its outline on the ground plus the text the AR view shows when the camera
/// points at it. Created by the staff scanner, stored in Firestore (collection "buildings"), cached on the
/// phone and optionally bundled in Resources/Raw/buildings.json.
/// </summary>
public sealed record BuildingAnchor
{
    /// <summary>Stable id, lowercase letters/digits/dashes (e.g. "main-building"). Also the Firestore document id.</summary>
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>One line shown on the AR label card.</summary>
    public string Summary { get; init; } = "";

    /// <summary>Longer text shown when the visitor taps "More info".</summary>
    public string Description { get; init; } = "";

    /// <summary>Optional tour stop id (e.g. "library") so the AR card can open that stop's page.</summary>
    public string StopId { get; init; } = "";

    public double CenterLat { get; init; }

    public double CenterLng { get; init; }

    /// <summary>Used when there is no footprint (the building is treated as a circle). Always set.</summary>
    public double RadiusMeters { get; init; }

    /// <summary>The building's corners in walking order (3+ points), or empty for "centre + radius" anchors.</summary>
    public IReadOnlyList<GeoPoint> Footprint { get; init; } = Array.Empty<GeoPoint>();

    /// <summary>Hidden from visitors when false (lets staff keep a draft in the cloud).</summary>
    public bool IsActive { get; init; } = true;

    public long UpdatedAtUnixMs { get; init; }

    /// <summary>Firebase uid of the staff account that last saved it.</summary>
    public string UpdatedBy { get; init; } = "";

    public GeoPoint Center => new(CenterLat, CenterLng);

    public bool HasFootprint => Footprint.Count >= 3;
}
