namespace BCOpendayApp.Services;

public sealed record CampusMap(string Id, string Title, string Image, int PixelWidth, int PixelHeight);

/// <summary>Stable IDs and bundled map metadata only. All stop content lives in locations.json / Firestore.</summary>
public static class CampusCatalog
{
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
}
