# PDF RAG Chatbot (.NET 10)

A simple RAG chatbot built with ASP.NET Core Minimal API, Microsoft Semantic Kernel, and Qdrant. It indexes the assessment PDF and answers questions with direct citations.

## Architecture
- **API**: ASP.NET Core Minimal API (`/api/chat`, `/api/upload`, `/api/status`)
- **Orchestration & LLM**: Semantic Kernel (`gpt-4o-mini`, `text-embedding-3-small`)
- **Vector DB**: Qdrant (1536-dim vectors, cosine distance)
- **PDF Extraction & Chunking**: PdfPig, 500-char chunks with 100-char overlap
- **Memory**: In-memory thread-safe chat history and user profile

---

## Chunking Strategy
- **Small Pieces:** Breaks down the PDF into **500-character** chunks.
- **Overlap:** Adds a **100-character** overlap between chunks so thoughts and sentences aren't cut in half.
- **Why?** Keeps answers focused, fast, and accurate while staying within model limits.

---

## Setup & Configuration (`appsettings.json`)

### 1. Vector DB Setup (Qdrant)
Run local Qdrant with Docker (no account or API key needed):
```bash
docker run -d -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

In `appsettings.json`, set:
```json
"QdrantSettings": {
  "Endpoint": "http://localhost:6334",
  "Port": 6334,
  "ApiKey": "",
  "CollectionName": "rag_chunks"
}
```

> **Note:** If using Qdrant Cloud instead of local Docker, set `Endpoint` to your cluster URL and provide your `ApiKey`.

### 2. LLM Setup (OpenAI or Azure OpenAI)
Configure your OpenAI or Azure OpenAI keys in `appsettings.json`:
```json
"RagSettings": {
  "Provider": "OpenAI", // or "AzureOpenAI"
  "OpenAiApiKey": "YOUR_KEY",
  "AzureOpenAiEndpoint": "https://<resource>.openai.azure.com/",
  "AzureOpenAiApiKey": "YOUR_AZURE_KEY"
}
```

---

## How to Run (Execution)

1. Run the app:
   ```powershell
   dotnet run
   ```
2. Open `http://localhost:5281` in your browser.
3. On startup, the app automatically indexes `Microsoft-Copilot-Studio-Licensing-Guide.pdf` into Qdrant. You can also upload your own PDF and start asking questions.
