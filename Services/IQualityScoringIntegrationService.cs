using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services.Templates;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Interface for quality scoring integration service that connects
    /// quality assessment with training data validation workflows.
    /// Task 6.5 Enhanced: Template Form Architecture integration added
    /// </summary>
    public interface IQualityScoringIntegrationService
    {
        /// <summary>
        /// Starts a comprehensive quality-guided derivation workflow
        /// </summary>
        /// <param name="atpStepText">ATP step to process with quality guidance</param>
        /// <param name="options">Configuration options for quality guidance</param>
        /// <returns>Complete quality-guided derivation result</returns>
        Task<QualityGuidedDerivationResult> PerformQualityGuidedDerivationAsync(
            string atpStepText, 
            QualityGuidanceOptions options = null);

        /// <summary>
        /// Correlates quality scores with human validation outcomes to improve scoring accuracy
        /// </summary>
        /// <param name="qualityScores">Quality scores to correlate</param>
        /// <param name="validationResults">Human validation results</param>
        /// <returns>Correlation analysis and improvement recommendations</returns>
        Task<QualityValidationCorrelationResult> CorrelateQualityWithValidationAsync(
            List<DerivationQualityScore> qualityScores,
            List<TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult> validationResults);

        /// <summary>
        /// Provides continuous quality feedback loop for scoring system improvement
        /// </summary>
        /// <param name="analysisWindow">Time window for analysis</param>
        /// <param name="options">Feedback loop options</param>
        /// <returns>Feedback loop results and recommendations</returns>
        Task<QualityFeedbackLoopResult> RunQualityFeedbackLoopAsync(
            TimeSpan analysisWindow, 
            QualityFeedbackOptions options = null);

        /// <summary>
        /// Gets real-time quality metrics for active derivation sessions
        /// </summary>
        /// <returns>Current quality metrics and system status</returns>
        Task<ActiveQualityMetrics> GetActiveQualityMetricsAsync();

        // Task 6.5: Template Form Architecture Integration Methods

        /// <summary>
        /// Evaluates template form completeness with quality-based recommendations
        /// Task 6.5: Integration of quality scoring with Template Form Architecture
        /// </summary>
        /// <param name="formTemplate">Template to evaluate</param>
        /// <param name="formData">Actual form data</param>
        /// <param name="options">Quality evaluation options</param>
        /// <returns>Comprehensive template form quality assessment</returns>
        Task<TemplateFormQualityAssessment> EvaluateTemplateFormQualityAsync(
            IFormTemplate formTemplate, 
            Dictionary<string, object> formData,
            TemplateQualityOptions options = null);

        /// <summary>
        /// Performs comprehensive field-level quality analysis with retry patterns and confidence metrics
        /// </summary>
        /// <param name="formTemplate">Template to analyze</param>
        /// <param name="formData">Form data to analyze</param>
        /// <returns>Detailed field-level quality analysis</returns>
        Task<FieldLevelQualityAnalysisResult> AnalyzeFieldLevelQualityAsync(
            IFormTemplate formTemplate,
            Dictionary<string, object> formData);

        /// <summary>
        /// Applies quality-based constraint degradation strategies
        /// </summary>
        /// <param name="formTemplate">Template with constraints</param>
        /// <param name="formData">Form data to process</param>
        /// <param name="recommendations">Degradation recommendations</param>
        /// <returns>Constraint degradation results</returns>
        Task<ConstraintDegradationResult> ApplyQualityBasedConstraintDegradationAsync(
            IFormTemplate formTemplate,
            Dictionary<string, object> formData,
            QualityDegradationRecommendations recommendations);

        /// <summary>
        /// Gets comprehensive quality dashboard data for Template Form Architecture monitoring
        /// </summary>
        /// <returns>Template form quality dashboard data</returns>
        Task<TemplateFormQualityDashboard> GetTemplateFormQualityDashboardAsync();
    }
}