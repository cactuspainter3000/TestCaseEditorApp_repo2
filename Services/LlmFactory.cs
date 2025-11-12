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
        /// Create a concrete ITextGenerationService.
        /// provider: "ollama" | "openai" | "noop" (case-insensitive). Defaults to "ollama".
        /// </summary>
        public static ITextGenerationService Create(string? provider = null)
        {
            provider ??= Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? "ollama";
            provider = provider.Trim().ToLowerInvariant();

            try
            {
                switch (provider)
                {
                    case "openai":
                        var openaiHttp = new HttpClient();
                        return new global::OpenAITextGenerationService(openaiHttp, model: null);

                    case "noop":
                        return new NoopTextGenerationService();

                    case "ollama":
                    default:
                        var ollamaClient = new HttpClient { BaseAddress = new Uri("http://localhost:11434/") };
                        var model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "phi4-mini:3.8b-q4_K_M";
                        return new global::OllamaTextGenerationService(model: model, http: ollamaClient);
                }
            }
            catch
            {
                // Fallback to the local Noop implementation to avoid crashing composition.
                return new NoopTextGenerationService();
            }
        }
    }
}