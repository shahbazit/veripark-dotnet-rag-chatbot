namespace MyApplication.Models;

public class DocumentPage
{
    public int PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;
}

public class DocumentChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string DocumentName { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public int CharacterCount => Text.Length;
    public int WordCount => Text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
    public float[]? Embedding { get; set; }
    public float SimilarityScore { get; set; }
}

public class DocumentSummary
{
    public string DocumentId { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public int ChunkCount { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public class ChatMessageDto
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ChatQueryRequest
{
    public string SessionId { get; set; } = "default-session";
    public string UserId { get; set; } = "default-user";
    public string Question { get; set; } = string.Empty;
    public List<ChatMessageDto> History { get; set; } = new();
    public int TopK { get; set; } = 3;
    public float MinRelevanceScore { get; set; } = 0.05f;
    public string? SelectedDocumentName { get; set; }
}

public class ChatQueryResponse
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public List<DocumentChunk> RetrievedChunks { get; set; } = new();
    public List<CitationDto> Citations { get; set; } = new();
    public long LatencyMs { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string? UserMemoryUsed { get; set; }
}

public class CitationDto
{
    public string DocumentName { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public int ChunkIndex { get; set; }
    public float RelevanceScore { get; set; }
    public string Snippet { get; set; } = string.Empty;
}

public class UserMemoryRequest
{
    public string UserId { get; set; } = "default-user";
    public string Memory { get; set; } = string.Empty;
}