using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MyApplication.Models;
using MyApplication.Services.Interfaces;
using UglyToad.PdfPig;

namespace MyApplication.Services.Implementations;

public class PdfRagService : IPdfRagService
{
    private readonly ISemanticKernelService _semanticKernel;
    private readonly IQdrantVectorService _vectorStore;
    private readonly IChatMemoryService _memoryService;
    private readonly RagSettings _settings;

    public PdfRagService(
        ISemanticKernelService semanticKernel,
        IQdrantVectorService vectorStore,
        IChatMemoryService memoryService,
        IOptions<RagSettings> settings)
    {
        _semanticKernel = semanticKernel;
        _vectorStore = vectorStore;
        _memoryService = memoryService;
        _settings = settings.Value;
    }

    public async Task<List<DocumentPage>> ExtractPagesAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        var pages = new List<DocumentPage>();

        using var memoryStream = new MemoryStream();
        if (pdfStream.CanSeek && pdfStream.Position != 0)
        {
            pdfStream.Position = 0;
        }
        await pdfStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        try
        {
            using var document = PdfDocument.Open(memoryStream);

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rawText = page.Text ?? string.Empty;
                var cleanedText = CleanText(rawText);

                if (!string.IsNullOrWhiteSpace(cleanedText))
                {
                    pages.Add(new DocumentPage
                    {
                        PageNumber = page.Number,
                        Text = cleanedText
                    });
                }
            }
        }
        catch
        {
            throw;
        }

        return pages;
    }

    public List<DocumentChunk> ChunkPages(string documentName, List<DocumentPage> pages, int chunkSize, int chunkOverlap)
    {
        var chunks = new List<DocumentChunk>();
        int globalChunkIndex = 1;

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Text))
                continue;

            int startIndex = 0;
            while (startIndex < page.Text.Length)
            {
                int remainingLength = page.Text.Length - startIndex;
                int currentChunkSize = Math.Min(chunkSize, remainingLength);

                int splitIndex = currentChunkSize;
                if (startIndex + currentChunkSize < page.Text.Length)
                {
                    int lastPeriod = page.Text.LastIndexOf('.', startIndex + currentChunkSize, Math.Min(currentChunkSize, 100));
                    int lastNewline = page.Text.LastIndexOf('\n', startIndex + currentChunkSize, Math.Min(currentChunkSize, 100));
                    int bestBreak = Math.Max(lastPeriod, lastNewline);

                    if (bestBreak > startIndex + (chunkSize / 2))
                    {
                        splitIndex = (bestBreak - startIndex) + 1;
                    }
                }

                string chunkContent = page.Text.Substring(startIndex, splitIndex).Trim();

                if (!string.IsNullOrWhiteSpace(chunkContent) && chunkContent.Length >= 40)
                {
                    chunks.Add(new DocumentChunk
                    {
                        DocumentName = documentName,
                        PageNumber = page.PageNumber,
                        ChunkIndex = globalChunkIndex++,
                        Text = chunkContent
                    });
                }

                if (startIndex + splitIndex >= page.Text.Length)
                {
                    break;
                }

                int advance = splitIndex - chunkOverlap;
                if (advance <= 0)
                {
                    advance = Math.Max(1, splitIndex);
                }

                startIndex += advance;
            }
        }

        return chunks;
    }

    public async Task<DocumentSummary> IngestPdfAsync(
        Stream pdfStream,
        string fileName,
        int? chunkSize = null,
        int? chunkOverlap = null,
        CancellationToken cancellationToken = default)
    {
        long initialStreamLength = pdfStream.CanSeek ? pdfStream.Length : 0;
        var pages = await ExtractPagesAsync(pdfStream, cancellationToken);

        int effChunkSize = chunkSize ?? _settings.ChunkSize;
        int effOverlap = chunkOverlap ?? _settings.ChunkOverlap;
        var chunks = ChunkPages(fileName, pages, effChunkSize, effOverlap);

        for (int i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            chunks[i].Embedding = await _semanticKernel.GenerateEmbeddingAsync(chunks[i].Text, cancellationToken);
        }

        await _vectorStore.UpsertChunksAsync(chunks, cancellationToken);

        var summary = new DocumentSummary
        {
            FileName = fileName,
            PageCount = pages.Count,
            ChunkCount = chunks.Count,
            FileSizeBytes = initialStreamLength,
            UploadedAt = DateTime.UtcNow
        };

        await _memoryService.SetDocumentSummaryAsync(summary.DocumentId, summary);

        return summary;
    }

    public async Task<ChatQueryResponse> AskQuestionAsync(ChatQueryRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = new ChatQueryResponse
        {
            Question = request.Question
        };

        try
        {
            string userId = string.IsNullOrWhiteSpace(request.UserId) ? "default-user" : request.UserId.Trim();
            string sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? "default-session" : request.SessionId.Trim();

            string? userMemory = await _memoryService.GetUserMemoryAsync(userId);
            if (Regex.IsMatch(request.Question, @"(?i)\b(my name is|call me|remember that|i am an?|i work as|i prefer|my role is)\b"))
            {
                string fact = request.Question.Trim();
                userMemory = string.IsNullOrWhiteSpace(userMemory) ? fact : $"{userMemory}\n- {fact}";
                await _memoryService.SetUserMemoryAsync(userId, userMemory);
            }

            response.UserMemoryUsed = userMemory;

            var history = request.History;
            if ((history == null || history.Count == 0) && !string.IsNullOrWhiteSpace(sessionId))
            {
                history = await _memoryService.GetChatHistoryAsync(sessionId, 50);
            }

            var questionEmbedding = await _semanticKernel.GenerateEmbeddingAsync(request.Question, cancellationToken);

            int topK = request.TopK > 0 ? request.TopK : _settings.DefaultTopK;
            float minScore = request.MinRelevanceScore > 0 ? request.MinRelevanceScore : _settings.MinRelevanceScore;

            var retrieved = await _vectorStore.SearchSimilarChunksAsync(
                queryVector: questionEmbedding,
                topK: topK,
                scoreThreshold: minScore,
                filterDocumentName: request.SelectedDocumentName,
                cancellationToken: cancellationToken);

            response.RetrievedChunks = retrieved;

            foreach (var chunk in retrieved)
            {
                response.Citations.Add(new CitationDto
                {
                    DocumentName = chunk.DocumentName,
                    PageNumber = chunk.PageNumber,
                    ChunkIndex = chunk.ChunkIndex,
                    RelevanceScore = chunk.SimilarityScore,
                    Snippet = chunk.Text.Length > 200 ? chunk.Text[..200] + "..." : chunk.Text
                });
            }

            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("You are an expert AI assistant strictly grounded in the Microsoft Copilot Studio Licensing Guide.");
            contextBuilder.AppendLine("Answer the user's question accurately, completely, and solely based on the provided retrieved context below.");
            contextBuilder.AppendLine("Provide a direct, complete, and properly structured answer based strictly on the retrieved context. Do NOT include inline citations, document filenames, or page numbers inside the answer text (citations are displayed separately).");
            contextBuilder.AppendLine("If the retrieved context does not contain enough information to answer the question, clearly state: 'The provided document does not contain sufficient information to answer this question.' Do NOT hallucinate or extrapolate outside the document.");
            contextBuilder.AppendLine();

            if (!string.IsNullOrWhiteSpace(userMemory))
            {
                contextBuilder.AppendLine("=== USER PERSISTENT PROFILE & MEMORY ===");
                contextBuilder.AppendLine("The user has the following profile, facts, and preferences:");
                contextBuilder.AppendLine(userMemory);
                contextBuilder.AppendLine("Always acknowledge these facts and adapt answers to their profile when relevant.");
                contextBuilder.AppendLine("=========================================");
                contextBuilder.AppendLine();
            }

            contextBuilder.AppendLine("=== RETRIEVED CONTEXT START ===");
            for (int i = 0; i < retrieved.Count; i++)
            {
                var c = retrieved[i];
                contextBuilder.AppendLine($"[Source {i + 1}] (Document: {c.DocumentName}, Page: {c.PageNumber}, Score: {c.SimilarityScore:F2})");
                contextBuilder.AppendLine(c.Text);
                contextBuilder.AppendLine();
            }
            contextBuilder.AppendLine("=== RETRIEVED CONTEXT END ===");

            string answer = await _semanticKernel.GenerateChatResponseAsync(
                userMessage: request.Question,
                systemPrompt: contextBuilder.ToString(),
                history: history,
                cancellationToken: cancellationToken);

            response.Answer = answer;
            response.IsSuccess = true;

            await _memoryService.AppendChatHistoryAsync(sessionId, new ChatMessageDto
            {
                Role = "user",
                Content = request.Question
            });

            await _memoryService.AppendChatHistoryAsync(sessionId, new ChatMessageDto
            {
                Role = "assistant",
                Content = answer
            });
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorMessage = ex.Message;
            response.Answer = $"An error occurred while answering your question: {ex.Message}";
        }
        finally
        {
            stopwatch.Stop();
            response.LatencyMs = stopwatch.ElapsedMilliseconds;
        }

        return response;
    }

    public async Task<bool> EnsureDefaultDocumentIngestedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string[] candidatePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assessment - Senior AI Engineer - 260716", "Microsoft-Copilot-Studio-Licensing-Guide.pdf"),
                Path.Combine(Directory.GetCurrentDirectory(), "Assessment - Senior AI Engineer - 260716", "Microsoft-Copilot-Studio-Licensing-Guide.pdf"),
                @"D:\OneDrive\MyComputer\VeriPark\MyApplication\Assessment - Senior AI Engineer - 260716\Microsoft-Copilot-Studio-Licensing-Guide.pdf"
            };

            string? targetPath = candidatePaths.FirstOrDefault(File.Exists);
            if (targetPath == null)
            {
                return false;
            }

            string targetFileName = Path.GetFileName(targetPath);

            try
            {
                var existing = await _vectorStore.SearchSimilarChunksAsync(
                    queryVector: new float[1536],
                    topK: 1,
                    scoreThreshold: -1.0f,
                    filterDocumentName: targetFileName,
                    cancellationToken: cancellationToken);

                if (existing != null && existing.Any(c => string.Equals(c.DocumentName, targetFileName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            catch
            {
            }

            using var stream = File.OpenRead(targetPath);
            await IngestPdfAsync(stream, targetFileName, cancellationToken: cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        return await _semanticKernel.GenerateEmbeddingAsync(text, cancellationToken);
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string cleaned = Regex.Replace(text, @"[^\u0009\u000A\u000D\u0020-\uD7FF\uE000-\uFFFD]", " ");
        cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
        cleaned = Regex.Replace(cleaned, @"(\r?\n){3,}", "\n\n");
        return cleaned.Trim();
    }
}