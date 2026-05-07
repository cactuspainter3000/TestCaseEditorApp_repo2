using System;
using System.Net.Http;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Minimal factory to construct a concrete ITextGenerationService.
    /// Defaults to Ollama when no provider is supplied or the environment variable is not set.
    /// </summary>
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
                        if (anythingLlmService == null)
                        {
                            throw new InvalidOperationException("AnythingLLM provider requested but service not provided. Please ensure AnythingLLM service is properly configured and running.");
                        }
                        return new AnythingLLMTextGenerationService(anythingLlmService);

                    case "openai":
                        var openaiHttp = new HttpClient();
                        return new global::OpenAITextGenerationService(openaiHttp, model: null);

                    case "noop":
                        return new NoopTextGenerationService();

                    case "ollama":
                    default:
                        var ollamaClient = new HttpClient { BaseAddress = new Uri("http://localhost:11434/") };
                        var model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "phi3.5:3.8b-mini-instruct-q4_K_M";
                        
                        // DEVELOPMENT MODE: Check for dev override to skip validation
                        var skipValidation = Environment.GetEnvironmentVariable("SKIP_LLM_VALIDATION");
                        if (!string.IsNullOrEmpty(skipValidation) && skipValidation.ToLowerInvariant() == "true")
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[LlmFactory] DEVELOPMENT MODE: Skipping model validation for '{model}' - returning NoopTextGenerationService");
                            return new NoopTextGenerationService();
                        }
                        
                        // Validate model availability before proceeding - fail fast if not available
                        try
                        {
                            var testRequest = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
                            {
                                Content = new StringContent($$"""{"model": "{{model}}", "prompt": "test", "stream": false}""", 
                                    System.Text.Encoding.UTF8, "application/json")
                            };
                            var testResponse = ollamaClient.Send(testRequest);
                            if (!testResponse.IsSuccessStatusCode)
                            {
                                var errorContent = testResponse.Content.ReadAsStringAsync().Result;
                                throw new InvalidOperationException($"Ollama model '{model}' is not available. Please install the model using: ollama pull {model}\n\nError: {errorContent}\n\nTo temporarily bypass this for development, set environment variable: SKIP_LLM_VALIDATION=true");
                            }
                        }
                        catch (HttpRequestException ex)
                        {
                            throw new InvalidOperationException($"Cannot connect to Ollama service at http://localhost:11434. Please ensure Ollama is running with: ollama serve\n\nError: {ex.Message}\n\nTo temporarily bypass this for development, set environment variable: SKIP_LLM_VALIDATION=true");
                        }
                        catch (InvalidOperationException)
                        {
                            throw; // Re-throw our custom exceptions
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException($"Failed to validate Ollama model '{model}'. Please check Ollama installation and model availability.\n\nError: {ex.Message}\n\nTo temporarily bypass this for development, set environment variable: SKIP_LLM_VALIDATION=true");
                        }
                        
                        return new global::OllamaTextGenerationService(model: model, http: ollamaClient);
                }
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                // Only catch unexpected exceptions, not our validation failures
                throw new InvalidOperationException($"Failed to create LLM service for provider '{provider}'. Please check your configuration.\n\nError: {ex.Message}", ex);
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

        public async System.Threading.Tasks.Task<string> GenerateAsync(string prompt, System.Threading.CancellationToken ct = default)
        {
            return await GetService().GenerateAsync(prompt, ct);
        }

        public async System.Threading.Tasks.Task<string> GenerateWithSystemAsync(string systemMessage, string contextMessage, System.Threading.CancellationToken ct = default)
        {
            return await GetService().GenerateWithSystemAsync(systemMessage, contextMessage, ct);
        }

        [Obsolete("Use GenerateAsync instead")]
        public async System.Threading.Tasks.Task<string> GenerateTextAsync(string prompt, int maxTokens = 1000)
        {
            return await GetService().GenerateAsync(prompt);
        }
    }
}