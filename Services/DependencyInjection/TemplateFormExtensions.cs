using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.Services.Templates;

namespace TestCaseEditorApp.Services.DependencyInjection
{
    /// <summary>
    /// Template Form Architecture services: structured LLM interaction with constraints, validation, and compliance.
    /// These services enable deterministic, reliable LLM output through template-based approach (Phase 6).
    /// Mix of TRANSIENT (per-request) and SINGLETON (state) based on use case.
    /// </summary>
    public static class TemplateFormExtensions
    {
        public static IServiceCollection AddTemplateFormServices(this IServiceCollection services)
        {
            // Template-based LLM services (TRANSIENT - fresh instance per use)
            services.AddTransient<ICapabilityDerivationTemplateService, CapabilityDerivationTemplateService>();
            services.AddTransient<ISelfAuditingTemplateService, SelfAuditingTemplateService>();

            // Hard/Soft Constraint System (TRANSIENT - process-specific constraints)
            services.AddTransient<IConstraintProcessor, ConstraintProcessor>();
            services.AddTransient<ITemplateConstraintService, TemplateConstraintService>();
            services.AddTransient<IConstraintRuleEngine, ConstraintRuleEngine>();
            services.AddTransient<IConstraintMetricsCollector, ConstraintMetricsCollector>();

            // Deterministic Output Envelopes (TRANSIENT - per-output schema)
            services.AddTransient<IEnvelopeSchemaService, EnvelopeSchemaService>();
            services.AddTransient<IOutputEnvelopeService, OutputEnvelopeService>();

            // Field-Level Quality Metrics (TRANSIENT - per-field analysis)
            services.AddTransient<IFieldLevelQualityService, FieldLevelQualityService>();

            // Service Compliance Wrapper (SINGLETON - shared compliance rules)
            services.AddSingleton<IServiceComplianceWrapper, ServiceComplianceWrapper>();

            // A/B Testing Framework (SINGLETON - test state and metrics)
            services.AddSingleton<IABTestingFramework, ABTestingFramework>();

            // Telemetry Dashboard Service (SINGLETON - metrics aggregation)
            services.AddSingleton<ITelemetryDashboardService, TelemetryDashboardService>();

            return services;
        }
    }
}
