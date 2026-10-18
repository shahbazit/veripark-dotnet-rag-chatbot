using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MyApplication.Models;
using MyApplication.Services.Interfaces;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace MyApplication.Services.Implementations;

public class QdrantVectorService : IQdrantVectorService
{
    private readonly QdrantClient _client;
    private readonly QdrantSettings _settings;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public QdrantVectorService(IOptions<QdrantSettings> settings)
    {
        _settings = settings.Value;

        if (string.IsNullOrWhiteSpace(_settings.Endpoint))
        {
            throw new InvalidOperationException("QdrantSettings:Endpoint is required.");
        }

        var uri = new Uri(_settings.Endpoint);
        int port = _settings.Port > 0 ? _settings.Port : (uri.Port > 0 ? uri.Port : 6334);
        bool useHttps = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

        string? apiKey = string.IsNullOrWhiteSpace(_settings.ApiKey) ? null : _settings.ApiKey;
        _client = new QdrantClient(
            host: uri.Host,
            port: port,
            https: useHttps,
            apiKey: apiKey
        );
    }

    public async Task EnsureCollectionAsync(int vectorSize = 1536, CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            string collection = _settings.CollectionName;
            bool exists = await _client.CollectionExistsAsync(collection, cancellationToken);

            if (!exists)
            {
                try
                {
                    await _client.CreateCollectionAsync(
                        collectionName: collection,
                        vectorsConfig: new VectorParams
                        {
                            Size = (ulong)vectorSize,
                            Distance = Distance.Cosine
                        },
                        cancellationToken: cancellationToken
                    );
                }
                catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.AlreadyExists || ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                }
            }

            try
            {
                await _client.CreatePayloadIndexAsync(
                    collectionName: collection,
                    fieldName: "document_name",
                    schemaType: PayloadSchemaType.Keyword,
                    cancellationToken: cancellationToken
                );
            }
            catch
            {
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<bool> UpsertChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        if (chunkList.Count == 0) return true;

        var sampleWithEmbedding = chunkList.FirstOrDefault(c => c.Embedding != null && c.Embedding.Length > 0);
        int vectorSize = sampleWithEmbedding?.Embedding?.Length ?? 1536;

        await EnsureCollectionAsync(vectorSize, cancellationToken);

        var points = new List<PointStruct>();

        foreach (var chunk in chunkList)
        {
            if (chunk.Embedding == null || chunk.Embedding.Length == 0) continue;

            var pointGuid = GeneratePointGuid(chunk.DocumentName, chunk.ChunkIndex);

            var point = new PointStruct
            {
                Id = pointGuid,
                Vectors = chunk.Embedding
            };

            point.Payload.Add("chunk_id", chunk.Id);
            point.Payload.Add("document_name", chunk.DocumentName);
            point.Payload.Add("page_number", chunk.PageNumber);
            point.Payload.Add("chunk_index", chunk.ChunkIndex);
            point.Payload.Add("text", chunk.Text);
            point.Payload.Add("uploaded_at", DateTime.UtcNow.ToString("o"));

            points.Add(point);
        }

        if (points.Count > 0)
        {
            await _client.UpsertAsync(_settings.CollectionName, points, cancellationToken: cancellationToken);
            return true;
        }

        return false;
    }

    public async Task<List<DocumentChunk>> SearchSimilarChunksAsync(
        float[] queryVector,
        int topK = 3,
        float scoreThreshold = 0.0f,
        string? filterDocumentName = null,
        CancellationToken cancellationToken = default)
    {
        if (queryVector == null || queryVector.Length == 0)
        {
            return new List<DocumentChunk>();
        }

        await EnsureCollectionAsync(queryVector.Length, cancellationToken);

        Filter? filter = null;
        if (!string.IsNullOrWhiteSpace(filterDocumentName))
        {
            filter = Conditions.MatchKeyword("document_name", filterDocumentName.Trim());
        }

        try
        {
#pragma warning disable CS0618
            IReadOnlyList<ScoredPoint> searchResults;
            try
            {
                searchResults = await _client.SearchAsync(
                    collectionName: _settings.CollectionName,
                    vector: queryVector,
                    filter: filter,
                    limit: (ulong)topK,
                    scoreThreshold: scoreThreshold > 0.01f ? scoreThreshold : null,
                    payloadSelector: true,
                    cancellationToken: cancellationToken
                );
            }
            catch when (filter != null)
            {
                searchResults = await _client.SearchAsync(
                    collectionName: _settings.CollectionName,
                    vector: queryVector,
                    filter: null,
                    limit: (ulong)topK,
                    scoreThreshold: scoreThreshold > 0.01f ? scoreThreshold : null,
                    payloadSelector: true,
                    cancellationToken: cancellationToken
                );
            }

            var results = new List<DocumentChunk>();
            foreach (var point in searchResults)
            {
                var chunk = PointToDocumentChunk(point);
                if (chunk != null)
                {
                    chunk.SimilarityScore = point.Score;
                    results.Add(chunk);
                }
            }

            if (results.Count == 0 && (scoreThreshold > 0.01f || filter != null))
            {
                var relaxedResults = await _client.SearchAsync(
                    collectionName: _settings.CollectionName,
                    vector: queryVector,
                    filter: null,
                    limit: (ulong)topK,
                    payloadSelector: true,
                    cancellationToken: cancellationToken
                );

                foreach (var point in relaxedResults)
                {
                    var chunk = PointToDocumentChunk(point);
                    if (chunk != null)
                    {
                        chunk.SimilarityScore = point.Score;
                        results.Add(chunk);
                    }
                }
            }
#pragma warning restore CS0618

            return results;
        }
        catch
        {
            return new List<DocumentChunk>();
        }
    }

    public async Task<bool> DeleteDocumentChunksAsync(string documentName, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Conditions.MatchKeyword("document_name", documentName);
            await _client.DeleteAsync(_settings.CollectionName, filter: filter, cancellationToken: cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _client.CollectionExistsAsync(_settings.CollectionName, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task<ulong> GetPointsCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var collectionInfo = await _client.GetCollectionInfoAsync(_settings.CollectionName, cancellationToken);
            return collectionInfo.PointsCount;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<bool> ClearCollectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DeleteCollectionAsync(_settings.CollectionName, cancellationToken: cancellationToken);
            _initialized = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static DocumentChunk? PointToDocumentChunk(ScoredPoint point)
    {
        if (point.Payload == null) return null;

        var payload = point.Payload;
        string chunkId = payload.TryGetValue("chunk_id", out var cId) ? cId.StringValue : point.Id.Uuid;
        string docName = payload.TryGetValue("document_name", out var dn) ? dn.StringValue : "Unknown";
        int pageNumber = payload.TryGetValue("page_number", out var pn) ? (int)pn.IntegerValue : 1;
        int chunkIndex = payload.TryGetValue("chunk_index", out var ci) ? (int)ci.IntegerValue : 0;
        string text = payload.TryGetValue("text", out var t) ? t.StringValue : string.Empty;

        return new DocumentChunk
        {
            Id = chunkId,
            DocumentName = docName,
            PageNumber = pageNumber,
            ChunkIndex = chunkIndex,
            Text = text,
            SimilarityScore = point.Score
        };
    }

    private static Guid GeneratePointGuid(string docName, int chunkIndex)
    {
        using var md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes($"{docName}:{chunkIndex}"));
        return new Guid(hash);
    }
}
