namespace BCOpendayApp.Services;

public interface IFirestoreService
{
    Task SetDocumentAsync<T>(string collection, string documentId, T data) where T : class;
    Task<T?> GetDocumentAsync<T>(string collection, string documentId) where T : class;
    Task<List<T>> GetCollectionAsync<T>(string collection) where T : class;
    Task DeleteDocumentAsync(string collection, string documentId);
}
