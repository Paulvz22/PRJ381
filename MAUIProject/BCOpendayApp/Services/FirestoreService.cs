using Plugin.Firebase.Firestore;

namespace BCOpendayApp.Services
{
    public class FirestoreService
    {
        // Saves any object to a given collection, under a given document ID.
        // Example: SetDocumentAsync("hotspots", "robotics_lab", hotspotData)
        public async Task SetDocumentAsync<T>(string collection, string documentId, T data) where T : class
        {
            await CrossFirebaseFirestore.Current
                .GetCollection(collection)
                .GetDocument(documentId)
                .SetDataAsync(data);
        }

        // Reads a single document back as the given type.
        // Returns null if it doesn't exist.
        public async Task<T?> GetDocumentAsync<T>(string collection, string documentId) where T : class
        {
            var snapshot = await CrossFirebaseFirestore.Current
                .GetCollection(collection)
                .GetDocument(documentId)
                .GetDocumentSnapshotAsync<T>();

            return snapshot.Data;
        }

        // Reads every document in a collection as a list of the given type.
        // Example: GetCollectionAsync<Hotspot>("hotspots")
        public async Task<List<T>> GetCollectionAsync<T>(string collection) where T : class
        {
            var snapshot = await CrossFirebaseFirestore.Current
                .GetCollection(collection)
                .GetDocumentsAsync<T>();

            var results = new List<T>();
            foreach (var doc in snapshot.Documents)
            {
                results.Add(doc.Data);
            }

            return results;
        }

        // Deletes a single document.
        public async Task DeleteDocumentAsync(string collection, string documentId)
        {
            await CrossFirebaseFirestore.Current
                .GetCollection(collection)
                .GetDocument(documentId)
                .DeleteDocumentAsync();
        }
    }
}