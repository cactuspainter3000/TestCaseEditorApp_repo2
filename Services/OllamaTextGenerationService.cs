// Services/OllamaTextGenerationService.cs
using System;
using System.Diagnostics;
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
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a text generation service for Ollama chat models.
    /// </summary>
    public OllamaTextGenerationService(string model = "llama3.1", HttpClient? http = null)
    {
        _ownsHttpClient = http == null;
        _http = http ?? new HttpClient { BaseAddress = new Uri("http://localhost:11434/") };
        _model = model;

        // Only set timeout when we created the client ourselves.
        if (_ownsHttpClient)
        {
            _http.Timeout = TimeSpan.FromMinutes(5);
        }

        TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaTextGen] Initialized with model: {_model}");
    }

    /// <summary>
    /// Generates a response using a single user prompt.
    /// </summary>
    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            TestCaseEditorApp.Services.Logging.Log.Warn("[OllamaTextGen] Skipping generation because prompt is empty.");
            return string.Empty;
        }

        TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaTextGen] Starting generation request with model: {_model} (prompt length: {prompt.Length})");

        var payload = new
        {
            model = _model,
            stream = false,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        return await SendChatRequestAsync(payload, ct);
    }

    /// <summary>
    /// Generates a response using a system message and a user/context message.
    /// </summary>
    public async Task<string> GenerateWithSystemAsync(string systemMessage, string contextMessage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(systemMessage) && string.IsNullOrWhiteSpace(contextMessage))
        {
            TestCaseEditorApp.Services.Logging.Log.Warn("[OllamaTextGen] Skipping generation because both system and context messages are empty.");
            return string.Empty;
        }

        TestCaseEditorApp.Services.Logging.Log.Info(
            $"[OllamaTextGen] Starting system generation request with model: {_model} " +
            $"(system length: {systemMessage?.Length ?? 0}, context length: {contextMessage?.Length ?? 0})");

        var payload = new
        {
            model = _model,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = systemMessage ?? string.Empty },
                new { role = "user", content = contextMessage ?? string.Empty }
            }
        };

        return await SendChatRequestAsync(payload, ct);
    }

    /// <summary>
    /// Sends a chat request to Ollama and extracts the returned text content.
    /// </summary>
    private async Task<string> SendChatRequestAsync(object payload, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var json = JsonSerializer.Serialize(payload);

        try
        {
            using var resp = await _http.PostAsync(
                "api/chat",
                new StringContent(json, Encoding.UTF8, "application/json"),
                ct);

            stopwatch.Stop();
            TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaTextGen] Received response from model {_model}: HTTP {(int)resp.StatusCode} after {stopwatch.ElapsedMilliseconds} ms");

            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync(ct);
                TestCaseEditorApp.Services.Logging.Log.Error($"[OllamaTextGen] HTTP {(int)resp.StatusCode} from Ollama model {_model}: {errorBody}");
                resp.EnsureSuccessStatusCode();
            }

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("message", out var messageElement) &&
                messageElement.TryGetProperty("content", out var contentElement))
            {
                return contentElement.GetString() ?? string.Empty;
            }

            if (doc.RootElement.TryGetProperty("messages", out var messagesElement) &&
                messagesElement.ValueKind == JsonValueKind.Array)
            {
                JsonElement last = default;
                foreach (var item in messagesElement.EnumerateArray())
                {
                    last = item;
                }

                if (last.ValueKind != JsonValueKind.Undefined &&
                    last.TryGetProperty("content", out var fallbackContentElement))
                {
                    return fallbackContentElement.GetString() ?? string.Empty;
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Warn($"[OllamaTextGen] No content field found in Ollama response for model {_model}.");
            return string.Empty;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            TestCaseEditorApp.Services.Logging.Log.Error(
                $"[OllamaTextGen] Request timed out for model {_model} after {stopwatch.ElapsedMilliseconds} ms. Error: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            TestCaseEditorApp.Services.Logging.Log.Error(
                $"[OllamaTextGen] Request failed for model {_model} after {stopwatch.ElapsedMilliseconds} ms. Error: {ex.Message}");
            throw;
        }
    }
}