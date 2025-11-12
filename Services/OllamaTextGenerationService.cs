// Services/OllamaTextGenerationService.cs
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;

public sealed class OllamaTextGenerationService : ITextGenerationService
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaTextGenerationService(string model = "llama3.1", HttpClient? http = null)
    {
        _http = http ?? new HttpClient { BaseAddress = new System.Uri("http://localhost:11434/") };
        _model = model;
        _http.Timeout = System.TimeSpan.FromMinutes(5);
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var payload = new
        {
            model = _model,
            stream = false,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var json = JsonSerializer.Serialize(payload);
        using var resp = await _http.PostAsync("api/chat",
            new StringContent(json, Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();

        using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);

        // Ollama chat shape: { "message": { "content": "..." }, ... }
        if (doc.RootElement.TryGetProperty("message", out var msg) &&
            msg.TryGetProperty("content", out var content))
        {
            return content.GetString() ?? string.Empty;
        }

        // Fallback: some variants return an array "messages": [ ... { "content": "..." } ... ]
        if (doc.RootElement.TryGetProperty("messages", out var arr) &&
            arr.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            // Get the last element without using index-from-end
            System.Text.Json.JsonElement last = default;
            foreach (var e in arr.EnumerateArray())
                last = e;

            if (last.ValueKind != System.Text.Json.JsonValueKind.Undefined &&
                last.TryGetProperty("content", out var cEl))
            {
                return cEl.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}

