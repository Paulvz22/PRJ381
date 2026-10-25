namespace BCOpendayApp.Panorama.Models;

/// <summary>
/// A panorama that has been imported and accepted on this phone. This is deliberately local-only: it has no
/// remote id, storage path or publishing state.
/// </summary>
public sealed record PanoramaDraft
{
    public const string EquirectangularProjection = "equirectangular";
    public const string GalleryImportMethod = "gallery-import";

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
}
