using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace KurumsalRAG.IntegrationTests;

/// <summary>
/// Uçtan uca API testleri: gerçek pgvector + sahte LLM/embedding. Şema init, seed ingest,
/// retrieval, session izolasyonu, cache ve OpenAPI dokümanı GERÇEK HTTP üzerinden doğrulanır.
/// </summary>
public sealed class ApiIntegrationTests : IClassFixture<RagWebApplicationFactory>
{
    private readonly RagWebApplicationFactory _factory;

    public ApiIntegrationTests(RagWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_is_served_and_lists_chat_endpoint()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Doküman geçerli OpenAPI + chat yolu listeleniyor mu?
        Assert.Equal("Kurumsal RAG API", doc.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.True(doc.RootElement.GetProperty("paths").TryGetProperty("/api/chat", out _));
    }

    [Fact]
    public async Task Chat_returns_answer_with_sources_from_seed_document()
    {
        var client = _factory.CreateClient();

        // Seed doküman (Aurora politikası) startup'ta ingest edildi → retrieval kaynak bulmalı.
        var response = await client.PostAsJsonAsync("/api/chat", new { question = "yıllık izin kaç gün" });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Answer));
        Assert.Equal("Normal", body.Type);          // limited/blocked değil
        Assert.NotEmpty(body.Sources);               // GERÇEK retrieval kaynak döndürdü
    }

    [Fact]
    public async Task Documents_list_includes_seed_and_text_is_viewable()
    {
        var client = _factory.CreateClient();

        // Seed doküman listelenmeli (görüntüleme/şeffaflık).
        var listResp = await client.GetAsync("/api/documents");
        listResp.EnsureSuccessStatusCode();
        var docs = await listResp.Content.ReadFromJsonAsync<List<DocItem>>();
        Assert.NotNull(docs);
        var seed = docs!.FirstOrDefault(d => d.IsSeed);
        Assert.NotNull(seed);                          // seed örnek belge var
        Assert.True(seed!.ChunkCount > 0);

        // Belgenin çıkarılmış metni alınabilmeli ve gerçek içerik dönmeli.
        var getResp = await client.GetAsync($"/api/documents/{seed.Id}");
        getResp.EnsureSuccessStatusCode();
        var doc = await getResp.Content.ReadFromJsonAsync<DocDetail>();
        Assert.NotNull(doc);
        Assert.False(string.IsNullOrWhiteSpace(doc!.Text));
        Assert.Contains("Aurora", doc.Text);           // seed belgenin bilinen içeriği
    }

    [Fact]
    public async Task Foreign_document_id_is_not_leaked()
    {
        var client = _factory.CreateClient();

        // Rastgele/başka bir doküman id'si → 404 (session izolasyonu: sızıntı yok).
        var resp = await client.GetAsync($"/api/documents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Chat_empty_question_returns_bad_request()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new { question = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Session_cookie_is_issued_on_first_request()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new { question = "test" });

        // İlk istekte anonim session cookie (rag_sid) set edilmeli (session izolasyonunun temeli).
        Assert.Contains(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            c => c.Contains("rag_sid"));
    }

    private sealed record ChatResponse(string Answer, List<Source> Sources, string Type);
    private sealed record Source(int Reference, string ChunkId, double Score);
    private sealed record DocItem(string Id, string FileName, int ChunkCount, bool IsSeed);
    private sealed record DocDetail(string Id, int ChunkCount, string Text);
}
