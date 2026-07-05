using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services.DependencyInjection
{
    /// <summary>
    /// Test Case Creation and Generation services: LLM-powered test case generation, deduplication.
    /// These services are SINGLETON for state management and cache efficiency.
    /// </summary>
    public static class TestCaseCreationExtensions
    {
        public static IServiceCollection AddTestCaseCreationServices(this IServiceCollection services)
        {
            // Test Case Generation Service (SINGLETON - coordinates with LLM)
            services.AddSingleton<ITestCaseGenerationService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<TestCaseGenerationService>>();
                var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();
                var ragContextService = provider.GetService<RAGContextService>(); // Optional
                var ragFeedbackService = provider.GetService<RAGFeedbackIntegrationService>(); // Optional
                return new TestCaseGenerationService(
                    logger, anythingLLMService, ragContextService, ragFeedbackService);
            });

            // Test Case Deduplication Service (SINGLETON - shared dedup rules)
            services.AddSingleton<ITestCaseDeduplicationService, TestCaseDeduplicationService>();

            return services;
        }
    }
}
