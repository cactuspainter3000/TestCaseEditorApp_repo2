using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Integration service connecting quality scoring with training data validation workflow.
    /// Provides continuous feedback loop for quality improvement and scoring calibration.
    /// </summary>
    public class QualityScoringIntegrationService : IQualityScoringIntegrationService
    {
        private readonly ILogger<QualityScoringIntegrationService> _logger;
        private readonly IDerivationQualityScorer _qualityScorer;
        private readonly ITrainingDataValidationService _validationService;
        private readonly ISystemCapabilityDerivationService _derivationService;
        
        // Quality tracking and feedback storage
        private readonly List<QualityValidationCorrelation> _correlationHistory;
        private readonly Dictionary<string, QualityFeedbackSession> _activeFeedbackSessions;

        public QualityScoringIntegrationService(
            ILogger<QualityScoringIntegrationService> logger,
            IDerivationQualityScorer qualityScorer,
            ITrainingDataValidationService validationService,
            ISystemCapabilityDerivationService derivationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _qualityScorer = qualityScorer ?? throw new ArgumentNullException(nameof(qualityScorer));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
            _derivationService = derivationService ?? throw new ArgumentNullException(nameof(derivationService));
            
            _correlationHistory = new List<QualityValidationCorrelation>();
            _activeFeedbackSessions = new Dictionary<string, QualityFeedbackSession>();

            _logger.LogInformation("QualityScoringIntegrationService initialized for quality-validation feedback loop");
        }

        /// <summary>
        /// Starts a comprehensive quality-guided derivation workflow
        /// </summary>
        public async Task<QualityGuidedDerivationResult> PerformQualityGuidedDerivationAsync(
            string atpStepText, 
            QualityGuidanceOptions options = null)
        {
            try
            {
                options ??= new QualityGuidanceOptions();
                
                var sessionId = Guid.NewGuid().ToString();
                
                _logger.LogInformation("Starting quality-guided derivation for session {SessionId}", sessionId);

                var result = new QualityGuidedDerivationResult
                {
                    SessionId = sessionId,
                    StartedAt = DateTime.UtcNow,
                    InputAtpStep = atpStepText
                };

                // Step 1: Initial derivation with real-time feedback
                result.InitialDerivation = await PerformGuidedDerivationAsync(atpStepText, sessionId);
                
                // Step 2: Comprehensive quality scoring
                result.QualityScore = await _qualityScorer.ScoreDerivationQualityAsync(result.InitialDerivation, atpStepText, options.ScoringOptions);
                
                // Step 3: Quality-based refinement if needed
                if (result.QualityScore.OverallScore < options.QualityThreshold && options.EnableAutoRefinement)
                {
                    result.RefinedDerivation = await PerformQualityBasedRefinementAsync(result.InitialDerivation, result.QualityScore);
                    if (result.RefinedDerivation != null)
                    {
                        result.RefinedQualityScore = await _qualityScorer.ScoreDerivationQualityAsync(result.RefinedDerivation, atpStepText);
                    }
                }

                // Step 4: Generate training examples if requested
                if (options.GenerateTrainingExamples)
                {
                    result.SyntheticTrainingExamples = await GenerateQualityBasedTrainingExamplesAsync(result);
                }

                result.CompletedAt = DateTime.UtcNow;
                result.ProcessingDuration = result.CompletedAt - result.StartedAt;

                _logger.LogInformation("Quality-guided derivation completed for session {SessionId}: Initial={InitialScore:F3}, Final={FinalScore:F3}, Duration={Duration}",
                    sessionId, result.QualityScore.OverallScore, 
                    result.RefinedQualityScore?.OverallScore ?? result.QualityScore.OverallScore,
                    result.ProcessingDuration);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to perform quality-guided derivation for ATP: {AtpText}", atpStepText);
                throw;
            }
        }

        /// <summary>
        /// Correlates quality scores with human validation outcomes to improve scoring accuracy
        /// </summary>
        public async Task<QualityValidationCorrelationResult> CorrelateQualityWithValidationAsync(
            List<DerivationQualityScore> qualityScores,
            List<TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult> validationResults)
        {
            try
            {
                _logger.LogInformation("Correlating {QualityCount} quality scores with {ValidationCount} validation results",
                    qualityScores.Count, validationResults.Count);

                var correlationResult = new QualityValidationCorrelationResult
                {
                    CorrelationId = Guid.NewGuid().ToString(),
                    AnalyzedAt = DateTime.UtcNow
                };

                // Match quality scores with validation outcomes
                var matchedPairs = await MatchQualityScoresWithValidationAsync(qualityScores, validationResults);
                
                if (matchedPairs.Any())
                {
                    // Calculate correlation metrics
                    correlationResult.OverallCorrelation = CalculateOverallCorrelation(matchedPairs);
                    correlationResult.DimensionCorrelations = CalculateDimensionCorrelations(matchedPairs);
                    
                    // Analyze prediction accuracy
                    correlationResult.PredictionAccuracy = CalculatePredictionAccuracy(matchedPairs);
                    
                    // Identify scoring improvements needed
                    correlationResult.ScoringImprovements = IdentifyScoringImprovements(matchedPairs);
                    
                    // Store for historical analysis
                    foreach (var pair in matchedPairs)
                    {
                        _correlationHistory.Add(new QualityValidationCorrelation
                        {
                            QualityScore = pair.QualityScore.OverallScore,
                            ValidationOutcome = pair.ValidationResult.Decision,
                            CorrelationId = correlationResult.CorrelationId,
                            RecordedAt = DateTime.UtcNow
                        });
                    }

                    _logger.LogInformation("Quality-validation correlation completed: Overall={Correlation:F3}, Accuracy={Accuracy:F3}, Pairs={PairCount}",
                        correlationResult.OverallCorrelation, correlationResult.PredictionAccuracy, matchedPairs.Count);
                }

                return correlationResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to correlate quality scores with validation results");
                throw;
            }
        }

        /// <summary>
        /// Provides continuous quality feedback loop for scoring system improvement
        /// </summary>
        public async Task<QualityFeedbackLoopResult> RunQualityFeedbackLoopAsync(
            TimeSpan analysisWindow, 
            QualityFeedbackOptions options = null)
        {
            try
            {
                options ??= new QualityFeedbackOptions();
                
                _logger.LogInformation("Running quality feedback loop for window: {Window}", analysisWindow);

                var result = new QualityFeedbackLoopResult
                {
                    LoopId = Guid.NewGuid().ToString(),
                    StartedAt = DateTime.UtcNow,
                    AnalysisWindow = analysisWindow
                };

                // Analyze recent correlation history
                var recentCorrelations = GetRecentCorrelations(analysisWindow);
                
                if (recentCorrelations.Any())
                {
                    // Calculate feedback metrics
                    result.FeedbackMetrics = CalculateFeedbackMetrics(recentCorrelations);
                    
                    // Identify systematic issues
                    result.SystematicIssues = IdentifySystematicIssues(recentCorrelations);
                    
                    // Generate calibration adjustments
                    result.CalibrationAdjustments = GenerateCalibrationAdjustments(recentCorrelations);
                    
                    // Recommend scoring improvements
                    result.ScoringRecommendations = GenerateScoringRecommendations(result.FeedbackMetrics, result.SystematicIssues);
                    
                    // Self-evaluation check
                    if (options.IncludeSelfEvaluation)
                    {
                        result.SelfEvaluationReport = await _qualityScorer.PerformSelfEvaluationAsync();
                    }

                    result.ImprovementPotential = AssessImprovementPotential(result.FeedbackMetrics, result.SystematicIssues);
                }

                result.CompletedAt = DateTime.UtcNow;

                _logger.LogInformation("Quality feedback loop completed for {LoopId}: Correlations={CorrelationCount}, Issues={IssueCount}, Potential={Potential}",
                    result.LoopId, recentCorrelations.Count, result.SystematicIssues.Count, result.ImprovementPotential);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run quality feedback loop");
                throw;
            }
        }

        /// <summary>
        /// Gets real-time quality metrics for active derivation sessions
        /// </summary>
        public async Task<ActiveQualityMetrics> GetActiveQualityMetricsAsync()
        {
            try
            {
                var metrics = new ActiveQualityMetrics
                {
                    MetricsId = Guid.NewGuid().ToString(),
                    CapturedAt = DateTime.UtcNow
                };

                // Active session metrics
                metrics.ActiveSessions = _activeFeedbackSessions.Count;
                
                // Recent scoring statistics
                var recentCorrelations = GetRecentCorrelations(TimeSpan.FromHours(24));
                if (recentCorrelations.Any())
                {
                    metrics.RecentAverageQualityScore = recentCorrelations.Average(c => c.QualityScore);
                    metrics.RecentValidationApprovalRate = recentCorrelations.Count(c => c.ValidationOutcome == ValidationDecision.Approved) / (double)recentCorrelations.Count;
                }

                // System performance
                metrics.TotalCorrelationsRecorded = _correlationHistory.Count;
                metrics.SystemUptimeHours = (DateTime.UtcNow - DateTime.Today).TotalHours;

                return metrics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get active quality metrics");
                throw;
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// Performs guided derivation with real-time quality feedback
        /// </summary>
        private async Task<DerivationResult> PerformGuidedDerivationAsync(string atpStepText, string sessionId)
        {
            // Start feedback session
            var feedbackSession = new QualityFeedbackSession
            {
                SessionId = sessionId,
                StartedAt = DateTime.UtcNow,
                AtpStepText = atpStepText
            };
            
            _activeFeedbackSessions[sessionId] = feedbackSession;

            try
            {
                // Get initial real-time feedback
                var initialFeedback = await _qualityScorer.GetRealTimeQualityFeedbackAsync(atpStepText, new List<DerivedCapability>());
                feedbackSession.FeedbackHistory.Add(initialFeedback);

                // Perform the actual derivation
                var derivationResult = await _derivationService.DeriveCapabilitiesAsync(atpStepText);

                // Get final feedback on complete derivation
                var finalFeedback = await _qualityScorer.GetRealTimeQualityFeedbackAsync(atpStepText, derivationResult.DerivedCapabilities);
                feedbackSession.FeedbackHistory.Add(finalFeedback);

                feedbackSession.CompletedAt = DateTime.UtcNow;

                return derivationResult;
            }
            finally
            {
                // Clean up session
                _activeFeedbackSessions.Remove(sessionId);
            }
        }

        /// <summary>
        /// Performs quality-based refinement of derivation results
        /// </summary>
        private async Task<DerivationResult> PerformQualityBasedRefinementAsync(
            DerivationResult initialResult,
            DerivationQualityScore qualityScore)
        {
            try
            {
                _logger.LogInformation("Performing quality-based refinement for score {Score:F3}", qualityScore.OverallScore);

                // Focus on the most problematic areas
                var prioritizedImprovements = qualityScore.ActionableRecommendations
                    .Take(3) // Focus on top 3 improvements
                    .ToList();

                if (!prioritizedImprovements.Any())
                    return null;

                // Create refinement prompt based on quality feedback
                var refinementPrompt = GenerateRefinementPrompt(initialResult, qualityScore, prioritizedImprovements);
                
                // Re-derive with refined approach
                var refinedResult = await _derivationService.DeriveCapabilitiesAsync(refinementPrompt);
                
                // Transfer original metadata
                refinedResult.SessionId = initialResult.SessionId + "_refined";
                refinedResult.SourceATPContent = initialResult.SourceATPContent;

                _logger.LogInformation("Quality-based refinement completed: Original={OriginalCount} capabilities, Refined={RefinedCount} capabilities",
                    initialResult.DerivedCapabilities.Count, refinedResult.DerivedCapabilities.Count);

                return refinedResult;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Quality-based refinement failed, returning null");
                return null;
            }
        }

        /// <summary>
        /// Generate training examples based on quality assessment
        /// </summary>
        private async Task<List<SyntheticTrainingExample>> GenerateQualityBasedTrainingExamplesAsync(QualityGuidedDerivationResult result)
        {
            var examples = new List<SyntheticTrainingExample>();

            try
            {
                // Use the best available derivation
                var bestResult = result.RefinedDerivation ?? result.InitialDerivation;
                var bestScore = result.RefinedQualityScore ?? result.QualityScore;

                // Only generate training examples for high-quality derivations
                if (bestScore.OverallScore >= 0.7)
                {
                    var trainingExample = new SyntheticTrainingExample
                    {
                        ExampleId = Guid.NewGuid().ToString(),
                        GeneratedAt = DateTime.UtcNow,
                        ATPStepText = result.InputAtpStep,
                        ExpectedCapability = ConvertToExpectedCapabilityDerivation(bestResult.DerivedCapabilities.FirstOrDefault()),
                        QualityScore = bestScore.OverallScore,
                        GenerationMethod = "QualityGuided",
                        ValidationStatus = ValidationStatus.NotValidated
                    };

                    examples.Add(trainingExample);
                }

                return examples;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate quality-based training examples");
                return examples;
            }
        }

        /// <summary>
        /// Match quality scores with validation results
        /// </summary>
        private async Task<List<QualityValidationPair>> MatchQualityScoresWithValidationAsync(
            List<DerivationQualityScore> qualityScores,
            List<TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult> validationResults)
        {
            var pairs = new List<QualityValidationPair>();

            foreach (var qualityScore in qualityScores)
            {
                var matchingValidation = validationResults
                    .FirstOrDefault(v => v.OriginalExample?.ExampleId == qualityScore.DerivationResultId);
                
                if (matchingValidation != null)
                {
                    pairs.Add(new QualityValidationPair
                    {
                        QualityScore = qualityScore,
                        ValidationResult = matchingValidation
                    });
                }
            }

            return pairs;
        }

        /// <summary>
        /// Calculate overall correlation between quality scores and validation outcomes
        /// </summary>
        private double CalculateOverallCorrelation(List<QualityValidationPair> pairs)
        {
            if (pairs.Count < 2) return 0.0;

            var qualityScores = pairs.Select(p => p.QualityScore.OverallScore).ToList();
            var validationScores = pairs.Select(p => ConvertValidationToScore(p.ValidationResult.Decision)).ToList();

            return CalculateCorrelationCoefficient(qualityScores, validationScores);
        }

        /// <summary>
        /// Calculate correlation for individual quality dimensions
        /// </summary>
        private Dictionary<string, double> CalculateDimensionCorrelations(List<QualityValidationPair> pairs)
        {
            var correlations = new Dictionary<string, double>();

            if (pairs.Count < 2) return correlations;

            var allDimensions = pairs
                .SelectMany(p => p.QualityScore.DimensionScores.Keys)
                .Distinct()
                .ToList();

            foreach (var dimension in allDimensions)
            {
                var dimensionScores = pairs
                    .Where(p => p.QualityScore.DimensionScores.ContainsKey(dimension))
                    .Select(p => p.QualityScore.DimensionScores[dimension])
                    .ToList();
                
                var validationScores = pairs
                    .Where(p => p.QualityScore.DimensionScores.ContainsKey(dimension))
                    .Select(p => ConvertValidationToScore(p.ValidationResult.Decision))
                    .ToList();

                if (dimensionScores.Count >= 2)
                {
                    correlations[dimension] = CalculateCorrelationCoefficient(dimensionScores, validationScores);
                }
            }

            return correlations;
        }

        /// <summary>
        /// Calculate prediction accuracy of quality scores
        /// </summary>
        private double CalculatePredictionAccuracy(List<QualityValidationPair> pairs)
        {
            if (!pairs.Any()) return 0.0;

            var correctPredictions = 0;

            foreach (var pair in pairs)
            {
                var predictedApproval = pair.QualityScore.OverallScore >= 0.7;
                var actualApproval = pair.ValidationResult.Decision == ValidationDecision.Approved;

                if (predictedApproval == actualApproval)
                    correctPredictions++;
            }

            return (double)correctPredictions / pairs.Count;
        }

        /// <summary>
        /// Identify needed improvements to scoring system
        /// </summary>
        private List<string> IdentifyScoringImprovements(List<QualityValidationPair> pairs)
        {
            var improvements = new List<string>();

            // Analyze false positives (high score, rejected)
            var falsePositives = pairs.Count(p => 
                p.QualityScore.OverallScore >= 0.7 && p.ValidationResult.Decision == ValidationDecision.Rejected);
            
            if (falsePositives > pairs.Count * 0.2)
                improvements.Add("Reduce false positives by stricter quality criteria");

            // Analyze false negatives (low score, approved)
            var falseNegatives = pairs.Count(p => 
                p.QualityScore.OverallScore < 0.5 && p.ValidationResult.Decision == ValidationDecision.Approved);
            
            if (falseNegatives > pairs.Count * 0.2)
                improvements.Add("Reduce false negatives by more lenient quality criteria");

            // Check dimension-specific issues
            var dimensionCorrelations = CalculateDimensionCorrelations(pairs);
            foreach (var (dimension, correlation) in dimensionCorrelations)
            {
                if (Math.Abs(correlation) < 0.3)
                    improvements.Add($"Improve {dimension} scoring - low correlation with validation");
            }

            return improvements;
        }

        /// <summary>
        /// Get correlations within a time window
        /// </summary>
        private List<QualityValidationCorrelation> GetRecentCorrelations(TimeSpan window)
        {
            var cutoffTime = DateTime.UtcNow - window;
            return _correlationHistory
                .Where(c => c.RecordedAt >= cutoffTime)
                .ToList();
        }

        /// <summary>
        /// Calculate feedback metrics from correlations
        /// </summary>
        private QualityFeedbackMetrics CalculateFeedbackMetrics(List<QualityValidationCorrelation> correlations)
        {
            return new QualityFeedbackMetrics
            {
                TotalCorrelations = correlations.Count,
                AverageQualityScore = correlations.Average(c => c.QualityScore),
                ApprovalRate = correlations.Count(c => c.ValidationOutcome == ValidationDecision.Approved) / (double)correlations.Count,
                CorrelationStrength = CalculateRecentCorrelationStrength(correlations),
                ScoreVariance = CalculateScoreVariance(correlations.Select(c => c.QualityScore))
            };
        }

        /// <summary>
        /// Identify systematic issues in scoring
        /// </summary>
        private List<string> IdentifySystematicIssues(List<QualityValidationCorrelation> correlations)
        {
            var issues = new List<string>();

            if (!correlations.Any()) return issues;

            // Check for systematic over-scoring
            var highScoreLowApproval = correlations
                .Where(c => c.QualityScore >= 0.8)
                .Count(c => c.ValidationOutcome != ValidationDecision.Approved) / (double)correlations.Count(c => c.QualityScore >= 0.8);
            
            if (highScoreLowApproval > 0.3)
                issues.Add("Systematic over-scoring detected - high scores not correlating with approvals");

            // Check for systematic under-scoring  
            var lowScoreHighApproval = correlations
                .Where(c => c.QualityScore <= 0.5)
                .Count(c => c.ValidationOutcome == ValidationDecision.Approved) / (double)correlations.Count(c => c.QualityScore <= 0.5);
            
            if (lowScoreHighApproval > 0.3)
                issues.Add("Systematic under-scoring detected - low scores for approved derivations");

            return issues;
        }

        /// <summary>
        /// Generate calibration adjustments
        /// </summary>
        private Dictionary<string, double> GenerateCalibrationAdjustments(List<QualityValidationCorrelation> correlations)
        {
            var adjustments = new Dictionary<string, double>();

            if (correlations.Count < 10) return adjustments;

            // Calculate optimal threshold based on validation outcomes
            var approvedScores = correlations
                .Where(c => c.ValidationOutcome == ValidationDecision.Approved)
                .Select(c => c.QualityScore)
                .ToList();
            
            var rejectedScores = correlations
                .Where(c => c.ValidationOutcome == ValidationDecision.Rejected)
                .Select(c => c.QualityScore)
                .ToList();

            if (approvedScores.Any() && rejectedScores.Any())
            {
                var optimalThreshold = (approvedScores.Min() + rejectedScores.Max()) / 2.0;
                adjustments["ApprovalThreshold"] = optimalThreshold;
            }

            return adjustments;
        }

        /// <summary>
        /// Generate scoring recommendations
        /// </summary>
        private List<string> GenerateScoringRecommendations(
            QualityFeedbackMetrics metrics,
            List<string> systematicIssues)
        {
            var recommendations = new List<string>();

            if (metrics.CorrelationStrength < 0.5)
                recommendations.Add("Improve correlation by adjusting dimension weights based on validation patterns");

            if (metrics.ScoreVariance < 0.05)
                recommendations.Add("Increase score sensitivity to better distinguish quality levels");
            else if (metrics.ScoreVariance > 0.3)
                recommendations.Add("Reduce score volatility for more consistent assessments");

            if (systematicIssues.Any())
                recommendations.Add("Address systematic scoring biases through calibration updates");

            return recommendations;
        }

        /// <summary>
        /// Assess improvement potential
        /// </summary>
        private double AssessImprovementPotential(QualityFeedbackMetrics metrics, List<string> systematicIssues)
        {
            var potential = 0.0;

            // Base potential from correlation strength
            potential += (1.0 - metrics.CorrelationStrength) * 0.4;

            // Additional potential from systematic issues
            potential += systematicIssues.Count * 0.15;

            // Score variance issues
            if (metrics.ScoreVariance < 0.05 || metrics.ScoreVariance > 0.3)
                potential += 0.2;

            return Math.Min(1.0, potential);
        }

        #endregion

        #region Utility Methods

        private double ConvertValidationToScore(ValidationDecision decision)
        {
            return decision switch
            {
                ValidationDecision.Approved => 1.0,
                ValidationDecision.RequiresEdits => 0.6,
                ValidationDecision.Skipped => 0.5,
                ValidationDecision.Rejected => 0.0,
                _ => 0.5
            };
        }

        private double CalculateCorrelationCoefficient(List<double> x, List<double> y)
        {
            if (x.Count != y.Count || x.Count < 2) return 0.0;

            var meanX = x.Average();
            var meanY = y.Average();

            var numerator = x.Zip(y, (xi, yi) => (xi - meanX) * (yi - meanY)).Sum();
            var denominatorX = Math.Sqrt(x.Sum(xi => Math.Pow(xi - meanX, 2)));
            var denominatorY = Math.Sqrt(y.Sum(yi => Math.Pow(yi - meanY, 2)));

            return denominatorX * denominatorY == 0 ? 0 : numerator / (denominatorX * denominatorY);
        }

        private double CalculateRecentCorrelationStrength(List<QualityValidationCorrelation> correlations)
        {
            if (correlations.Count < 2) return 0.0;

            var qualityScores = correlations.Select(c => c.QualityScore).ToList();
            var validationScores = correlations.Select(c => ConvertValidationToScore(c.ValidationOutcome)).ToList();

            return Math.Abs(CalculateCorrelationCoefficient(qualityScores, validationScores));
        }

        private double CalculateScoreVariance(IEnumerable<double> scores)
        {
            var scoreList = scores.ToList();
            if (scoreList.Count < 2) return 0.0;

            var mean = scoreList.Average();
            return scoreList.Sum(s => Math.Pow(s - mean, 2)) / scoreList.Count;
        }

        private string GenerateRefinementPrompt(
            DerivationResult originalResult,
            DerivationQualityScore qualityScore,
            List<string> improvements)
        {
            var prompt = $"Original ATP: {originalResult.SourceATPContent}\n\n";
            prompt += "Quality Assessment Identified These Improvement Areas:\n";
            
            foreach (var improvement in improvements)
            {
                prompt += $"- {improvement}\n";
            }

            prompt += "\nPlease re-analyze the ATP step with focus on these improvement areas.";
            
            return prompt;
        }

        private ExpectedCapabilityDerivation ConvertToExpectedCapabilityDerivation(DerivedCapability capability)
        {
            if (capability == null) return new ExpectedCapabilityDerivation();

            return new ExpectedCapabilityDerivation
            {
                RequirementText = capability.RequirementText,
                TaxonomyCategory = capability.TaxonomyCategory,
                TaxonomySubcategory = capability.TaxonomySubcategory,
                DerivationRationale = capability.DerivationRationale,
                AllocationTargets = capability.AllocationTargets,
                MissingSpecifications = capability.MissingSpecifications,
                VerificationIntent = capability.VerificationIntent
            };
        }

        #endregion
    }
}