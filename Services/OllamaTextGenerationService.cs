// Services/OllamaTextGenerationService.cs
using System;
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
    private const string OllamaBaseUrl = "http://localhost:11434/";
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(15);

    public OllamaTextGenerationService(string model = "llama3.1", HttpClient? http = null)
    {
        _model = model;
        _http = http ?? new HttpClient
        {
            BaseAddress = new Uri(OllamaBaseUrl)
        };

        // IMPORTANT:
        // Do not mutate Timeout or BaseAddress on an injected/shared HttpClient here.
        // If a shared client was already used, changing those properties throws InvalidOperationException.

        TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaTextGen] Initialized with model: {_model}");
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        TestCaseEditorApp.Services.Logging.Log.Info(
            $"[OllamaTextGen] Starting generation request with model: {_model} (prompt length: {prompt.Length})");

        var payload = new
        {
            model = _model,
            stream = false,
            messages = new[] { new { role = "user", content = prompt } }
        };

        return await SendChatRequestAsync(payload, ct);
    }

    public async Task<string> GenerateWithSystemAsync(string systemMessage, string contextMessage, CancellationToken ct = default)
    {
        TestCaseEditorApp.Services.Logging.Log.Info(
            $"[OllamaTextGen] Starting GenerateWithSystemAsync with model: {_model} " +
            $"(system length: {systemMessage?.Length ?? 0}, context length: {contextMessage?.Length ?? 0})");

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var token = linkedCts.Token;
            //var token = ct;

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

            TestCaseEditorApp.Services.Logging.Log.Info("[OllamaTextGen] Sending POST to api/chat");

            using var resp = await _http.PostAsync(
                "api/chat",
                new StringContent(json, Encoding.UTF8, "application/json"),
                token);

            TestCaseEditorApp.Services.Logging.Log.Info(
                $"[OllamaTextGen] GenerateWithSystemAsync received response: HTTP {(int)resp.StatusCode}");

            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync(token);
                TestCaseEditorApp.Services.Logging.Log.Error(
                    $"[OllamaTextGen] HTTP {(int)resp.StatusCode} from Ollama GenerateWithSystemAsync: {errorBody}");
                resp.EnsureSuccessStatusCode();
            }

            await using var s = await resp.Content.ReadAsStreamAsync(token);
            using var doc = await JsonDocument.ParseAsync(s, cancellationToken: token);

            if (doc.RootElement.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var content))
            {
                var result = content.GetString() ?? string.Empty;
                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[OllamaTextGen] GenerateWithSystemAsync parsed response length: {result.Length}");
                return result;
            }

            TestCaseEditorApp.Services.Logging.Log.Warn(
                "[OllamaTextGen] GenerateWithSystemAsync returned empty after parsing response JSON");

            return string.Empty;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            TestCaseEditorApp.Services.Logging.Log.Error(
                $"[OllamaTextGen] GenerateWithSystemAsync timed out for model '{_model}': {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Error(
                $"[OllamaTextGen] GenerateWithSystemAsync failed for model '{_model}': {ex}");
            throw;
        }
    }

    /// <summary>
    /// Sends a non-streaming chat request to Ollama using a per-call timeout rather than mutating HttpClient.Timeout.
    /// </summary>
    private async Task<string> SendChatRequestAsync(object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildChatUri())
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var timeoutCts = new CancellationTokenSource(DefaultRequestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var resp = await _http.SendAsync(request, linkedCts.Token);

        TestCaseEditorApp.Services.Logging.Log.Info(
            $"[OllamaTextGen] Received response: HTTP {(int)resp.StatusCode}");

        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync(linkedCts.Token);
            TestCaseEditorApp.Services.Logging.Log.Error(
                $"[OllamaTextGen] HTTP {(int)resp.StatusCode} from Ollama: {errorBody}");
            resp.EnsureSuccessStatusCode();
            TestCaseEditorApp.Services.Logging.Log.Info("[OllamaTextGen] Response headers received, reading body...");
        }

        await using var s = await resp.Content.ReadAsStreamAsync(linkedCts.Token);
        using var doc = await JsonDocument.ParseAsync(s, cancellationToken: linkedCts.Token);

        // Ollama chat shape: { "message": { "content": "..." }, ... }
        if (doc.RootElement.TryGetProperty("message", out var msg) &&
            msg.TryGetProperty("content", out var content))
        {
            return content.GetString() ?? string.Empty;
        }

        // Fallback: some variants return an array "messages": [ ... { "content": "..." } ... ]
        if (doc.RootElement.TryGetProperty("messages", out var arr) &&
            arr.ValueKind == JsonValueKind.Array)
        {
            JsonElement last = default;
            foreach (var e in arr.EnumerateArray())
                last = e;

            if (last.ValueKind != JsonValueKind.Undefined &&
                last.TryGetProperty("content", out var cEl))
            {
                return cEl.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Builds the correct chat URI whether the HttpClient has a BaseAddress or is a shared client without one.
    /// </summary>
    private Uri BuildChatUri()
    {
        if (_http.BaseAddress != null)
        {
            return new Uri(_http.BaseAddress, "api/chat");
        }

        return new Uri(new Uri(OllamaBaseUrl), "api/chat");
    }
}