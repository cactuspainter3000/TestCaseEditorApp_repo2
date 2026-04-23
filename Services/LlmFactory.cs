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
        /// Create a lazy ITextGenerationService that defers initialization until first use.
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

            if (provider == "anythinglm")
            {
                provider = "anythingllm";
            }

            TestCaseEditorApp.Services.Logging.Log.Info(
                $"[LlmFactory] Create called with provider='{provider}'");

            try
            {
                switch (provider)
                {
                    case "anythingllm":
                        if (anythingLlmService == null)
                        {
                            throw new InvalidOperationException(
                                "AnythingLLM provider requested but service not provided. Please ensure AnythingLLM service is properly configured and running.");
                        }

                        TestCaseEditorApp.Services.Logging.Log.Info(
                            "[LlmFactory] Creating AnythingLLMTextGenerationService");

                        return new AnythingLLMTextGenerationService(anythingLlmService);

                    case "openai":
                        TestCaseEditorApp.Services.Logging.Log.Info(
                            "[LlmFactory] Creating OpenAITextGenerationService");

                        var openaiHttp = new HttpClient();
                        return new global::OpenAITextGenerationService(openaiHttp, model: null);

                    case "noop":
                        TestCaseEditorApp.Services.Logging.Log.Info(
                            "[LlmFactory] Creating NoopTextGenerationService");

                        return new NoopTextGenerationService();

                    case "ollama":
                    default:
                        var ollamaClient = new HttpClient
                        {
                            BaseAddress = new Uri("http://localhost:11434/")
                        };

                        var envModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
                        TestCaseEditorApp.Services.Logging.Log.Info(
                            $"[LlmFactory] OLLAMA_MODEL env var = '{envModel ?? "(null)"}'");

                        var model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "phi4-mini:3.8b-q4_K_M";

                        TestCaseEditorApp.Services.Logging.Log.Info(
                            $"[LlmFactory] Creating OllamaTextGenerationService with model '{model}'");

                        // DEVELOPMENT MODE: Check for dev override to skip validation
                        var skipValidation = Environment.GetEnvironmentVariable("SKIP_LLM_VALIDATION");
                        if (!string.IsNullOrEmpty(skipValidation) && skipValidation.ToLowerInvariant() == "true")
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info(
                                $"[LlmFactory] DEVELOPMENT MODE: Skipping model validation for '{model}' and returning OllamaTextGenerationService directly");
                        }

                        // IMPORTANT:
                        // Do not do a blocking validation request here.
                        // Let the actual Generate call be the first real request so fallback starts fast and logs clearly.

                        return new global::OllamaTextGenerationService(model: model, http: ollamaClient);
                }
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                throw new InvalidOperationException(
                    $"Failed to create LLM service for provider '{provider}'. Please check your configuration.\n\nError: {ex.Message}", ex);
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
            TestCaseEditorApp.Services.Logging.Log.Info("[LazyTextGen] GetService called");

            if (_inner == null)
            {
                lock (_lock)
                {
                    if (_inner == null)
                    {
                        _inner = _factory();
                        TestCaseEditorApp.Services.Logging.Log.Info(
                            $"[LazyTextGen] Resolved service type: {_inner.GetType().Name}");
                    }
                }
            }

            return _inner;
        }

        public async System.Threading.Tasks.Task<string> GenerateAsync(string prompt, System.Threading.CancellationToken ct = default)
        {
            TestCaseEditorApp.Services.Logging.Log.Info(
                $"[LazyTextGen] GenerateAsync called. Prompt length={prompt?.Length ?? 0}");

            return await GetService().GenerateAsync(prompt, ct);
        }

        public async System.Threading.Tasks.Task<string> GenerateWithSystemAsync(string systemMessage, string contextMessage, System.Threading.CancellationToken ct = default)
        {
            TestCaseEditorApp.Services.Logging.Log.Info(
                $"[LazyTextGen] GenerateWithSystemAsync called. System length={systemMessage?.Length ?? 0}, Context length={contextMessage?.Length ?? 0}");

            return await GetService().GenerateWithSystemAsync(systemMessage, contextMessage, ct);
        }

        [Obsolete("Use GenerateAsync instead")]
        public async System.Threading.Tasks.Task<string> GenerateTextAsync(string prompt, int maxTokens = 1000)
        {
            return await GetService().GenerateAsync(prompt);
        }
    }
}