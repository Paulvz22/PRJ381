#if FIREBASE_CONFIGURED
using Plugin.Firebase.Firestore;
#endif

namespace BCOpendayApp.Services.Campus;

/// <summary>Location photos in Firestore: one document locationPhotos/{locationId} per photo.</summary>
public sealed class FirestoreLocationPhotoStore : ILocationPhotoRemoteStore
{
    public const string CollectionName = "locationPhotos";

    private readonly IAuthService auth;

    public FirestoreLocationPhotoStore(IAuthService auth) => this.auth = auth;

    public bool IsConfigured => FirebaseAvailability.IsReady;

    public async Task<LocationPhoto?> GetAsync(string locationId, CancellationToken cancellationToken)
    {
#if FIREBASE_CONFIGURED
        if (!await auth.EnsureSignedInAsync().WaitAsync(cancellationToken))
        {
            throw new UnauthorizedAccessException("Firebase sign-in failed.");
        }

        var snapshot = await CrossFirebaseFirestore.Current
            .GetCollection(CollectionName)
            .GetDocument(locationId)
            .GetDocumentSnapshotAsync<FirestoreLocationPhoto>(Source.Server)
            .WaitAsync(cancellationToken);
        return snapshot.Data is { } data
            ? new LocationPhoto(data.Data, data.Sha256, data.MimeType, data.UpdatedAtUnixMs, data.UpdatedBy)
            : null;
#else
        await Task.CompletedTask;
        throw new NotSupportedException("Firebase is not configured in this build.");
#endif
    }

    public async Task SetAsync(string locationId, LocationPhoto photo)
    {
#if FIREBASE_CONFIGURED
        await CrossFirebaseFirestore.Current
            .GetCollection(CollectionName)
            .GetDocument(locationId)
            .SetDataAsync(new FirestoreLocationPhoto
            {
                Data = photo.Data,
                Sha256 = photo.Sha256,
                MimeType = photo.MimeType,
                UpdatedAtUnixMs = photo.UpdatedAtUnixMs,
                UpdatedBy = photo.UpdatedBy,
            });
#else
        await Task.CompletedTask;
        throw new NotSupportedException("Firebase is not configured in this build.");
#endif
    }
}

#if FIREBASE_CONFIGURED
public sealed class FirestoreLocationPhoto : IFirestoreObject
{
    [FirestoreProperty("data")]
    public string Data { get; set; } = "";

    [FirestoreProperty("sha256")]
    public string Sha256 { get; set; } = "";

    [FirestoreProperty("mimeType")]
    public string MimeType { get; set; } = "image/jpeg";

    [FirestoreProperty("updatedAtUnixMs")]
    public long UpdatedAtUnixMs { get; set; }

    [FirestoreProperty("updatedBy")]
    public string UpdatedBy { get; set; } = "";
}
#endif
