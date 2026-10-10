using Plugin.Firebase.Firestore;

namespace BCOpendayApp.Services;

public class FirestoreService : IFirestoreService
{
    public async Task SetDocumentAsync<T>(string collection, string documentId, T data) where T : class
    {
        await CrossFirebaseFirestore.Current
            .GetCollection(collection).GetDocument(documentId).SetDataAsync(data);
    }

    public async Task<T?> GetDocumentAsync<T>(string collection, string documentId) where T : class
    {
        var snapshot = await CrossFirebaseFirestore.Current
            .GetCollection(collection).GetDocument(documentId).GetDocumentSnapshotAsync<T>();
        return snapshot.Data;
    }

    public async Task<List<T>> GetCollectionAsync<T>(string collection) where T : class
    {
        var snapshot = await CrossFirebaseFirestore.Current
            .GetCollection(collection).GetDocumentsAsync<T>();

        var results = new List<T>();
        foreach (var doc in snapshot.Documents)
            results.Add(doc.Data);
        return results;
    }

    public async Task DeleteDocumentAsync(string collection, string documentId)
    {
        await CrossFirebaseFirestore.Current
            .GetCollection(collection).GetDocument(documentId).DeleteDocumentAsync();
    }
}
