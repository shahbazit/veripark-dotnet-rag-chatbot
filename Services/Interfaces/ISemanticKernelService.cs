using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;
using MyApplication.Models;

namespace MyApplication.Services.Interfaces;

#pragma warning disable CS0618

public interface ISemanticKernelService
{
    Kernel? GetKernel();
    IChatCompletionService? GetChatCompletionService();
    ITextEmbeddingGenerationService? GetEmbeddingService();
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<string> GenerateChatResponseAsync(
        string userMessage,
        string? systemPrompt = null,
        List<ChatMessageDto>? history = null,
        CancellationToken cancellationToken = default);
    Task<string> SummarizeTextAsync(string text, CancellationToken cancellationToken = default);
    bool IsConfigured();
}
