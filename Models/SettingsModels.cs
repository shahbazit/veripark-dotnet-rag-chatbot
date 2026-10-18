namespace MyApplication.Models;

public class RagSettings
{
    public string Provider { get; set; } = "AzureOpenAI";
    public string AzureOpenAiEndpoint { get; set; } = string.Empty;
    public string AzureOpenAiApiKey { get; set; } = string.Empty;
    public string AzureOpenAiChatDeployment { get; set; } = "gpt-4o-mini";
    public string AzureOpenAiEmbeddingDeployment { get; set; } = "text-embedding-3-small";
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string OpenAiChatModel { get; set; } = "gpt-4o-mini";
    public string OpenAiEmbeddingModel { get; set; } = "text-embedding-3-small";
    public int ChunkSize { get; set; } = 500;
    public int ChunkOverlap { get; set; } = 100;
    public int DefaultTopK { get; set; } = 3;
    public float MinRelevanceScore { get; set; } = 0.05f;
}

public class QdrantSettings
{
    public string Endpoint { get; set; } = "http://localhost:6334";
    public int Port { get; set; } = 6334;
    public string ApiKey { get; set; } = string.Empty;
    public string CollectionName { get; set; } = "rag_chunks";
}
