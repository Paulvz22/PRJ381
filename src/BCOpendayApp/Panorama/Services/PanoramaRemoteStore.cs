using System.Globalization;
using BCOpendayApp.Panorama.Models;
using BCOpendayApp.Services;
#if FIREBASE_CONFIGURED
using Plugin.Firebase.Firestore;
#endif

namespace BCOpendayApp.Panorama.Services;

/// <summary>
/// Firestore implementation. Reads use the anonymous visitor account the app already signs in with; writes are
/// only accepted from staff accounts on the "scanners" list (firebase/firestore.rules, same as buildings).
/// </summary>
public sealed class FirestorePanoramaStore : IPanoramaRemoteStore
{
    public const string CollectionName = "panoramas";
    public const string ChunkCollectionName = "chunks";

    private readonly IAuthService auth;

    public FirestorePanoramaStore(IAuthService auth) => this.auth = auth;

    public bool IsConfigured => FirebaseAvailability.IsReady;

    public async Task<IReadOnlyList<PanoramaManifest>> ListAsync(CancellationToken cancellationToken)
    {
#if FIREBASE_CONFIGURED
        await EnsureSignedInAsync(cancellationToken);
        var snapshot = await CrossFirebaseFirestore.Current
            .GetCollection(CollectionName)
            .GetDocumentsAsync<FirestorePanoramaDocument>(Source.Server)
            .WaitAsync(cancellationToken);

        return snapshot.Documents
            .Select(document => document.Data)
            .OfType<FirestorePanoramaDocument>()
            .Select(document => document.ToManifest())
            .Where(PanoramaChunks.IsValid)
            .ToList();
#else
        await Task.CompletedTask;
        throw new NotSupportedException("Firebase is not configured in this build.");
#endif
    }

    public async Task<byte[]> DownloadAsync(PanoramaManifest manifest, CancellationToken cancellationToken)
    {
#if FIREBASE_CONFIGURED
        await EnsureSignedInAsync(cancellationToken);
        var chunks = Chunks(manifest.Id);
        var downloads = Enumerable.Range(0, manifest.ChunkCount)
            .Select(index => chunks
                .GetDocument(index.ToString(CultureInfo.InvariantCulture))
                .GetDocumentSnapshotAsync<FirestorePanoramaChunk>(Source.Server)
                .WaitAsync(cancellationToken))
            .ToList();
        var snapshots = await Task.WhenAll(downloads);

        var pieces = new List<string>(manifest.ChunkCount);
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Data is not { } chunk || chunk.UploadId != manifest.UploadId)
            {
                throw new InvalidDataException("This view is being re-uploaded. Try again in a minute.");
            }

            pieces.Add(chunk.Data);
        }

        return PanoramaChunks.Join(pieces, manifest);
#else
        await Task.CompletedTask;
        throw new NotSupportedException("Firebase is not configured in this build.");
#endif
    }

    public async Task<PanoramaManifest> UploadAsync(PanoramaDraft draft, byte[] image, string uid, IProgress<double>? progress)
    {
#if FIREBASE_CONFIGURED
        var pieces = PanoramaChunks.Split(image);
        var manifest = PanoramaManifest.FromDraft(
            draft, image, Guid.NewGuid().ToString("N"), uid, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        // An older upload of this view may have had more pieces; remember how many to clean up afterwards.
        var previous = await CrossFirebaseFirestore.Current
            .GetCollection(CollectionName)
            .GetDocument(draft.Id)
            .GetDocumentSnapshotAsync<FirestorePanoramaDocument>(Source.Server);
        var previousCount = previous.Data?.ChunkCount ?? 0;

        var chunks = Chunks(draft.Id);
        for (var index = 0; index < pieces.Count; index++)
        {
            await chunks.GetDocument(index.ToString(CultureInfo.InvariantCulture)).SetDataAsync(new FirestorePanoramaChunk
            {
                Data = pieces[index],
                UploadId = manifest.UploadId,
                Index = index,
            });
            progress?.Report((index + 1) / (double)(pieces.Count + 1));
        }

        // Written last: until now visitors still see the previous complete version (or nothing).
        await CrossFirebaseFirestore.Current
            .GetCollection(CollectionName)
            .GetDocument(draft.Id)
            .SetDataAsync(FirestorePanoramaDocument.From(manifest));
        progress?.Report(1);

        for (var index = pieces.Count; index < previousCount; index++)
        {
            await DeleteQuietlyAsync(chunks.GetDocument(index.ToString(CultureInfo.InvariantCulture)));
        }

        return manifest;
#else
        await Task.CompletedTask;
        throw new NotSupportedException("Firebase is not configured in this build.");
#endif
    }

    public async Task DeleteAsync(PanoramaManifest manifest)
    {
#if FIREBASE_CONFIGURED
        // Manifest first, so visitors stop seeing the view before its pieces disappear.
        await CrossFirebaseFirestore.Current.GetCollection(CollectionName).GetDocument(manifest.Id).DeleteDocumentAsync();
        var chunks = Chunks(manifest.Id);
        for (var index = 0; index < Math.Max(manifest.ChunkCount, PanoramaChunks.MaxChunks); index++)
        {
            await DeleteQuietlyAsync(chunks.GetDocument(index.ToString(CultureInfo.InvariantCulture)));
        }
#else
        await Task.CompletedTask;
        throw new NotSupportedException("Firebase is not configured in this build.");
#endif
    }

#if FIREBASE_CONFIGURED
    private static ICollectionReference Chunks(string id) => CrossFirebaseFirestore.Current
        .GetCollection(CollectionName)
        .GetDocument(id)
        .GetCollection(ChunkCollectionName);

    private static async Task DeleteQuietlyAsync(IDocumentReference document)
    {
        try
        {
            await document.DeleteDocumentAsync(); // deleting a piece that does not exist is fine
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Could not delete panorama piece: {exception.Message}");
        }
    }

    private async Task EnsureSignedInAsync(CancellationToken cancellationToken)
    {
        if (!await auth.EnsureSignedInAsync().WaitAsync(cancellationToken))
        {
            throw new UnauthorizedAccessException("Firebase sign-in failed.");
        }
    }
#endif
}

