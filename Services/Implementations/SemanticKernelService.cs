using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using MyApplication.Models;
using MyApplication.Services.Interfaces;

namespace MyApplication.Services.Implementations;

#pragma warning disable CS0618

public class SemanticKernelService : ISemanticKernelService
{
    private readonly RagSettings _settings;
    private Kernel? _kernel;
    private IChatCompletionService? _chatCompletionService;
    private ITextEmbeddingGenerationService? _embeddingService;

    public SemanticKernelService(IOptions<RagSettings> settings)
    {
        _settings = settings.Value;

        InitializeKernel();
    }

    private void InitializeKernel()
    {
        try
        {
            if (string.Equals(_settings.Provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(_settings.AzureOpenAiEndpoint) &&
                !string.IsNullOrWhiteSpace(_settings.AzureOpenAiApiKey))
            {
                var builder = Kernel.CreateBuilder();

                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: _settings.AzureOpenAiChatDeployment,
                    endpoint: _settings.AzureOpenAiEndpoint,
                    apiKey: _settings.AzureOpenAiApiKey);

                builder.AddAzureOpenAITextEmbeddingGeneration(
                    deploymentName: _settings.AzureOpenAiEmbeddingDeployment,
                    endpoint: _settings.AzureOpenAiEndpoint,
                    apiKey: _settings.AzureOpenAiApiKey);

                _kernel = builder.Build();
                _chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>();
                _embeddingService = _kernel.GetRequiredService<ITextEmbeddingGenerationService>();
                return;
            }

            if (!string.IsNullOrWhiteSpace(_settings.OpenAiApiKey))
            {
                var builder = Kernel.CreateBuilder();

                builder.AddOpenAIChatCompletion(
                    modelId: _settings.OpenAiChatModel,
                    apiKey: _settings.OpenAiApiKey);

                builder.AddOpenAITextEmbeddingGeneration(
                    modelId: _settings.OpenAiEmbeddingModel,
                    apiKey: _settings.OpenAiApiKey);

                _kernel = builder.Build();
                _chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>();
                _embeddingService = _kernel.GetRequiredService<ITextEmbeddingGenerationService>();
            }
            else
            {
                _kernel = null;
                _chatCompletionService = null;
                _embeddingService = null;
            }
        }
        catch
        {
            _kernel = null;
            _chatCompletionService = null;
            _embeddingService = null;
        }
    }

    public Kernel? GetKernel() => _kernel;

    public IChatCompletionService? GetChatCompletionService() => _chatCompletionService;

    public ITextEmbeddingGenerationService? GetEmbeddingService() => _embeddingService;

    public bool IsConfigured() => _chatCompletionService != null && _embeddingService != null;

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_embeddingService == null)
        {
            throw new InvalidOperationException("Embedding service is not initialized. Check your OpenAI API key in appsettings.json.");
        }

        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(new[] { text }, cancellationToken: cancellationToken);
        if (embeddings != null && embeddings.Count > 0)
        {
            return embeddings[0].ToArray();
        }
        throw new InvalidOperationException("Failed to generate embedding vector from OpenAI.");
    }

    public async Task<string> GenerateChatResponseAsync(
        string userMessage,
        string? systemPrompt = null,
        List<ChatMessageDto>? history = null,
        CancellationToken cancellationToken = default)
    {
        if (_chatCompletionService == null)
        {
            throw new InvalidOperationException("Chat completion service is not initialized. Check your OpenAI API key in appsettings.json.");
        }

        var chatHistory = new ChatHistory();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            chatHistory.AddSystemMessage(systemPrompt);
        }

        if (history != null)
        {
            foreach (var msg in history)
            {
                if (string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    chatHistory.AddAssistantMessage(msg.Content);
                }
                else if (string.Equals(msg.Role, "system", StringComparison.OrdinalIgnoreCase))
                {
                    chatHistory.AddSystemMessage(msg.Content);
                }
                else
                {
                    chatHistory.AddUserMessage(msg.Content);
                }
            }
        }

        chatHistory.AddUserMessage(userMessage);

        var promptSettings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0.3,
            MaxTokens = 1500
        };

        var response = await _chatCompletionService.GetChatMessageContentAsync(
            chatHistory: chatHistory,
            executionSettings: promptSettings,
            kernel: _kernel,
            cancellationToken: cancellationToken);

        return response.Content ?? string.Empty;
    }

    public async Task<string> SummarizeTextAsync(string text, CancellationToken cancellationToken = default)
    {
        string systemPrompt = "You are an expert AI summarizer. Provide a concise, clear summary of the following content:";
        return await GenerateChatResponseAsync(text, systemPrompt, cancellationToken: cancellationToken);
    }
}
#pragma warning restore CS0618
