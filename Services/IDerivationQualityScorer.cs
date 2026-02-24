using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Interface for advanced quality scoring service for capability derivation processes.
    /// Provides automated quality assessment, self-evaluation metrics, and continuous improvement feedback.
    /// </summary>
    public interface IDerivationQualityScorer
    {
        /// <summary>
        /// Performs comprehensive quality scoring for a capability derivation result
        /// </summary>
        /// <param name="derivationResult">The derivation result to score</param>
        /// <param name=\"sourceAtpText\">Original ATP text that was processed</param>
        /// <param name=\"options\">Optional scoring configuration</param>
        /// <returns>Comprehensive quality score with multi-dimensional analysis</returns>
        Task<DerivationQualityScore> ScoreDerivationQualityAsync(
            DerivationResult derivationResult, 
            string sourceAtpText, 
            QualityScoringOptions options = null);

        /// <summary>
        /// Batch scoring for multiple derivation results with efficiency optimizations
        /// </summary>
        /// <param name="derivationResults">Multiple derivation results to score</param>
        /// <param name="options">Optional scoring configuration</param>
        /// <returns>List of quality scores for each derivation result</returns>
        Task<List<DerivationQualityScore>> ScoreBatchDerivationsAsync(
            List<DerivationResult> derivationResults,
            QualityScoringOptions options = null);

        /// <summary>
        /// Self-evaluation of the quality scoring system's own performance
        /// </summary>
        /// <returns>Report on scoring system performance and recommendations for improvement</returns>
        Task<SelfEvaluationReport> PerformSelfEvaluationAsync();

        /// <summary>
        /// Real-time quality feedback during active derivation processes
        /// </summary>
        /// <param name="atpStepText">Current ATP step being processed</param>
        /// <param name="partialCapabilities">Capabilities derived so far</param>
        /// <returns>Real-time quality indicators and suggestions</returns>
        Task<RealTimeQualityFeedback> GetRealTimeQualityFeedbackAsync(
            string atpStepText,
            List<DerivedCapability> partialCapabilities);
    }
}