// File: Services/OpenAITextGenerationService.cs
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;

public sealed class OpenAITextGenerationService : ITextGenerationService
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OpenAITextGenerationService(HttpClient? http = null, string? model = null)
    {
        _http = http ?? new HttpClient();
        _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model; // pick your default
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("OPENAI_API_KEY not set.");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        _http.Timeout = TimeSpan.FromSeconds(45);
        _http.BaseAddress = new Uri("https://api.openai.com/"); // change if using Azure/OpenRouter/etc.
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var payload = new
        {
            model = _model,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0.2
        };
        var json = JsonSerializer.Serialize(payload);
        using var resp = await _http.PostAsync("v1/chat/completions",
            new StringContent(json, Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();
        using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);
        var content = doc.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString();
        return content ?? string.Empty;
    }
}
