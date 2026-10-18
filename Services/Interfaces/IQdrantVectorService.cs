using MyApplication.Models;

namespace MyApplication.Services.Interfaces;

public interface IQdrantVectorService
{
    Task EnsureCollectionAsync(int vectorSize = 1536, CancellationToken cancellationToken = default);
    Task<bool> UpsertChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default);
    Task<List<DocumentChunk>> SearchSimilarChunksAsync(
        float[] queryVector,
        int topK = 3,
        float scoreThreshold = 0.0f,
        string? filterDocumentName = null,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentChunksAsync(string documentName, CancellationToken cancellationToken = default);
    Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default);
    Task<ulong> GetPointsCountAsync(CancellationToken cancellationToken = default);
    Task<bool> ClearCollectionAsync(CancellationToken cancellationToken = default);
}
