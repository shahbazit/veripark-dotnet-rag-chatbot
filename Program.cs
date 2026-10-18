using Microsoft.AspNetCore.Mvc;
using MyApplication.Models;
using MyApplication.Services.Implementations;
using MyApplication.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RagSettings>(builder.Configuration.GetSection("RagSettings"));
builder.Services.Configure<QdrantSettings>(builder.Configuration.GetSection("QdrantSettings"));

builder.Services.AddSingleton<IChatMemoryService, InMemoryChatMemoryService>();
builder.Services.AddSingleton<IQdrantVectorService, QdrantVectorService>();
builder.Services.AddSingleton<ISemanticKernelService, SemanticKernelService>();
builder.Services.AddSingleton<IPdfRagService, PdfRagService>();

builder.Services.AddAntiforgery();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseAntiforgery();

app.MapPost("/api/chat", async ([FromBody] ChatQueryRequest request, IPdfRagService ragService) =>
{
    if (string.IsNullOrWhiteSpace(request?.Question))
    {
        return Results.BadRequest(new { error = "Question cannot be empty." });
    }

    try
    {
        var response = await ragService.AskQuestionAsync(request);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
})
.WithName("AskChatbot");

app.MapPost("/api/upload", async (IFormFile file, IPdfRagService ragService) =>
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "No PDF file provided." });
    }
    if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Only .pdf files are supported." });
    }

    try
    {
        using var stream = file.OpenReadStream();
        var summary = await ragService.IngestPdfAsync(stream, file.FileName);
        return Results.Ok(new
        {
            success = true,
            fileName = summary.FileName,
            pageCount = summary.PageCount,
            chunkCount = summary.ChunkCount,
            fileSizeBytes = summary.FileSizeBytes,
            uploadedAt = summary.UploadedAt
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("UploadDocument")
.DisableAntiforgery();

app.MapGet("/api/history/{sessionId}", async (string sessionId, IChatMemoryService memory, int limit = 30) =>
{
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        return Results.BadRequest(new { error = "SessionId is required." });
    }
    var history = await memory.GetChatHistoryAsync(sessionId, limit);
    return Results.Ok(history);
})
.WithName("GetChatHistory");

app.MapDelete("/api/history/{sessionId}", async (string sessionId, IChatMemoryService memory) =>
{
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        return Results.BadRequest(new { error = "SessionId is required." });
    }
    bool cleared = await memory.ClearChatHistoryAsync(sessionId);
    return Results.Ok(new { success = cleared, sessionId });
})
.WithName("ClearChatHistory");

app.MapPost("/api/history/clear", async (string? sessionId, IChatMemoryService memory) =>
{
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        return Results.BadRequest(new { error = "SessionId is required." });
    }
    bool cleared = await memory.ClearChatHistoryAsync(sessionId);
    return Results.Ok(new { success = cleared, sessionId });
})
.WithName("ClearChatHistoryPost");

app.MapGet("/api/memory", async (string? userId, IChatMemoryService memory) =>
{
    var uid = string.IsNullOrWhiteSpace(userId) ? "default-user" : userId;
    var mem = await memory.GetUserMemoryAsync(uid);
    return Results.Ok(new { userId = uid, memory = mem ?? string.Empty });
})
.WithName("GetUserMemory");

app.MapPost("/api/memory", async ([FromBody] UserMemoryRequest request, IChatMemoryService memory) =>
{
    if (string.IsNullOrWhiteSpace(request?.UserId))
    {
        return Results.BadRequest(new { error = "UserId is required." });
    }
    await memory.SetUserMemoryAsync(request.UserId, request.Memory ?? string.Empty);
    return Results.Ok(new { success = true, userId = request.UserId, memory = request.Memory });
})
.WithName("UpdateUserMemory");

app.MapPost("/api/memory/clear", async (string? userId, IChatMemoryService memory) =>
{
    var uid = string.IsNullOrWhiteSpace(userId) ? "default-user" : userId;
    await memory.ClearUserMemoryAsync(uid);
    return Results.Ok(new { success = true, userId = uid });
})
.WithName("ClearUserMemory");

app.MapPost("/api/init", async (IPdfRagService ragService) =>
{
    bool ingested = await ragService.EnsureDefaultDocumentIngestedAsync();
    return Results.Ok(new { success = ingested, message = ingested ? "Assessment PDF verified/indexed." : "Ingestion failed or PDF not found." });
})
.WithName("InitAssessmentGuide");

app.MapGet("/api/status", async (IQdrantVectorService vectorStore, IChatMemoryService memory, ISemanticKernelService sk) =>
{
    var qdrantReady = await vectorStore.CollectionExistsAsync();
    var pointCount = qdrantReady ? await vectorStore.GetPointsCountAsync() : 0;
    return Results.Ok(new
    {
        status = "Healthy",
        qdrant = new { connected = qdrantReady, totalVectors = pointCount },
        semanticKernel = new { configured = sk.IsConfigured() },
        memory = "InMemory (Thread-Safe ConcurrentDictionary)",
        timestamp = DateTime.UtcNow
    });
})
.WithName("HealthStatus");

app.MapFallbackToFile("index.html");

_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(2000);
        using var scope = app.Services.CreateScope();
        var ragService = scope.ServiceProvider.GetRequiredService<IPdfRagService>();
        await ragService.EnsureDefaultDocumentIngestedAsync();
    }
    catch { }
});

app.Run();
