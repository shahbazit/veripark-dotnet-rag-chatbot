using MyApplication.Models;

namespace MyApplication.Services.Interfaces;

public interface IChatMemoryService
{
    Task<long> AppendChatHistoryAsync(string sessionId, ChatMessageDto message);
    Task<List<ChatMessageDto>> GetChatHistoryAsync(string sessionId, int limit = 50);
    Task<bool> ClearChatHistoryAsync(string sessionId);
    Task<string?> GetUserMemoryAsync(string userId);
    Task<bool> SetUserMemoryAsync(string userId, string memory);
    Task<bool> ClearUserMemoryAsync(string userId);
    Task<bool> SetDocumentSummaryAsync(string documentId, DocumentSummary summary);
    Task<DocumentSummary?> GetDocumentSummaryAsync(string documentId);
    Task<bool> PingAsync();
}
