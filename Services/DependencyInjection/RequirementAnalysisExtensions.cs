using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using TestCaseEditorApp.Services.Parsing;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.Services.Templates;
using TestCaseEditorApp.Prompts;
using System;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services.DependencyInjection
{
    /// <summary>
    /// Requirements domain analysis services: requirement parsing, analysis, Jama integration.
    /// These services coordinate LLM analysis with requirement extraction and validation.
    /// Primarily SINGLETON for state management and cache efficiency.
    /// </summary>
    public static class RequirementAnalysisExtensions
    {
        public static IServiceCollection AddRequirementAnalysisServices(this IServiceCollection services)
        {
            // Base requirement service - core requirement model operations (SINGLETON)
            services.AddSingleton<RequirementService>();
            
            // Requirement service with notification wrapper (SINGLETON)
            services.AddSingleton<IRequirementService, NotifyingRequirementService>();
            
            // Smart requirement importer with fallback logic (SINGLETON)
            services.AddSingleton<SmartRequirementImporter>();

            // Requirement Edit Session Service - manages edit workspace and persistence (SINGLETON)
            services.AddSingleton<RequirementEditSessionService>();

            // Prompt building and response parsing (SINGLETON - template definitions)
            services.AddSingleton<RequirementAnalysisPromptBuilder>();
            services.AddSingleton<ResponseParserManager>();

            // Capability Derivation Prompt Builder (SINGLETON - ATP prompts with A-N taxonomy)
            services.AddSingleton<CapabilityDerivationPromptBuilder>();
            services.AddSingleton<ICapabilityDerivationPromptBuilder>(provider =>
                provider.GetRequiredService<CapabilityDerivationPromptBuilder>());

            // Requirement Analysis Service - LLM-powered analysis (SINGLETON - orchestrator)
            services.AddSingleton<IRequirementAnalysisService, RequirementAnalysisService>(provider =>
            {
                var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();
                var primaryLlmService = LlmFactory.CreateLazy(anythingLlmService: anythingLLMService);
                var directRagService = provider.GetService<IDirectRagService>(); // Optional RAG fallback
                var promptBuilder = provider.GetRequiredService<RequirementAnalysisPromptBuilder>();
                var parserManager = provider.GetRequiredService<ResponseParserManager>();
                var cache = provider.GetService<RequirementAnalysisCache>(); // Optional
                var complianceWrapper = provider.GetService<IServiceComplianceWrapper>();

                // Optional derivation analysis services
                var derivationService = provider.GetService<ISystemCapabilityDerivationService>();
                var gapAnalyzer = provider.GetService<IRequirementGapAnalyzer>();

                return new RequirementAnalysisService(
                    primaryLlmService,
                    promptBuilder,
                    parserManager,
                    healthMonitor: null, // No health monitor for performance
                    cache: cache,
                    anythingLLMService: anythingLLMService,
                    directRagService: directRagService,
                    derivationService: derivationService,
                    gapAnalyzer: gapAnalyzer,
                    complianceWrapper: complianceWrapper);
            });

            // Requirement Analysis Engine - Consolidated analysis functionality (SCOPED - per-request)
            services.AddScoped<IRequirementAnalysisEngine, RequirementAnalysisEngine>();

            // Jama integration services (SINGLETON - API client state)
            services.AddSingleton<JamaConnectService>(provider =>
            {
                try
                {
                    var jamaService = JamaConnectService.FromConfiguration();

                    // Inject OCR service for image text extraction
                    var ocrService = provider.GetService<IOCRService>();
                    if (ocrService != null)
                    {
                        jamaService.SetOCRService(ocrService);
                    }

                    return jamaService;
                }
                catch (Exception ex)
                {
                    // Create a non-configured service that will report proper errors
                    var logger = provider.GetService<ILogger<JamaConnectService>>();
                    logger?.LogWarning("Jama Connect not configured: {Error}", ex.Message);
                    return new JamaConnectService("", ""); // Will properly report "not configured" in IsConfigured
                }
            });

            // Register interface for testable architecture
            services.AddSingleton<IJamaConnectService>(provider => provider.GetRequiredService<JamaConnectService>());

            // Jama Document Parser Service - LLM-powered attachment analysis (SINGLETON - orchestrator)
            services.AddSingleton<IJamaDocumentParserService, JamaDocumentParserService>(provider =>
            {
                var jamaService = provider.GetRequiredService<IJamaConnectService>();
                var llmService = provider.GetRequiredService<IAnythingLLMService>();
                var directRagService = provider.GetService<IDirectRagService>(); // Optional fallback
                var textGenerationService = provider.GetService<ITextGenerationService>(); // For DirectRag fallback
                var derivationService = provider.GetService<ISystemCapabilityDerivationService>(); // ATP derivation
                
                // Template Form Architecture services (Phase 6 integration)
                var envelopeService = provider.GetService<IOutputEnvelopeService>();
                var qualityService = provider.GetService<IFieldLevelQualityService>();
                var complianceWrapper = provider.GetService<IServiceComplianceWrapper>();
                var abTestingFramework = provider.GetService<IABTestingFramework>();
                var telemetryService = provider.GetService<ITelemetryDashboardService>();
                
                // Ollama management
                var ollamaProcessManager = provider.GetService<IOllamaProcessManager>();
                var ollamaStatusMonitor = provider.GetService<IOllamaStatusMonitor>();

                return new JamaDocumentParserService(
                    jamaService,
                    llmService,
                    directRagService,
                    textGenerationService,
                    derivationService,
                    envelopeService,
                    qualityService,
                    complianceWrapper,
                    abTestingFramework,
                    telemetryService,
                    ollamaProcessManager,
                    ollamaStatusMonitor);
            });

            // Jama Test Case Conversion Service - Requirement to test case mapping (SINGLETON)
            services.AddSingleton<IJamaTestCaseConversionService, JamaTestCaseConversionService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<JamaTestCaseConversionService>>();
                return new JamaTestCaseConversionService(logger);
            });

            return services;
        }
    }
}