#if FIREBASE_CONFIGURED
/// <summary>panoramas/{id}. Field names must match firebase/firestore.rules.</summary>
public sealed class FirestorePanoramaDocument : IFirestoreObject
{
    [FirestoreDocumentId]
    public string Id { get; set; } = "";

    [FirestoreProperty("name")]
    public string Name { get; set; } = "";

    [FirestoreProperty("width")]
    public long Width { get; set; }

    [FirestoreProperty("height")]
    public long Height { get; set; }

    [FirestoreProperty("verticalFov")]
    public double VerticalFov { get; set; }

    [FirestoreProperty("isFullSphere")]
    public bool IsFullSphere { get; set; }

    [FirestoreProperty("captureMethod")]
    public string CaptureMethod { get; set; } = "";

    [FirestoreProperty("photoCount")]
    public long PhotoCount { get; set; }

    [FirestoreProperty("hasLocation")]
    public bool HasLocation { get; set; }

    [FirestoreProperty("lat")]
    public double Lat { get; set; }

    [FirestoreProperty("lng")]
    public double Lng { get; set; }

    [FirestoreProperty("accuracyMeters")]
    public double AccuracyMeters { get; set; }

    [FirestoreProperty("hasNorthOffset")]
    public bool HasNorthOffset { get; set; }

    [FirestoreProperty("northOffset")]
    public double NorthOffset { get; set; }

    [FirestoreProperty("locationId")]
    public string LocationId { get; set; } = "";

    [FirestoreProperty("description")]
    public string Description { get; set; } = "";

    [FirestoreProperty("mimeType")]
    public string MimeType { get; set; } = "image/jpeg";

    [FirestoreProperty("chunkCount")]
    public long ChunkCount { get; set; }

    [FirestoreProperty("byteLength")]
    public long ByteLength { get; set; }

    [FirestoreProperty("sha256")]
    public string Sha256 { get; set; } = "";

    [FirestoreProperty("uploadId")]
    public string UploadId { get; set; } = "";

    [FirestoreProperty("updatedAtUnixMs")]
    public long UpdatedAtUnixMs { get; set; }

    [FirestoreProperty("updatedBy")]
    public string UpdatedBy { get; set; } = "";

    public PanoramaManifest ToManifest() => new()
    {
        Id = Id,
        Name = Name,
        Width = (int)Math.Clamp(Width, 0, int.MaxValue),
        Height = (int)Math.Clamp(Height, 0, int.MaxValue),
        VerticalFovDegrees = VerticalFov,
        IsFullSphere = IsFullSphere,
        CaptureMethod = CaptureMethod,
        PhotoCount = (int)Math.Clamp(PhotoCount, 0, int.MaxValue),
        HasLocation = HasLocation,
        Latitude = Lat,
        Longitude = Lng,
        LocationAccuracyMeters = AccuracyMeters,
        HasNorthOffset = HasNorthOffset,
        NorthOffsetDegrees = NorthOffset,
        LocationId = LocationId ?? "",
        Description = Description ?? "",
        MimeType = MimeType,
        ChunkCount = (int)Math.Clamp(ChunkCount, 0, int.MaxValue),
        ByteLength = ByteLength,
        Sha256 = Sha256,
        UploadId = UploadId,
        UpdatedAtUnixMs = UpdatedAtUnixMs,
        UpdatedBy = UpdatedBy,
    };

    public static FirestorePanoramaDocument From(PanoramaManifest m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        Width = m.Width,
        Height = m.Height,
        VerticalFov = m.VerticalFovDegrees,
        IsFullSphere = m.IsFullSphere,
        CaptureMethod = m.CaptureMethod,
        PhotoCount = m.PhotoCount,
        HasLocation = m.HasLocation,
        Lat = m.Latitude,
        Lng = m.Longitude,
        AccuracyMeters = m.LocationAccuracyMeters,
        HasNorthOffset = m.HasNorthOffset,
        NorthOffset = m.NorthOffsetDegrees,
        LocationId = m.LocationId,
        Description = m.Description,
        MimeType = m.MimeType,
        ChunkCount = m.ChunkCount,
        ByteLength = m.ByteLength,
        Sha256 = m.Sha256,
        UploadId = m.UploadId,
        UpdatedAtUnixMs = m.UpdatedAtUnixMs,
        UpdatedBy = m.UpdatedBy,
    };
}

/// <summary>panoramas/{id}/chunks/{index}: one piece of the image as base64 text.</summary>
public sealed class FirestorePanoramaChunk : IFirestoreObject
{
    [FirestoreProperty("data")]
    public string Data { get; set; } = "";

    [FirestoreProperty("uploadId")]
    public string UploadId { get; set; } = "";

    [FirestoreProperty("index")]
    public long Index { get; set; }
}
#endif
