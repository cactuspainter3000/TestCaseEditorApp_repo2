using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.Services
{
    public static class LlmFactory
    {
        /// <summary>
        /// Create a lazy ITextGenerationService that defers validation until first use.
        /// This avoids blocking application startup with LLM validation.
        /// </summary>
        public static ITextGenerationService CreateLazy(string? provider = null, IAnythingLLMService? anythingLlmService = null)
        {
            return new LazyTextGenerationService(() => Create(provider, anythingLlmService));
        }

        /// <summary>
        /// Create a concrete ITextGenerationService.
        /// provider: "ollama" | "openai" | "anythingllm" | "noop" (case-insensitive). Defaults to "ollama".
        /// anythingLlmService: Required when provider is "anythingllm"
        /// </summary>
        public static ITextGenerationService Create(string? provider = null, IAnythingLLMService? anythingLlmService = null)
        {
            provider ??= Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? "ollama";
            provider = provider.Trim().ToLowerInvariant();

            try
            {
                switch (provider)
                {
                    case "anythingllm":
                    case "anythinglm": // tolerate legacy/mistyped value
                        if (anythingLlmService == null)
                        {
                            throw new InvalidOperationException(
                                "AnythingLLM provider requested but service not provided. Please ensure AnythingLLM service is properly configured and running.");
                        }

                        return new AnythingLLMTextGenerationService(anythingLlmService);

                    case "openai":
                        var openaiHttp = new HttpClient();
                        return new global::OpenAITextGenerationService(openaiHttp, model: null);

                    case "noop":
                        return new NoopTextGenerationService();

                    case "ollama":
                    default:
                        var ollamaHttp = new HttpClient
                        {
                            BaseAddress = new Uri("http://localhost:11434/"),
                            Timeout = TimeSpan.FromSeconds(15)
                        };

                        var model = Environment.GetEnvironmentVariable("OLLAMA_MODEL")
                                    ?? "phi4-mini:3.8b-q4_K_M";

                        TestCaseEditorApp.Services.Logging.Log.Info(
                            $"[LlmFactory] Runtime model selection: provider='{provider}', env OLLAMA_MODEL='{Environment.GetEnvironmentVariable("OLLAMA_MODEL")}', selected='{model}'");

                        var skipValidation = Environment.GetEnvironmentVariable("SKIP_LLM_VALIDATION");
                        if (!string.IsNullOrWhiteSpace(skipValidation) &&
                            skipValidation.Equals("true", StringComparison.OrdinalIgnoreCase))
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info(
                                $"[LlmFactory] DEVELOPMENT MODE: Skipping model validation for '{model}' and returning OllamaTextGenerationService.");

                            return new global::OllamaTextGenerationService(model: model, http: ollamaHttp);
                        }

                        ValidateOllamaModelInstalled(ollamaHttp, model);

                        TestCaseEditorApp.Services.Logging.Log.Info(
                            $"[LlmFactory] Creating OllamaTextGenerationService with model '{model}'.");

                        return new global::OllamaTextGenerationService(model: model, http: ollamaHttp);
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Failed to create LLM service for provider '{provider}'. Please check your configuration.\n\nError: {ex.Message}",
                    ex);
            }
        }

        /// <summary>
        /// Validate that the requested Ollama model is installed without triggering a full generation call.
        /// </summary>
        private static void ValidateOllamaModelInstalled(HttpClient http, string model)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "api/tags");
                using var response = http.Send(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    throw new InvalidOperationException(
                        $"Cannot query Ollama models from http://localhost:11434/api/tags.\n\nError: {errorContent}");
                }

                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("models", out var modelsElement) ||
                    modelsElement.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException(
                        "Ollama returned an unexpected response when listing installed models.");
                }

                var installed = modelsElement.EnumerateArray()
                    .Select(x => x.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (!installed.Contains(model))
                {
                    var availableModels = installed.Count > 0
                        ? string.Join(", ", installed.OrderBy(x => x))
                        : "(none found)";

                    throw new InvalidOperationException(
                        $"Ollama model '{model}' is not installed.\n\n" +
                        $"Install it with: ollama pull {model}\n\n" +
                        $"Available models: {availableModels}");
                }
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    "Cannot connect to Ollama service at http://localhost:11434. " +
                    "Please ensure Ollama is running with: ollama serve\n\n" +
                    $"Error: {ex.Message}");
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to validate Ollama model '{model}'. Please check Ollama installation and model availability.\n\nError: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Lazy wrapper for ITextGenerationService that defers initialization until first use.
    /// Prevents LLM validation from blocking application startup.
    /// </summary>
    internal class LazyTextGenerationService : ITextGenerationService
    {
        private readonly Func<ITextGenerationService> _factory;
        private ITextGenerationService? _inner;
        private readonly object _lock = new();

        public LazyTextGenerationService(Func<ITextGenerationService> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>
        /// Get or create the inner text generation service.
        /// </summary>
        private ITextGenerationService GetService()
        {
            if (_inner == null)
            {
                lock (_lock)
                {
                    _inner ??= _factory();
                }
            }

            return _inner;
        }

        /// <summary>
        /// Generate text using the configured provider.
        /// </summary>
        public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
        {
            return await GetService().GenerateAsync(prompt, ct);
        }

        /// <summary>
        /// Generate text using a system message plus contextual content.
        /// </summary>
        public async Task<string> GenerateWithSystemAsync(string systemMessage, string contextMessage, CancellationToken ct = default)
        {
            return await GetService().GenerateWithSystemAsync(systemMessage, contextMessage, ct);
        }

        /// <summary>
        /// Legacy compatibility wrapper for text generation.
        /// </summary>
        [Obsolete("Use GenerateAsync instead")]
        public async Task<string> GenerateTextAsync(string prompt, int maxTokens = 1000)
        {
            return await GetService().GenerateAsync(prompt);
        }
    }
}