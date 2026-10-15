#if FIREBASE_CONFIGURED
using Plugin.Firebase.Firestore;

namespace BCOpendayApp.Models;

// Separate native DTO keeps the domain model and offline tests independent of Firebase.
public sealed class FirestoreHotspotDocument : IFirestoreObject
{
    [FirestoreDocumentId]
    public string Id { get; set; } = "";

    [FirestoreProperty("name")]
    public string Name { get; set; } = "";

    [FirestoreProperty("image")]
    public string Image { get; set; } = "";

    [FirestoreProperty("summary")]
    public string Summary { get; set; } = "";

    [FirestoreProperty("description")]
    public string Description { get; set; } = "";

    [FirestoreProperty("highlight")]
    public string Highlight { get; set; } = "";

    [FirestoreProperty("sortOrder")]
    public long SortOrder { get; set; } = 0;

    [FirestoreProperty("mapSection")]
    public string MapSection { get; set; } = "";

    [FirestoreProperty("x")]
    public double? X { get; set; } = null;

    [FirestoreProperty("y")]
    public double? Y { get; set; } = null;

    [FirestoreProperty("positionNote")]
    public string PositionNote { get; set; } = "";

    public Hotspot ToHotspot() => new()
    {
        Id = Id, Name = Name, Image = Image, Summary = Summary, Description = Description,
        Highlight = Highlight, SortOrder = SortOrder is >= 1 and <= 100 ? (int)SortOrder : 0,
        MapSection = MapSection, X = X, Y = Y, PositionNote = PositionNote
    };
}
#endif
