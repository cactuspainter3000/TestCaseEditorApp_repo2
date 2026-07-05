using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.Prompts;
using TestCaseEditorApp.Services.Parsing;
using TestCaseEditorApp.Services.Templates;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using System;

namespace TestCaseEditorApp.Services.DependencyInjection
{
    /// <summary>
    /// System Capability Derivation and Advanced Analysis services.
    /// These are specialized LLM-powered services for ATP (Acceptance Test Procedure) analysis and capability extraction.
    /// All are SINGLETON because they maintain state about capability taxonomies and quality metrics.
    /// </summary>
    public static class CapabilityDerivationExtensions
    {
        public static IServiceCollection AddCapabilityDerivationServices(this IServiceCollection services)
        {
            // ATP Step Parser - Extract and classify test procedure steps (SINGLETON - taxonomy definitions)
            services.AddSingleton<ATPStepParser>();

            // Taxonomy Validator - Validate A-N taxonomy assignments (SINGLETON - shared taxonomy)
            services.AddSingleton<TaxonomyValidator>();

            // Capability Allocator - Intelligent subsystem allocation (SINGLETON - taxonomy-based)
            services.AddSingleton<ICapabilityAllocator, CapabilityAllocator>();

            // Synthetic Training Data Generator - Generate ATP+derivation pairs (SINGLETON - template state)
            services.AddSingleton<ISyntheticTrainingDataGenerator, SyntheticTrainingDataGenerator>();

            // MBSE Requirement Classifier - System-level requirement classification (SINGLETON - taxonomy)
            services.AddSingleton<IMBSERequirementClassifier, MBSERequirementClassifier>();

            // Derivation Quality Scorer - Multi-dimensional quality assessment (SINGLETON - metric definitions)
            services.AddSingleton<IDerivationQualityScorer, DerivationQualityScorer>();

            // Capability Derivation Service - ATP-to-requirements derivation (SINGLETON - orchestrator)
            services.AddSingleton<ISystemCapabilityDerivationService, SystemCapabilityDerivationService>();

            // Requirement Gap Analyzer - Derived vs existing comparison (SINGLETON - analysis rules)
            services.AddSingleton<IRequirementGapAnalyzer, RequirementGapAnalyzer>();

            // Prompt Refinement Engine - Intelligent prompt optimization (SINGLETON - A/B testing state)
            services.AddSingleton<IPromptRefinementEngine, PromptRefinementEngine>();

            // Prompt Optimization Integration - Bridge refinement and derivation (SINGLETON - integration state)
            services.AddSingleton<IPromptOptimizationIntegrationService, PromptOptimizationIntegrationService>();

            return services;
        }
    }
}
