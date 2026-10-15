using System.Diagnostics;
using BCOpendayApp.Models;

namespace BCOpendayApp.Services;

public sealed class FirestoreLocationSource : ILocationRemoteSource
{
    public const string CollectionName = "locations";
    private readonly IAuthService auth;
    private readonly IFirestoreService firestore;
    private readonly object sync = new();
    private Task<IReadOnlyList<Hotspot>>? inFlight;

    public FirestoreLocationSource(IAuthService auth, IFirestoreService firestore)
    {
        this.auth = auth;
        this.firestore = firestore;
    }

    public bool IsConfigured => FirebaseAvailability.IsReady;

    public async Task<IReadOnlyList<Hotspot>> ReadAsync(CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<Hotspot>> request;
        lock (sync)
        {
            // The native SDK cannot cancel a get/sign-in. Reuse it instead of starting
            // unlimited requests when a visitor repeatedly refreshes on a poor connection.
            if (inFlight is null || inFlight.IsCompleted)
            {
                inFlight = ReadCoreAsync();
                _ = inFlight.ContinueWith(task => Debug.WriteLine(task.Exception),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
            request = inFlight;
        }
        return await request.WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Hotspot>> ReadCoreAsync()
    {
#if FIREBASE_CONFIGURED
        if (!IsConfigured) throw new InvalidOperationException("Firebase is not initialized.");
        if (!await auth.EnsureSignedInAsync())
            throw new UnauthorizedAccessException("Anonymous Firebase sign-in failed.");
        var documents = await firestore.GetCollectionAsync<FirestoreHotspotDocument>(CollectionName);
        return documents.Select(document => document.ToHotspot()).ToArray();
#else
        await Task.CompletedTask;
        throw new NotSupportedException("This build uses bundled offline locations.");
#endif
    }
}
