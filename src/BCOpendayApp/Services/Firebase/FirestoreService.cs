#if FIREBASE_CONFIGURED
using Plugin.Firebase.Firestore;
#endif

namespace BCOpendayApp.Services;

// Existing Member 1/2 interface is preserved. Location pages never call write operations.
public class FirestoreService : IFirestoreService
{
    public async Task SetDocumentAsync<T>(string collection, string documentId, T data) where T : class
    {
#if FIREBASE_CONFIGURED
        await CrossFirebaseFirestore.Current.GetCollection(collection).GetDocument(documentId).SetDataAsync(data);
#else
        await Task.CompletedTask;
        throw Offline();
#endif
    }

    public async Task<T?> GetDocumentAsync<T>(string collection, string documentId) where T : class
    {
#if FIREBASE_CONFIGURED
        var snapshot = await CrossFirebaseFirestore.Current.GetCollection(collection)
            .GetDocument(documentId).GetDocumentSnapshotAsync<T>();
        return snapshot.Data;
#else
        await Task.CompletedTask;
        throw Offline();
#endif
    }

    public async Task<List<T>> GetCollectionAsync<T>(string collection) where T : class
    {
#if FIREBASE_CONFIGURED
        // Explicit server reads allow the location service to label its fallback accurately.
        var snapshot = await CrossFirebaseFirestore.Current.GetCollection(collection)
            .GetDocumentsAsync<T>(Source.Server);
        return snapshot.Documents.Select(document => document.Data).OfType<T>().ToList();
#else
        await Task.CompletedTask;
        throw Offline();
#endif
    }

    public async Task DeleteDocumentAsync(string collection, string documentId)
    {
#if FIREBASE_CONFIGURED
        await CrossFirebaseFirestore.Current.GetCollection(collection).GetDocument(documentId).DeleteDocumentAsync();
#else
        await Task.CompletedTask;
        throw Offline();
#endif
    }

    private static NotSupportedException Offline() => new("Firebase is not configured on this target.");
}
