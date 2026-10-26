using System.Security.Cryptography;
using BCOpendayApp.Panorama.Models;

namespace BCOpendayApp.Panorama.Services;

/// <summary>
/// What Firestore stores about one online 360° view (document panoramas/{id}). The image itself is stored as
/// base64 text split over <see cref="ChunkCount"/> documents panoramas/{id}/chunks/{0..n-1}, because one Firestore
/// document holds at most 1 MB and Cloud Storage would need the paid Blaze plan.
/// </summary>
public sealed record PanoramaManifest
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Vertical field of view; 180 for full spheres.</summary>
    public double VerticalFovDegrees { get; init; }

    public bool IsFullSphere { get; init; }

    public string CaptureMethod { get; init; } = string.Empty;

    public int PhotoCount { get; init; }

    public bool HasLocation { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public double LocationAccuracyMeters { get; init; }

    public bool HasNorthOffset { get; init; }

    /// <summary>Campus location this view belongs to (see <see cref="PanoramaDraft.LocationId"/>).</summary>
    public string LocationId { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public double NorthOffsetDegrees { get; init; }

    public string MimeType { get; init; } = "image/jpeg";

    public int ChunkCount { get; init; }

    public long ByteLength { get; init; }

    /// <summary>SHA-256 of the image (hex). Phones keep a cached copy until this changes.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Random id of one upload: every chunk carries it, so chunks from an older upload are never mixed in.</summary>
    public string UploadId { get; init; } = string.Empty;

    public long UpdatedAtUnixMs { get; init; }

    public string UpdatedBy { get; init; } = string.Empty;

    /// <summary>The same view as panorama metadata, so the tour and viewer can treat it like a local one.</summary>
    public PanoramaDraft ToDraft() => new()
    {
        Id = Id,
        DisplayName = Name,
        FileName = Id + (MimeType == "image/png" ? ".png" : ".jpg"),
        Width = Width,
        Height = Height,
        Projection = PanoramaDraft.EquirectangularProjection,
        IsFullSphere = IsFullSphere,
        CaptureMethod = CaptureMethod,
        FileSizeBytes = ByteLength,
        CreatedAtUnixMs = UpdatedAtUnixMs,
        VerticalFovDegrees = IsFullSphere ? 0 : VerticalFovDegrees,
        PhotoCount = PhotoCount,
        HasLocation = HasLocation,
        Latitude = Latitude,
        Longitude = Longitude,
        LocationAccuracyMeters = LocationAccuracyMeters,
        HasNorthOffset = HasNorthOffset,
        NorthOffsetDegrees = NorthOffsetDegrees,
        LocationId = LocationId,
        Description = Description,
    };

    public static PanoramaManifest FromDraft(PanoramaDraft draft, byte[] image, string uploadId, string uid, long nowUnixMs) => new()
    {
        Id = draft.Id,
        Name = draft.DisplayName,
        Width = draft.Width,
        Height = draft.Height,
        VerticalFovDegrees = draft.ViewerVerticalFov,
        IsFullSphere = draft.IsFullSphere,
        CaptureMethod = draft.CaptureMethod,
        PhotoCount = draft.PhotoCount,
        HasLocation = draft.HasLocation,
        Latitude = draft.Latitude,
        Longitude = draft.Longitude,
        LocationAccuracyMeters = draft.LocationAccuracyMeters,
        HasNorthOffset = draft.HasNorthOffset,
        NorthOffsetDegrees = draft.NorthOffsetDegrees,
        LocationId = draft.LocationId,
        Description = draft.Description,
        MimeType = draft.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg",
        ChunkCount = PanoramaChunks.ChunkCountFor(image.Length),
        ByteLength = image.Length,
        Sha256 = PanoramaChunks.Sha256Hex(image),
        UploadId = uploadId,
        UpdatedAtUnixMs = nowUnixMs,
        UpdatedBy = uid,
    };
}

/// <summary>Splitting an image into Firestore-sized pieces and putting it back together. Pure - unit-tested.</summary>
public static class PanoramaChunks
{
    /// <summary>
    /// Base64 characters per chunk document. Firestore allows ~1 MB per document; 700,000 characters
    /// (= 525,000 image bytes) leaves room for the other fields. Must match firebase/firestore.rules.
    /// </summary>
    public const int ChunkChars = 700_000;

    /// <summary>Image bytes per chunk (base64 turns 3 bytes into 4 characters).</summary>
    public const int ChunkBytes = ChunkChars / 4 * 3;

    /// <summary>At most 16 chunks = 8.4 MB per view. Must match firebase/firestore.rules.</summary>
    public const int MaxChunks = 16;

    public const long MaxImageBytes = (long)ChunkBytes * MaxChunks;

    public static int ChunkCountFor(long imageBytes) => (int)Math.Max(1, (imageBytes + ChunkBytes - 1) / ChunkBytes);

    /// <summary>
    /// The image as base64 pieces. Each piece encodes a whole number of 3-byte groups (except the last), so the
    /// pieces can be decoded separately and simply concatenated.
    /// </summary>
    public static IReadOnlyList<string> Split(byte[] image)
    {
        if (image.Length == 0)
        {
            throw new ArgumentException("The image is empty.", nameof(image));
        }

        if (image.Length > MaxImageBytes)
        {
            throw new ArgumentException($"The image is larger than {MaxImageBytes / (1024 * 1024)} MB.", nameof(image));
        }

        var chunks = new List<string>();
        for (var offset = 0; offset < image.Length; offset += ChunkBytes)
        {
            chunks.Add(Convert.ToBase64String(image, offset, Math.Min(ChunkBytes, image.Length - offset)));
        }

        return chunks;
    }

    /// <summary>Puts the pieces back together and checks length and SHA-256. Throws if anything does not match.</summary>
    public static byte[] Join(IReadOnlyList<string> chunks, PanoramaManifest manifest)
    {
        if (chunks.Count != manifest.ChunkCount)
        {
            throw new InvalidDataException($"Expected {manifest.ChunkCount} pieces, got {chunks.Count}.");
        }

        using var buffer = new MemoryStream((int)Math.Min(manifest.ByteLength, MaxImageBytes));
        foreach (var chunk in chunks)
        {
            var bytes = Convert.FromBase64String(chunk);
            buffer.Write(bytes, 0, bytes.Length);
        }

        var image = buffer.ToArray();
        if (image.Length != manifest.ByteLength || !string.Equals(Sha256Hex(image), manifest.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The downloaded panorama is incomplete or damaged.");
        }

        return image;
    }

    /// <summary>Empty, a building-style id ("library-entrance") or a panorama id (32 hex).</summary>
    public static bool IsValidLocationId(string? id) =>
        string.IsNullOrEmpty(id)
        || System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,39}$");

    public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>Same checks as firebase/firestore.rules, so a bad document from the server is ignored.</summary>
    public static bool IsValid(PanoramaManifest? m) =>
        m is not null
        && PanoramaLocalFiles.IsSafeDraftId(m.Id)
        && !string.IsNullOrWhiteSpace(m.Name) && m.Name.Length <= 100
        && m.Width is >= 256 and <= 8192
        && m.Height is >= 32 and <= 4096
        && double.IsFinite(m.VerticalFovDegrees) && m.VerticalFovDegrees is >= 10 and <= 180
        && m.MimeType is "image/jpeg" or "image/png"
        && m.ChunkCount is >= 1 and <= MaxChunks
        && m.ByteLength > 0 && m.ByteLength <= MaxImageBytes
        && ChunkCountFor(m.ByteLength) == m.ChunkCount
        && m.Sha256.Length == 64
        && m.UploadId.Length is >= 8 and <= 40
        && IsValidLocationId(m.LocationId)
        && m.Description.Length <= 300
        && (!m.HasLocation || (double.IsFinite(m.Latitude) && double.IsFinite(m.Longitude)
            && Math.Abs(m.Latitude) <= 90 && Math.Abs(m.Longitude) <= 180 && !(m.Latitude == 0 && m.Longitude == 0)))
        && (!m.HasNorthOffset || (double.IsFinite(m.NorthOffsetDegrees) && m.NorthOffsetDegrees >= 0 && m.NorthOffsetDegrees < 360));
}
