using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Interface for quality scoring integration service that connects
    /// quality assessment with training data validation workflows.
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
    }
}