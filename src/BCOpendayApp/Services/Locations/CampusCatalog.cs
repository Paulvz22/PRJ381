using BCOpendayApp.Models;

namespace BCOpendayApp.Services;

public sealed record CampusMap(string Id, string Title, string Image, int PixelWidth, int PixelHeight);

/// <summary>
/// Stable IDs and bundled map metadata only. All stop CONTENT (names, descriptions, positions) lives in
/// Resources/Raw/locations.json and in Firestore - see <see cref="ILocationService"/>.
/// </summary>
public static class CampusCatalog
{
    private const string CodePrefix = "bc-";

    private static readonly IReadOnlyDictionary<string, string> ExtraImages = new Dictionary<string, string>
    {
        ["robotics"] = "robotarm.png"
    };

    public static IReadOnlyList<string> StopIds { get; } = Array.AsReadOnly(new[]
    {
        "robotics", "manufacturing", "lecture", "library", "lounge"
    });

    public static IReadOnlyList<string> StopImages { get; } = Array.AsReadOnly(new[]
    {
        "roboticslab.png", "manufacturinglab.png", "lecturehall.png", "library.png", "studentlounge.png"
    });

    // Dimensions of the three ORIGINAL supplied images. These keep pins aligned while zooming.
    public static IReadOnlyList<CampusMap> Maps { get; } = Array.AsReadOnly(new[]
    {
        new CampusMap("main", "Main Campus Map", "campus_map_main.png", 1368, 1964),
        new CampusMap("north", "North Campus Map", "campus_map_north.png", 1403, 726),
        new CampusMap("west", "West Campus Map", "campus_map_west.png", 944, 895)
    });

    public static CampusMap Map(string id) => Maps.FirstOrDefault(m => m.Id == id) ?? Maps[0];

    /// <summary>A second photo for stops that have one (shown on the info page and in the gallery).</summary>
    public static string? ExtraImageFor(string stopId) =>
        ExtraImages.TryGetValue(stopId, out var image) ? image : null;

    public static IReadOnlyList<string> GalleryImagesFor(Hotspot stop)
    {
        var extra = ExtraImageFor(stop.Id);
        return extra is null ? new[] { stop.Image } : new[] { stop.Image, extra };
    }

    /// <summary>The code a visitor types in (and, later, the value a QR code will carry), e.g. "BC-ROBOTICS".</summary>
    public static string CodeFor(string stopId) => "BC-" + stopId.ToUpperInvariant();

    public static string ValidCodes => string.Join(", ", StopIds.Select(CodeFor));

    /// <summary>Turns "BC-Robotics " into "Robotics" (the "BC-" prefix is optional; matching is case-insensitive later).</summary>
    public static string StopIdFromCode(string? code)
    {
        var key = (code ?? string.Empty).Trim();
        return key.StartsWith(CodePrefix, StringComparison.OrdinalIgnoreCase) ? key[CodePrefix.Length..] : key;
    }
}
