using System.Text.Json.Serialization;

namespace BCOpendayApp.Panorama.Models;

/// <summary>
/// A panorama saved on this phone: either a 2:1 full-sphere image imported from the gallery, or a 360° strip
/// captured with the camera inside the app (staff only). Staff put a view online with Upload (Firestore, see
/// PanoramaChunks); the online copy uses the same id. Online views are turned back into this record with
/// PanoramaManifest.ToDraft so the tour treats both alike (docs/panoramas.md).
/// </summary>
public sealed record PanoramaDraft
{
    public const string EquirectangularProjection = "equirectangular";
    public const string GalleryImportMethod = "gallery-import";

    /// <summary>Taken with the phone camera on the staff capture screen and stitched on the phone.</summary>
    public const string InAppCaptureMethod = "in-app-capture";

    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>File name inside the app-private panorama drafts folder. Never a full path.</summary>
    public string FileName { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    public string Projection { get; set; } = string.Empty;

    public bool IsFullSphere { get; set; }

    public string CaptureMethod { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public long CreatedAtUnixMs { get; set; }

    /// <summary>
    /// Vertical extent of a captured 360° strip in degrees, centred on the horizon. 0 for full spheres (180°).
    /// </summary>
    public double VerticalFovDegrees { get; set; }

    /// <summary>Number of photos stitched into a captured panorama (0 for imports).</summary>
    public int PhotoCount { get; set; }

    /// <summary>Where the panorama was taken. Used to place the arrows between panoramas automatically.</summary>
    public bool HasLocation { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double LocationAccuracyMeters { get; set; }

    /// <summary>
    /// True when the image direction is known: <see cref="NorthOffsetDegrees"/> is then the true-north compass
    /// heading at the panorama's yaw 0. Captured panoramas are laid out by compass heading, so it is 0 for them.
    /// </summary>
    public bool HasNorthOffset { get; set; }

    /// <summary>
    /// The campus location this view belongs to: an AR building id, or a location created together with the
    /// view. Empty for views made before locations existed (the view is then its own location).
    /// </summary>
    public string LocationId { get; set; } = string.Empty;

    /// <summary>Short description; used as the location's description when the view created the location.</summary>
    public string Description { get; set; } = string.Empty;

    public double NorthOffsetDegrees { get; set; }

    [JsonIgnore]
    public bool IsCaptured => CaptureMethod == InAppCaptureMethod;

    /// <summary>Vertical field of view to give the viewer.</summary>
    [JsonIgnore]
    public double ViewerVerticalFov => IsFullSphere ? 180 : VerticalFovDegrees;
}
