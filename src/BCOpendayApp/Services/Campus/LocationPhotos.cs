using BCOpendayApp.Panorama.Services;

namespace BCOpendayApp.Services.Campus;

/// <summary>
/// The one photo of a location (e.g. the front of the building), stored as base64 in a single Firestore
/// document locationPhotos/{locationId}. It is shrunk before upload so it always fits in one document.
/// </summary>
public sealed record LocationPhoto(string Data, string Sha256, string MimeType, long UpdatedAtUnixMs, string UpdatedBy);

public static class LocationPhotoRules
{
    /// <summary>Largest JPEG accepted (base64 = 800,000 characters, well under Firestore's 1 MB per document).</summary>
    public const int MaxBytes = 600_000;

    /// <summary>Longest side after shrinking.</summary>
    public const int MaxSidePixels = 1280;

    /// <summary>Must match firebase/firestore.rules.</summary>
    public const int MaxBase64Chars = (MaxBytes + 2) / 3 * 4;

    public static LocationPhoto Create(byte[] jpeg, string uid, long nowUnixMs)
    {
        if (jpeg.Length == 0 || jpeg.Length > MaxBytes)
        {
            throw new ArgumentException($"The photo must be between 1 byte and {MaxBytes / 1000} KB after shrinking.", nameof(jpeg));
        }

        return new LocationPhoto(Convert.ToBase64String(jpeg), PanoramaChunks.Sha256Hex(jpeg), "image/jpeg", nowUnixMs, uid);
    }

    /// <summary>The image bytes, or null when the document is damaged.</summary>
    public static byte[]? Decode(LocationPhoto? photo)
    {
        if (photo is null || photo.Data.Length == 0 || photo.Data.Length > MaxBase64Chars || photo.MimeType != "image/jpeg")
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(photo.Data);
            return PanoramaChunks.Sha256Hex(bytes) == photo.Sha256 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

public interface ILocationPhotoRemoteStore
{
    bool IsConfigured { get; }

    /// <summary>Null when the location has no photo.</summary>
    Task<LocationPhoto?> GetAsync(string locationId, CancellationToken cancellationToken);

    Task SetAsync(string locationId, LocationPhoto photo);
}
