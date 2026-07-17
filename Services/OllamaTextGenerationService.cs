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
    private const string OllamaBaseUrl = "http://localhost:11434/";
    private static readonly System.TimeSpan DefaultGenerationTimeout = System.TimeSpan.FromMinutes(3);
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaTextGenerationService(string model = "llama3.1", HttpClient? http = null)
    {
        var ownsHttpClient = http == null;
        _http = http ?? new HttpClient { BaseAddress = new System.Uri(OllamaBaseUrl) };
        _model = model;
        if (ownsHttpClient)
        {
            _http.Timeout = DefaultGenerationTimeout;
        }
        else if (_http.Timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            _http.Timeout = DefaultGenerationTimeout;
        }
        
        // Log which model is configured
        TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaTextGen] Initialized with model: {_model}, timeout={_http.Timeout.TotalSeconds:F0}s");
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaTextGen] Starting generation request with model: {_model} (prompt length: {prompt.Length})");
        
        var payload = new
        {
            model = _model,
            stream = false,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var json = JsonSerializer.Serialize(payload);
        using var resp = await _http.PostAsync($"{OllamaBaseUrl}api/chat",
            new StringContent(json, Encoding.UTF8, "application/json"), ct);
        
        TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaTextGen] Received response: HTTP {(int)resp.StatusCode}");
        
        // Capture error details from Ollama before throwing
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync(ct);
            TestCaseEditorApp.Services.Logging.Log.Error($"[OllamaTextGen] HTTP {(int)resp.StatusCode} from Ollama: {errorBody}");
            resp.EnsureSuccessStatusCode(); // This will throw with proper context
        }

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

    public async Task<string> GenerateWithSystemAsync(string systemMessage, string contextMessage, CancellationToken ct = default)
    {
        var payload = new
        {
            model = _model,
            stream = false,
            messages = new[] 
            { 
                new { role = "system", content = systemMessage },
                new { role = "user", content = contextMessage } 
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var resp = await _http.PostAsync($"{OllamaBaseUrl}api/chat",
            new StringContent(json, Encoding.UTF8, "application/json"), ct);
        
        // Capture error details from Ollama before throwing
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync(ct);
            TestCaseEditorApp.Services.Logging.Log.Error($"[OllamaTextGen] HTTP {(int)resp.StatusCode} from Ollama GenerateWithSystemAsync: {errorBody}");
            resp.EnsureSuccessStatusCode(); // This will throw with proper context
        }

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

