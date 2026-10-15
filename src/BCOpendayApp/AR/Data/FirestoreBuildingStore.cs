using BCOpendayApp.AR.Models;
using BCOpendayApp.Services;
#if FIREBASE_CONFIGURED
using Plugin.Firebase.Firestore;
#endif

namespace BCOpendayApp.AR.Data;

/// <summary>
/// Reads/writes the "buildings" collection. Visitors read with the anonymous account the app already uses;
/// writes only succeed for staff accounts listed in the "scanners" collection (see firebase/firestore.rules).
/// </summary>
public sealed class FirestoreBuildingStore : IBuildingRemoteStore
{
    public const string CollectionName = "buildings";

    private readonly IAuthService auth;

    public FirestoreBuildingStore(IAuthService auth) => this.auth = auth;

    public bool IsConfigured => FirebaseAvailability.IsReady;

    public async Task<IReadOnlyList<BuildingAnchor>> ReadAllAsync(CancellationToken cancellationToken)
    {
#if FIREBASE_CONFIGURED
        if (!await auth.EnsureSignedInAsync().WaitAsync(cancellationToken))
        {
            throw new UnauthorizedAccessException("Firebase sign-in failed.");
        }

        var snapshot = await CrossFirebaseFirestore.Current
            .GetCollection(CollectionName)
            .GetDocumentsAsync<FirestoreBuildingDocument>(Source.Server)
            .WaitAsync(cancellationToken);

        return snapshot.Documents
            .Select(document => document.Data)
            .OfType<FirestoreBuildingDocument>()
            .Select(document => document.ToAnchor())
            .ToList();
#else
        await Task.CompletedTask;
        throw new NotSupportedException("Firebase is not configured in this build.");
#endif
    }

    public async Task SaveAsync(BuildingAnchor building)
    {
#if FIREBASE_CONFIGURED
        await CrossFirebaseFirestore.Current
            .GetCollection(CollectionName)
            .GetDocument(building.Id)
            .SetDataAsync(FirestoreBuildingDocument.From(building));
#else
        await Task.CompletedTask;
        throw new NotSupportedException("Firebase is not configured in this build.");
#endif
    }

    public async Task DeleteAsync(string id)
    {
#if FIREBASE_CONFIGURED
        await CrossFirebaseFirestore.Current.GetCollection(CollectionName).GetDocument(id).DeleteDocumentAsync();
#else
        await Task.CompletedTask;
        throw new NotSupportedException("Firebase is not configured in this build.");
#endif
    }
}

#if FIREBASE_CONFIGURED
/// <summary>Firestore document shape. Field names here must match firebase/firestore.rules.</summary>
public sealed class FirestoreBuildingDocument : IFirestoreObject
{
    [FirestoreDocumentId]
    public string Id { get; set; } = "";

    [FirestoreProperty("name")]
    public string Name { get; set; } = "";

    [FirestoreProperty("summary")]
    public string Summary { get; set; } = "";

    [FirestoreProperty("description")]
    public string Description { get; set; } = "";

    [FirestoreProperty("stopId")]
    public string StopId { get; set; } = "";

    [FirestoreProperty("centerLat")]
    public double CenterLat { get; set; }

    [FirestoreProperty("centerLng")]
    public double CenterLng { get; set; }

    [FirestoreProperty("radiusMeters")]
    public double RadiusMeters { get; set; }

    /// <summary>"lat,lng;lat,lng;..." or empty.</summary>
    [FirestoreProperty("footprint")]
    public string Footprint { get; set; } = "";

    [FirestoreProperty("isActive")]
    public bool IsActive { get; set; } = true;

    [FirestoreProperty("updatedAtUnixMs")]
    public long UpdatedAtUnixMs { get; set; }

    [FirestoreProperty("updatedBy")]
    public string UpdatedBy { get; set; } = "";

    public BuildingAnchor ToAnchor() => new()
    {
        Id = Id,
        Name = Name,
        Summary = Summary,
        Description = Description,
        StopId = StopId,
        CenterLat = CenterLat,
        CenterLng = CenterLng,
        RadiusMeters = RadiusMeters,
        Footprint = BuildingValidation.ParseFootprint(Footprint),
        IsActive = IsActive,
        UpdatedAtUnixMs = UpdatedAtUnixMs,
        UpdatedBy = UpdatedBy,
    };

    public static FirestoreBuildingDocument From(BuildingAnchor b) => new()
    {
        Id = b.Id,
        Name = b.Name,
        Summary = b.Summary,
        Description = b.Description,
        StopId = b.StopId,
        CenterLat = b.CenterLat,
        CenterLng = b.CenterLng,
        RadiusMeters = b.RadiusMeters,
        Footprint = BuildingValidation.FormatFootprint(b.Footprint),
        IsActive = b.IsActive,
        UpdatedAtUnixMs = b.UpdatedAtUnixMs,
        UpdatedBy = b.UpdatedBy,
    };
}
#endif
