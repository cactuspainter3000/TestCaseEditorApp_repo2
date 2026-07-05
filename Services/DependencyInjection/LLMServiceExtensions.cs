using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.Services.Parsing;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services.DependencyInjection
{
    /// <summary>
    /// LLM (Large Language Model) integration services: Ollama, AnythingLLM, RAG (Retrieval-Augmented Generation).
    /// These are critical for AI-powered features and require careful lifecycle management.
    /// All are SINGLETON because they manage expensive resources (process management, service connections).
    /// </summary>
    public static class LLMServiceExtensions
    {
        public static IServiceCollection AddLLMServices(this IServiceCollection services)
        {
            // Ollama process management (SINGLETON - controls system process)
            services.AddSingleton<IOllamaProcessManager, OllamaProcessManager>();
            services.AddSingleton<IOllamaStatusMonitor, OllamaStatusMonitor>();

            // Ollama embedding service (SINGLETON - resource-intensive, stateful)
            services.AddSingleton<IOllamaEmbeddingService, OllamaEmbeddingService>(provider =>
            {
                var userSettingsService = provider.GetRequiredService<IUserSettingsService>();
                var settings = userSettingsService.LoadSettings();
                var embeddingModel = string.IsNullOrWhiteSpace(settings.OllamaEmbeddingModel)
                    ? "nomic-embed-text:latest"
                    : settings.OllamaEmbeddingModel.Trim();
                return new OllamaEmbeddingService(embeddingModel);
            });

            // Core LLM factory (SINGLETON - lazy-loaded text generation)
            // Lazily creates the text generation service to avoid startup validation
            services.AddSingleton<ITextGenerationService>(provider =>
            {
                var anythingLlmService = provider.GetService<IAnythingLLMService>();
                return LlmFactory.CreateLazy(anythingLlmService: anythingLlmService);
            });

            // LLM health monitoring (SINGLETON - periodic health checks)
            services.AddSingleton<LlmServiceHealthMonitor>(provider =>
            {
                var anythingLlmService = provider.GetService<IAnythingLLMService>();
                var primaryLlmService = LlmFactory.CreateLazy(anythingLlmService: anythingLlmService);
                var logger = provider.GetRequiredService<ILogger<LlmServiceHealthMonitor>>();
                return new LlmServiceHealthMonitor(
                    primaryLlmService,
                    logger,
                    TimeSpan.FromMinutes(2)); // Less frequent health checks to avoid premature fallback
            });

            // LLM analysis caching (SINGLETON - shared cache with bounded size)
            services.AddSingleton<RequirementAnalysisCache>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<RequirementAnalysisCache>>();
                return new RequirementAnalysisCache(
                    logger,
                    maxCacheSize: 500,          // Cache up to 500 analysis results
                    maxAge: TimeSpan.FromHours(8), // Cache expires after 8 hours
                    cleanupInterval: TimeSpan.FromMinutes(30)); // Cleanup every 30 minutes
            });

            // AnythingLLM service (SINGLETON - workspace/chat operations)
            services.AddSingleton<AnythingLLMService>(provider =>
            {
                try
                {
                    return new AnythingLLMService(); // Let it get baseUrl and apiKey from defaults/user config
                }
                catch (Exception ex)
                {
                    var logger = provider.GetService<ILogger<AnythingLLMService>>();
                    logger?.LogWarning("AnythingLLM not configured: {Error}", ex.Message);
                    return new AnythingLLMService(); // Will report "not configured" in IsConfigured
                }
            });

            // Register interface for testable architecture
            services.AddSingleton<IAnythingLLMService>(provider => provider.GetRequiredService<AnythingLLMService>());

            // Test case LLM wrapper (SINGLETON - shared across app)
            services.AddSingleton<TestCaseAnythingLLMService>();

            // RAG (Retrieval-Augmented Generation) services (SINGLETON - document indexing state)
            services.AddSingleton<IDirectRagService, DirectRagService>();

            services.AddSingleton<RAGContextService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<RAGContextService>>();
                var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();
                return new RAGContextService(logger, anythingLLMService);
            });

            services.AddSingleton<RAGFeedbackService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<RAGFeedbackService>>();
                var ragContextService = provider.GetRequiredService<RAGContextService>();
                return new RAGFeedbackService(logger, ragContextService);
            });

            services.AddSingleton<RAGParameterOptimizer>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<RAGParameterOptimizer>>();
                var feedbackService = provider.GetRequiredService<RAGFeedbackService>();
                var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();
                return new RAGParameterOptimizer(logger, feedbackService, anythingLLMService);
            });

            services.AddSingleton<RAGFeedbackIntegrationService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<RAGFeedbackIntegrationService>>();
                var feedbackService = provider.GetRequiredService<RAGFeedbackService>();
                var parameterOptimizer = provider.GetRequiredService<RAGParameterOptimizer>();
                var ragContextService = provider.GetRequiredService<RAGContextService>();
                return new RAGFeedbackIntegrationService(logger, feedbackService, parameterOptimizer, ragContextService);
            });

            // LLM learning and text processing (SINGLETON - model training state)
            services.AddSingleton<ITextSimilarityService, TextSimilarityService>();
            services.AddSingleton<ILLMLearningService, LLMLearningService>();
            services.AddSingleton<IEditDetectionService, EditDetectionService>();

            return services;
        }
    }
}
