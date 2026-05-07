using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TrainingData.Services
{
    /// <summary>
    /// Interface for integrating quality scoring with derivation and validation workflows
    /// </summary>
    public interface IQualityScoringIntegrationService
    {
        /// <summary>
        /// Performs quality-guided capability derivation with automatic refinement based on quality scores
        /// </summary>
        /// <param name="atpStepText">ATP step text to derive capabilities from</param>
        /// <param name="options">Quality guidance options</param>
        /// <returns>Quality-guided derivation result with scores and refinements</returns>
        Task<QualityGuidedDerivationResult> PerformQualityGuidedDerivationAsync(
            string atpStepText, 
            QualityGuidanceOptions options);

        /// <summary>
        /// Correlates quality scores with validation outcomes to assess scoring system accuracy
        /// </summary>
        /// <param name="qualityValidationPairs">Pairs of quality scores and validation results</param>
        /// <returns>Correlation analysis results</returns>
        Task<QualityValidationCorrelationResult> CorrelateQualityWithValidationAsync(
            IEnumerable<QualityValidationPair> qualityValidationPairs);

        /// <summary>
        /// Runs quality feedback loop to improve scoring system based on validation outcomes
        /// </summary>
        /// <param name="analysisWindow">Time window to analyze</param>
        /// <param name="options">Feedback loop options</param>
        /// <returns>Feedback loop results with improvement recommendations</returns>
        Task<QualityFeedbackLoopResult> RunQualityFeedbackLoopAsync(
            TimeSpan analysisWindow, 
            QualityFeedbackOptions options);

        /// <summary>
        /// Gets active quality metrics for real-time monitoring
        /// </summary>
        /// <returns>Current quality metrics snapshot</returns>
        Task<ActiveQualityMetrics> GetActiveQualityMetricsAsync();
    }
}