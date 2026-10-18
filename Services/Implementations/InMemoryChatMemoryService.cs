using System.Collections.Concurrent;
using MyApplication.Models;
using MyApplication.Services.Interfaces;

namespace MyApplication.Services.Implementations;

public class InMemoryChatMemoryService : IChatMemoryService
{
    private readonly ConcurrentDictionary<string, List<ChatMessageDto>> _sessionHistories = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _userMemories = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DocumentSummary> _documentSummaries = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryChatMemoryService()
    {
    }

    public Task<long> AppendChatHistoryAsync(string sessionId, ChatMessageDto message)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return Task.FromResult(0L);

        var list = _sessionHistories.GetOrAdd(sessionId, _ => new List<ChatMessageDto>());
        lock (list)
        {
            list.Add(message);
            return Task.FromResult((long)list.Count);
        }
    }

    public Task<List<ChatMessageDto>> GetChatHistoryAsync(string sessionId, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !_sessionHistories.TryGetValue(sessionId, out var list))
        {
            return Task.FromResult(new List<ChatMessageDto>());
        }

        lock (list)
        {
            var result = list.TakeLast(limit).ToList();
            return Task.FromResult(result);
        }
    }

    public Task<bool> ClearChatHistoryAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return Task.FromResult(false);
        bool removed = _sessionHistories.TryRemove(sessionId, out _);
        return Task.FromResult(removed);
    }

    public Task<string?> GetUserMemoryAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Task.FromResult<string?>(null);
        _userMemories.TryGetValue(userId, out var memory);
        return Task.FromResult<string?>(memory);
    }

    public Task<bool> SetUserMemoryAsync(string userId, string memory)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Task.FromResult(false);
        _userMemories[userId] = memory;
        return Task.FromResult(true);
    }

    public Task<bool> ClearUserMemoryAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Task.FromResult(false);
        bool removed = _userMemories.TryRemove(userId, out _);
        return Task.FromResult(removed);
    }

    public Task<bool> SetDocumentSummaryAsync(string documentId, DocumentSummary summary)
    {
        if (string.IsNullOrWhiteSpace(documentId) || summary == null) return Task.FromResult(false);
        _documentSummaries[documentId] = summary;
        return Task.FromResult(true);
    }

    public Task<DocumentSummary?> GetDocumentSummaryAsync(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId) || !_documentSummaries.TryGetValue(documentId, out var summary))
        {
            return Task.FromResult<DocumentSummary?>(null);
        }
        return Task.FromResult<DocumentSummary?>(summary);
    }

    public Task<bool> PingAsync() => Task.FromResult(true);
}
