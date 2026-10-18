using MyApplication.Models;

namespace MyApplication.Services.Interfaces;

public interface IPdfRagService
{
    Task<List<DocumentPage>> ExtractPagesAsync(Stream pdfStream, CancellationToken cancellationToken = default);
    List<DocumentChunk> ChunkPages(string documentName, List<DocumentPage> pages, int chunkSize = 500, int chunkOverlap = 100);
    Task<DocumentSummary> IngestPdfAsync(Stream pdfStream, string fileName, int? chunkSize = null, int? chunkOverlap = null, CancellationToken cancellationToken = default);
    Task<ChatQueryResponse> AskQuestionAsync(ChatQueryRequest request, CancellationToken cancellationToken = default);
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<bool> EnsureDefaultDocumentIngestedAsync(CancellationToken cancellationToken = default);
}
