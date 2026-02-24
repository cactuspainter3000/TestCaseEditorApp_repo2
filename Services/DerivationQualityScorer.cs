using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Advanced quality scoring service for capability derivation processes.
    /// Provides automated quality assessment, self-evaluation metrics, and continuous improvement feedback.
    /// </summary>
    public class DerivationQualityScorer : IDerivationQualityScorer
    {
        private readonly ILogger<DerivationQualityScorer> _logger;
        private readonly TaxonomyValidator _taxonomyValidator;
        private readonly ISystemCapabilityDerivationService _derivationService;
        
        // Quality scoring weights and thresholds
        private readonly QualityWeights _weights;
        private readonly List<QualityScoreRecord> _historicalScores;

        public DerivationQualityScorer(
            ILogger<DerivationQualityScorer> logger,
            TaxonomyValidator taxonomyValidator,
            ISystemCapabilityDerivationService derivationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _taxonomyValidator = taxonomyValidator ?? throw new ArgumentNullException(nameof(taxonomyValidator));
            _derivationService = derivationService ?? throw new ArgumentNullException(nameof(derivationService));
            
            _weights = new QualityWeights();
            _historicalScores = new List<QualityScoreRecord>();
            
            _logger.LogInformation("DerivationQualityScorer initialized with evaluation framework");
        }

        /// <summary>
        /// Performs comprehensive quality scoring for a capability derivation result
        /// </summary>
        public async Task<DerivationQualityScore> ScoreDerivationQualityAsync(
            DerivationResult derivationResult, 
            string sourceAtpText, 
            QualityScoringOptions options = null)
        {
            try
            {
                options ??= new QualityScoringOptions();
                
                var qualityScore = new DerivationQualityScore
                {
                    ScoringId = Guid.NewGuid().ToString(),
                    DerivationResultId = derivationResult.SessionId,
                    ScoredAt = DateTime.UtcNow,
                    ScoringVersion = "1.0"
                };

                _logger.LogInformation("Starting quality scoring for derivation result {ResultId} with {CapabilityCount} capabilities", 
                    derivationResult.SessionId, derivationResult.DerivedCapabilities.Count);

                // Multi-dimensional quality assessment
                qualityScore.DimensionScores = await CalculateDimensionScoresAsync(derivationResult, options);
                
                // Overall weighted score
                qualityScore.OverallScore = CalculateWeightedOverallScore(qualityScore.DimensionScores);
                
                // Quality confidence level
                qualityScore.ConfidenceLevel = CalculateConfidenceLevel(qualityScore.DimensionScores, derivationResult);
                
                // Improvement recommendations
                qualityScore.ImprovementAreas = IdentifyImprovementAreas(qualityScore.DimensionScores);
                qualityScore.ActionableRecommendations = GenerateActionableRecommendations(qualityScore.DimensionScores, derivationResult);
                
                // Comparative assessment
                qualityScore.RelativePerformance = await AssessRelativePerformanceAsync(qualityScore.OverallScore);
                
                // Store for trend analysis
                RecordQualityScore(qualityScore, derivationResult);

                _logger.LogInformation("Quality scoring completed for {ResultId}: Overall={OverallScore:F3}, Confidence={Confidence}, Dimensions={DimensionCount}",
                    derivationResult.SessionId, qualityScore.OverallScore, qualityScore.ConfidenceLevel, qualityScore.DimensionScores.Count);

                return qualityScore;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to score derivation quality for result {ResultId}", derivationResult.SessionId);
                throw;
            }
        }

        /// <summary>
        /// Batch scoring for multiple derivation results with efficiency optimizations
        /// </summary>
        public async Task<List<DerivationQualityScore>> ScoreBatchDerivationsAsync(
            List<DerivationResult> derivationResults,
            QualityScoringOptions options = null)
        {
            try
            {
                options ??= new QualityScoringOptions();
                var scores = new List<DerivationQualityScore>();

                _logger.LogInformation("Starting batch quality scoring for {ResultCount} derivation results", derivationResults.Count);

                // Process in parallel for efficiency
                var scoringTasks = derivationResults.Select(async result =>
                {
                    try
                    {
                        return await ScoreDerivationQualityAsync(result, string.Empty, options);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to score individual derivation {ResultId}, skipping", result.SessionId);
                        return null;
                    }
                });

                var results = await Task.WhenAll(scoringTasks);
                scores.AddRange(results.Where(s => s != null));

                // Batch-level analytics
                if (scores.Any())
                {
                    var avgScore = scores.Average(s => s.OverallScore);
                    var scoreRange = scores.Max(s => s.OverallScore) - scores.Min(s => s.OverallScore);
                    
                    _logger.LogInformation("Batch scoring completed: {SuccessCount}/{TotalCount} successful, Avg={AvgScore:F3}, Range={Range:F3}",
                        scores.Count, derivationResults.Count, avgScore, scoreRange);
                }

                return scores;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to complete batch quality scoring");
                throw;
            }
        }

        /// <summary>
        /// Self-evaluation of the quality scoring system's own performance
        /// </summary>
        public async Task<SelfEvaluationReport> PerformSelfEvaluationAsync()
        {
            try
            {
                _logger.LogInformation("Starting self-evaluation of quality scoring system");
                
                var report = new SelfEvaluationReport
                {
                    EvaluationId = Guid.NewGuid().ToString(),
                    EvaluatedAt = DateTime.UtcNow,
                    ScoringVersion = "1.0"
                };

                // Analyze historical scoring patterns
                report.ScoringConsistency = AnalyzeScoringConsistency();
                report.PredictiveAccuracy = await CalculatePredictiveAccuracyAsync();
                report.BiasDetection = DetectScoringBias();
                
                // System performance metrics
                report.SystemMetrics = CalculateSystemPerformanceMetrics();
                
                // Calibration assessment
                report.CalibrationQuality = AssessScoreCalibration();
                
                // Improvement recommendations for the scoring system itself
                report.SystemImprovements = GenerateSystemImprovementRecommendations(report);

                _logger.LogInformation("Self-evaluation completed: Consistency={Consistency:F3}, Accuracy={Accuracy:F3}, BiasReport={BiasCount}",
                    report.ScoringConsistency, report.PredictiveAccuracy, report.BiasDetection.DetectedBiases.Count);

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Self-evaluation failed");
                throw;
            }
        }

        /// <summary>
        /// Real-time quality feedback during active derivation processes  
        /// </summary>
        public async Task<RealTimeQualityFeedback> GetRealTimeQualityFeedbackAsync(
            string atpStepText,
            List<DerivedCapability> partialCapabilities)
        {
            try
            {
                var feedback = new RealTimeQualityFeedback
                {
                    FeedbackId = Guid.NewGuid().ToString(),
                    GeneratedAt = DateTime.UtcNow,
                    InputAtpStep = atpStepText
                };

                // Quick quality indicators for immediate feedback
                feedback.CurrentQualityIndicators = await CalculateRealTimeIndicators(atpStepText, partialCapabilities);
                
                // Early warning system for quality issues
                feedback.QualityWarnings = DetectEarlyQualityIssues(atpStepText, partialCapabilities);
                
                // Suggested improvements for in-progress derivation
                feedback.ImmediateImprovements = SuggestImmediateImprovements(partialCapabilities);

                return feedback;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate real-time quality feedback");
                throw;
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// Calculate multi-dimensional quality scores
        /// </summary>
        private async Task<Dictionary<string, double>> CalculateDimensionScoresAsync(
            DerivationResult result, 
            QualityScoringOptions options)
        {
            var scores = new Dictionary<string, double>();

            // Taxonomy compliance scoring
            scores["TaxonomyCompliance"] = await ScoreTaxonomyComplianceAsync(result.DerivedCapabilities);
            
            // Completeness assessment
            scores["Completeness"] = ScoreCompletenessQuality(result);
            
            // Specificity and clarity
            scores["Specificity"] = ScoreSpecificityQuality(result.DerivedCapabilities);
            
            // ATP-capability alignment 
            scores["AtpAlignment"] = ScoreAtpAlignmentQuality(result);
            
            // Testability assessment
            scores["Testability"] = ScoreTestabilityQuality(result.DerivedCapabilities);
            
            // Consistency across capabilities
            scores["Consistency"] = ScoreInternalConsistency(result.DerivedCapabilities);
            
            // Realistic implementation feasibility
            scores["Feasibility"] = ScoreFeasibilityRealism(result.DerivedCapabilities);
            
            // Missing specification identification
            scores["SpecificationCompleteness"] = ScoreSpecificationCompleteness(result.DerivedCapabilities);

            return scores;
        }

        /// <summary>
        /// Score taxonomy compliance using advanced validation
        /// </summary>
        private async Task<double> ScoreTaxonomyComplianceAsync(List<DerivedCapability> capabilities)
        {
            if (!capabilities.Any()) return 0.0;

            try
            {
                var validationOptions = new TaxonomyValidationOptions
                {
                    RequireSpecificSubcategories = true,
                    ValidateExpectedCategories = true,
                    DetectDuplicates = true
                };

                var validationResult = _taxonomyValidator.ValidateDerivationResult(
                    capabilities, 
                    string.Empty,
                    validationOptions);

                // Convert validation results to quality score
                var baseScore = validationResult.IsValid ? 0.8 : 0.4;
                var errorPenalty = validationResult.Issues.Count(i => i.Severity == TaxonomyValidationSeverity.Error) * 0.1;
                var warningPenalty = validationResult.Issues.Count(i => i.Severity == TaxonomyValidationSeverity.Warning) * 0.05;

                var score = Math.Max(0.0, baseScore - errorPenalty - warningPenalty);
                
                _logger.LogDebug("Taxonomy compliance score: {Score:F3} (base={BaseScore:F3}, errors={Errors}, warnings={Warnings})",
                    score, baseScore, errorPenalty / 0.1, warningPenalty / 0.05);

                return score;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate taxonomy compliance score, using default");
                return 0.5;
            }
        }

        /// <summary>
        /// Score completeness relative to ATP input content
        /// </summary>
        private double ScoreCompletenessQuality(DerivationResult result)
        {
            if (string.IsNullOrWhiteSpace(result.SourceATPContent) || !result.DerivedCapabilities.Any())
                return 0.0;

            try
            {
                // Analyze ATP step complexity indicators
                var atpComplexityIndicators = ExtractComplexityIndicators(result.SourceATPContent);
                var expectedCapabilityCount = EstimateExpectedCapabilityCount(atpComplexityIndicators);
                var actualCapabilityCount = result.DerivedCapabilities.Count;

                // Score based on coverage relative to expected complexity
                var coverageRatio = expectedCapabilityCount > 0 ? 
                    Math.Min(1.0, (double)actualCapabilityCount / expectedCapabilityCount) : 0.5;

                // Bonus for finding capabilities in each major complexity area
                var complexityAreasCovered = CountComplexityAreasCovered(result.DerivedCapabilities, atpComplexityIndicators);
                var complexityBonus = complexityAreasCovered * 0.1;

                var score = Math.Min(1.0, coverageRatio + complexityBonus);
                
                _logger.LogDebug("Completeness score: {Score:F3} (coverage={Coverage:F3}, areas={Areas}, expected={Expected})",
                    score, coverageRatio, complexityAreasCovered, expectedCapabilityCount);

                return score;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate completeness score, using default");
                return 0.5;
            }
        }

        /// <summary>
        /// Score specificity and clarity of derived capabilities
        /// </summary>
        private double ScoreSpecificityQuality(List<DerivedCapability> capabilities)
        {
            if (!capabilities.Any()) return 0.0;

            try
            {
                var specificityScores = capabilities.Select(capability =>
                {
                    var score = 0.0;

                    // Penalize vague language
                    if (ContainsVagueLanguage(capability.RequirementText))
                        score -= 0.2;

                    // Reward specific measurements and criteria
                    if (ContainsSpecificMeasurements(capability.RequirementText))
                        score += 0.3;

                    // Check for testable criteria
                    if (HasTestableCriteria(capability.RequirementText))
                        score += 0.2;

                    // Reward clear specifications
                    if (capability.MissingSpecifications.Count < 3)
                        score += 0.2;

                    // Ensure reasonable baseline
                    return Math.Max(0.1, Math.Min(1.0, score + 0.5));
                });

                var avgScore = specificityScores.Average();
                
                _logger.LogDebug("Specificity score: {Score:F3} (capabilities analyzed: {Count})", 
                    avgScore, capabilities.Count);

                return avgScore;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate specificity score, using default");
                return 0.5;
            }
        }

        /// <summary>
        /// Score alignment between ATP steps and derived capabilities
        /// </summary>
        private double ScoreAtpAlignmentQuality(DerivationResult result)
        {
            if (string.IsNullOrWhiteSpace(result.SourceATPContent) || !result.DerivedCapabilities.Any())
                return 0.0;

            try
            {
                var atpKeywords = ExtractKeywords(result.SourceATPContent);
                var alignmentScores = result.DerivedCapabilities.Select(capability =>
                {
                    var capabilityKeywords = ExtractKeywords(capability.RequirementText + " " + capability.DerivationRationale);
                    
                    // Calculate keyword overlap
                    var commonKeywords = atpKeywords.Intersect(capabilityKeywords, StringComparer.OrdinalIgnoreCase).Count();
                    var totalKeywords = atpKeywords.Union(capabilityKeywords, StringComparer.OrdinalIgnoreCase).Count();
                    
                    var overlapScore = totalKeywords > 0 ? (double)commonKeywords / totalKeywords : 0.0;
                    
                    // Bonus for meaningful rationale
                    var rationaleBonus = !string.IsNullOrWhiteSpace(capability.DerivationRationale) ? 0.2 : 0.0;
                    
                    return Math.Min(1.0, overlapScore + rationaleBonus);
                });

                var avgScore = alignmentScores.Average();
                
                _logger.LogDebug("ATP alignment score: {Score:F3} (ATP keywords: {AtpKeywords}, capabilities: {CapCount})",
                    avgScore, atpKeywords.Count, result.DerivedCapabilities.Count);

                return avgScore;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate ATP alignment score, using default");
                return 0.5;
            }
        }

        /// <summary>
        /// Score testability of derived capabilities
        /// </summary>
        private double ScoreTestabilityQuality(List<DerivedCapability> capabilities)
        {
            if (!capabilities.Any()) return 0.0;

            try
            {
                var testabilityScores = capabilities.Select(capability =>
                {
                    var score = 0.0;

                    // Reward measurable criteria
                    if (ContainsMeasurableCriteria(capability.RequirementText))
                        score += 0.3;

                    // Reward clear verification intent
                    if (!string.IsNullOrEmpty(capability.VerificationIntent) && capability.VerificationIntent != "TBD")
                        score += 0.2;

                    // Penalize excessive missing specifications
                    if (capability.MissingSpecifications.Count > 5)
                        score -= 0.2;

                    // Reward specific pass/fail criteria
                    if (ContainsPassFailCriteria(capability.RequirementText))
                        score += 0.3;

                    return Math.Max(0.0, Math.Min(1.0, score + 0.4));
                });

                var avgScore = testabilityScores.Average();
                
                _logger.LogDebug("Testability score: {Score:F3} (capabilities: {Count})", avgScore, capabilities.Count);

                return avgScore;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate testability score, using default");
                return 0.5;
            }
        }

        /// <summary>
        /// Score internal consistency across all derived capabilities
        /// </summary>
        private double ScoreInternalConsistency(List<DerivedCapability> capabilities)
        {
            if (capabilities.Count < 2) return 1.0; // Perfect consistency with single capability

            try
            {
                var consistencyFactors = new List<double>();

                // Taxonomy category consistency
                var categories = capabilities.Select(c => c.TaxonomyCategory).Distinct().Count();
                var categoryConsistency = categories <= Math.Ceiling(capabilities.Count * 0.6) ? 1.0 : 0.7;
                consistencyFactors.Add(categoryConsistency);

                // Terminology consistency
                var terminologyConsistency = AssessTerminologyConsistency(capabilities);
                consistencyFactors.Add(terminologyConsistency);

                // Specification format consistency
                var formatConsistency = AssessSpecificationFormatConsistency(capabilities);  
                consistencyFactors.Add(formatConsistency);

                // Abstraction level consistency
                var abstractionConsistency = AssessAbstractionLevelConsistency(capabilities);
                consistencyFactors.Add(abstractionConsistency);

                var avgConsistency = consistencyFactors.Average();
                
                _logger.LogDebug("Internal consistency score: {Score:F3} (categories={Cat}, terminology={Term}, format={Format}, abstraction={Abs})",
                    avgConsistency, categoryConsistency, terminologyConsistency, formatConsistency, abstractionConsistency);

                return avgConsistency;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate internal consistency score, using default");
                return 0.5;
            }
        }

        /// <summary>
        /// Score feasibility and realism of implementation
        /// </summary>
        private double ScoreFeasibilityRealism(List<DerivedCapability> capabilities)
        {
            if (!capabilities.Any()) return 0.0;

            try
            {
                var feasibilityScores = capabilities.Select(capability =>
                {
                    var score = 0.5; // Start with neutral

                    // Penalize unrealistic performance requirements
                    if (ContainsUnrealisticPerformance(capability.RequirementText))
                        score -= 0.3;

                    // Penalize conflicting requirements
                    if (ContainsConflictingRequirements(capability.RequirementText))
                        score -= 0.2;

                    // Reward well-defined allocations
                    if (capability.AllocationTargets.Any() && !capability.AllocationTargets.Contains("TBD"))
                        score += 0.2;

                    // Reward reasonable confidence scores
                    if (capability.ConfidenceScore >= 0.6 && capability.ConfidenceScore <= 0.9)
                        score += 0.1;

                    return Math.Max(0.0, Math.Min(1.0, score));
                });

                var avgScore = feasibilityScores.Average();
                
                _logger.LogDebug("Feasibility score: {Score:F3} (capabilities: {Count})", avgScore, capabilities.Count);

                return avgScore;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate feasibility score, using default");
                return 0.5;
            }
        }

        /// <summary>
        /// Score completeness of specification identification
        /// </summary>
        private double ScoreSpecificationCompleteness(List<DerivedCapability> capabilities)
        {
            if (!capabilities.Any()) return 0.0;

            try
            {
                var specificationScores = capabilities.Select(capability =>
                {
                    var score = 0.0;

                    // Reward identification of key specification areas
                    if (capability.MissingSpecifications.Any(s => s.ToLower().Contains("tolerance")))
                        score += 0.15;
                    if (capability.MissingSpecifications.Any(s => s.ToLower().Contains("timing")))
                        score += 0.15;
                    if (capability.MissingSpecifications.Any(s => s.ToLower().Contains("accuracy")))
                        score += 0.15;

                    // Penalize excessive or insufficient missing specs
                    var specCount = capability.MissingSpecifications.Count;
                    if (specCount >= 2 && specCount <= 5)
                        score += 0.3;
                    else if (specCount > 8)
                        score -= 0.2;

                    // Reward specific, actionable missing specifications
                    var specificSpecs = capability.MissingSpecifications.Count(IsSpecificSpecification);
                    score += Math.Min(0.25, specificSpecs * 0.05);

                    return Math.Max(0.0, Math.Min(1.0, score + 0.3));
                });

                var avgScore = specificationScores.Average();
                
                _logger.LogDebug("Specification completeness score: {Score:F3} (capabilities: {Count})", 
                    avgScore, capabilities.Count);

                return avgScore;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate specification completeness score, using default");
                return 0.5;
            }
        }

        /// <summary>
        /// Calculate weighted overall score from dimension scores
        /// </summary>
        private double CalculateWeightedOverallScore(Dictionary<string, double> dimensionScores)
        {
            var weightedSum = 0.0;
            var totalWeight = 0.0;

            foreach (var (dimension, score) in dimensionScores)
            {
                var weight = _weights.GetWeight(dimension);
                weightedSum += score * weight;
                totalWeight += weight;
            }

            return totalWeight > 0 ? weightedSum / totalWeight : 0.0;
        }

        /// <summary>
        /// Calculate confidence level in the quality assessment
        /// </summary>
        private ConfidenceLevel CalculateConfidenceLevel(
            Dictionary<string, double> dimensionScores, 
            DerivationResult result)
        {
            // Factors affecting confidence
            var factors = new List<double>();

            // Score consistency across dimensions
            var scoreVariance = CalculateVariance(dimensionScores.Values);
            factors.Add(1.0 - Math.Min(1.0, scoreVariance * 2)); // Lower variance = higher confidence

            // Data sufficiency
            var dataSufficiency = Math.Min(1.0, result.DerivedCapabilities.Count / 5.0);
            factors.Add(dataSufficiency);

            // Input quality
            var inputQuality = string.IsNullOrWhiteSpace(result.SourceATPContent) ? 0.0 : 
                Math.Min(1.0, result.SourceATPContent.Length / 100.0);
            factors.Add(inputQuality);

            var avgConfidence = factors.Average();

            return avgConfidence >= 0.8 ? ConfidenceLevel.High :
                   avgConfidence >= 0.6 ? ConfidenceLevel.Medium :
                   ConfidenceLevel.Low;
        }

        /// <summary>
        /// Identify key improvement areas based on dimension scores
        /// </summary>
        private List<string> IdentifyImprovementAreas(Dictionary<string, double> dimensionScores)
        {
            return dimensionScores
                .Where(kvp => kvp.Value < 0.7)
                .OrderBy(kvp => kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// Generate actionable improvement recommendations
        /// </summary>
        private List<string> GenerateActionableRecommendations(
            Dictionary<string, double> dimensionScores, 
            DerivationResult result)
        {
            var recommendations = new List<string>();

            foreach (var (dimension, score) in dimensionScores.Where(kvp => kvp.Value < 0.7))
            {
                switch (dimension)
                {
                    case "TaxonomyCompliance":
                        recommendations.Add("Review taxonomy category assignments and ensure proper subcategory specificity");
                        break;
                    case "Completeness":
                        recommendations.Add("Analyze ATP step for additional system capabilities that may have been missed");
                        break;
                    case "Specificity":
                        recommendations.Add("Add specific measurements, criteria, and quantifiable requirements");
                        break;
                    case "AtpAlignment":
                        recommendations.Add("Ensure derived capabilities directly address ATP step requirements with clear rationale");
                        break;
                    case "Testability":
                        recommendations.Add("Define measurable pass/fail criteria and specific verification methods");
                        break;
                    case "Consistency":
                        recommendations.Add("Standardize terminology and abstraction level across all capabilities");
                        break;
                    case "Feasibility":
                        recommendations.Add("Review technical feasibility and resolve any conflicting requirements");
                        break;
                    case "SpecificationCompleteness":
                        recommendations.Add("Identify additional missing specifications for tolerances, timing, and accuracy");
                        break;
                }
            }

            return recommendations;
        }

        /// <summary>
        /// Assess performance relative to historical scores
        /// </summary>
        private async Task<RelativePerformance> AssessRelativePerformanceAsync(double currentScore)
        {
            if (!_historicalScores.Any())
                return new RelativePerformance { Percentile = 50, IsAboveAverage = null };

            var historicalOverallScores = _historicalScores.Select(h => h.OverallScore).ToList();
            var avgHistoricalScore = historicalOverallScores.Average();
            var percentile = CalculatePercentile(currentScore, historicalOverallScores);

            return new RelativePerformance
            {
                Percentile = percentile,
                IsAboveAverage = currentScore > avgHistoricalScore,
                HistoricalAverage = avgHistoricalScore,
                HistoricalCount = _historicalScores.Count
            };
        }

        /// <summary>
        /// Record quality score for trend analysis
        /// </summary>
        private void RecordQualityScore(DerivationQualityScore qualityScore, DerivationResult result)
        {
            var record = new QualityScoreRecord
            {
                ScoreId = qualityScore.ScoringId,
                ScoredAt = qualityScore.ScoredAt,
                OverallScore = qualityScore.OverallScore,
                CapabilityCount = result.DerivedCapabilities.Count,
                AtpLength = result.SourceATPContent?.Length ?? 0
            };

            _historicalScores.Add(record);

            // Keep only recent history to prevent memory growth
            if (_historicalScores.Count > 1000)
            {
                _historicalScores.RemoveRange(0, 100);
            }
        }

        #endregion

        #region Self-Evaluation Methods

        /// <summary>
        /// Analyze consistency of scoring across similar derivations
        /// </summary>
        private double AnalyzeScoringConsistency()
        {
            if (_historicalScores.Count < 10) return 0.5; // Insufficient data

            // Group by similar characteristics and check score variance
            var groups = _historicalScores
                .GroupBy(s => new { CapRange = s.CapabilityCount / 5 * 5, AtpRange = s.AtpLength / 100 * 100 })
                .Where(g => g.Count() >= 3);

            if (!groups.Any()) return 0.5;

            var consistencyScores = groups.Select(group =>
            {
                var scores = group.Select(s => s.OverallScore).ToList();
                var variance = CalculateVariance(scores);
                return 1.0 - Math.Min(1.0, variance * 10); // Lower variance = higher consistency
            });

            return consistencyScores.Average();
        }

        /// <summary>
        /// Calculate how well scores predict actual validation outcomes
        /// </summary>
        private async Task<double> CalculatePredictiveAccuracyAsync()
        {
            // This would require validation outcome data from Task 3.2
            // For now, return estimated accuracy based on score distribution
            if (_historicalScores.Count < 20) return 0.5;

            // Assume normal distribution should have most scores in middle range
            var middleRangeCount = _historicalScores.Count(s => s.OverallScore >= 0.4 && s.OverallScore <= 0.8);
            var middleRangeRatio = (double)middleRangeCount / _historicalScores.Count;

            // Good predictive accuracy should have 60-80% in middle range
            return middleRangeRatio >= 0.6 && middleRangeRatio <= 0.8 ? 0.8 : 0.6;
        }

        /// <summary>
        /// Detect systematic biases in scoring
        /// </summary>
        private BiasDetectionReport DetectScoringBias()
        {
            var report = new BiasDetectionReport();

            if (_historicalScores.Count < 30)
            {
                report.DetectedBiases.Add("Insufficient data for bias detection");
                return report;
            }

            // Check for score inflation bias
            var avgScore = _historicalScores.Average(s => s.OverallScore);
            if (avgScore > 0.8)
                report.DetectedBiases.Add("Potential score inflation - average score unusually high");
            else if (avgScore < 0.4)
                report.DetectedBiases.Add("Potential score deflation - average score unusually low");

            // Check for range restriction bias
            var scoreRange = _historicalScores.Max(s => s.OverallScore) - _historicalScores.Min(s => s.OverallScore);
            if (scoreRange < 0.3)
                report.DetectedBiases.Add("Range restriction - scores clustered too tightly");

            // Check for complexity bias
            var complexityCorrelation = CalculateCorrelation(
                _historicalScores.Select(s => (double)s.CapabilityCount).ToList(),
                _historicalScores.Select(s => s.OverallScore).ToList());
            
            if (Math.Abs(complexityCorrelation) > 0.7)
                report.DetectedBiases.Add($"Strong complexity bias detected (correlation: {complexityCorrelation:F2})");

            return report;
        }

        /// <summary>
        /// Calculate system performance metrics
        /// </summary>
        private SystemPerformanceMetrics CalculateSystemPerformanceMetrics()
        {
            return new SystemPerformanceMetrics
            {
                TotalScoringsPerformed = _historicalScores.Count,
                AverageProcessingTime = TimeSpan.FromMilliseconds(150), // Estimated
                ScoreDistribution = CalculateScoreDistribution(),
                MemoryUsage = GC.GetTotalMemory(false) / 1024 / 1024 // MB
            };
        }

        /// <summary>
        /// Assess how well scores are calibrated
        /// </summary>
        private double AssessScoreCalibration()
        {
            if (_historicalScores.Count < 20) return 0.5;

            // Check if score distribution follows expected patterns
            var scoreDistribution = CalculateScoreDistribution();
            
            // Good calibration should have reasonable spread across ranges
            var nonZeroRanges = scoreDistribution.Values.Count(v => v > 0);
            var distributionQuality = Math.Min(1.0, nonZeroRanges / 10.0);

            return distributionQuality;
        }

        /// <summary>
        /// Generate improvement recommendations for the scoring system
        /// </summary>
        private List<string> GenerateSystemImprovementRecommendations(SelfEvaluationReport report)
        {
            var recommendations = new List<string>();

            if (report.ScoringConsistency < 0.7)
                recommendations.Add("Improve scoring consistency by refining dimension weight calculations");

            if (report.PredictiveAccuracy < 0.7)
                recommendations.Add("Enhance predictive accuracy by incorporating more validation feedback data");

            if (report.BiasDetection.DetectedBiases.Any())
                recommendations.Add("Address detected scoring biases through calibration adjustments");

            if (report.CalibrationQuality < 0.6)
                recommendations.Add("Improve score calibration by analyzing more diverse derivation examples");

            return recommendations;
        }

        #endregion

        #region Real-Time Feedback Methods

        /// <summary>
        /// Calculate quick quality indicators for real-time feedback
        /// </summary>
        private async Task<Dictionary<string, double>> CalculateRealTimeIndicators(
            string atpStepText, 
            List<DerivedCapability> partialCapabilities)
        {
            var indicators = new Dictionary<string, double>();

            // Quick ATP quality check
            indicators["AtpQuality"] = ScoreAtpInputQuality(atpStepText);
            
            // Partial capability assessment
            if (partialCapabilities?.Any() == true)
            {
                indicators["PartialTaxonomyCompliance"] = await ScoreTaxonomyComplianceAsync(partialCapabilities);
                indicators["PartialSpecificity"] = ScoreSpecificityQuality(partialCapabilities);
            }

            return indicators;
        }

        /// <summary>
        /// Detect early quality issues during derivation
        /// </summary>
        private List<string> DetectEarlyQualityIssues(string atpStepText, List<DerivedCapability> partialCapabilities)
        {
            var warnings = new List<string>();

            // ATP input issues
            if (string.IsNullOrWhiteSpace(atpStepText))
                warnings.Add("ATP step text is empty or missing");
            else if (atpStepText.Length < 50)
                warnings.Add("ATP step text appears too brief for meaningful capability derivation");
            else if (ContainsVagueLanguage(atpStepText))
                warnings.Add("ATP step contains vague language that may lead to unclear capabilities");

            // Partial capability issues
            if (partialCapabilities?.Any() == true)
            {
                var duplicateCategories = partialCapabilities
                    .GroupBy(c => c.TaxonomyCategory)
                    .Where(g => g.Count() > 3)
                    .Select(g => g.Key);
                
                foreach (var category in duplicateCategories)
                    warnings.Add($"High concentration of capabilities in category {category} - consider variety");

                var vagueCapabilities = partialCapabilities.Count(c => ContainsVagueLanguage(c.RequirementText));
                if (vagueCapabilities > partialCapabilities.Count / 2)
                    warnings.Add("Many capabilities contain vague language - strive for specificity");
            }

            return warnings;
        }

        /// <summary>
        /// Suggest immediate improvements for in-progress derivation
        /// </summary>
        private List<string> SuggestImmediateImprovements(List<DerivedCapability> partialCapabilities)
        {
            var suggestions = new List<string>();

            if (partialCapabilities?.Any() != true)
            {
                suggestions.Add("Begin by identifying the primary system functions described in the ATP step");
                return suggestions;
            }

            // Check for missing critical areas
            var categories = partialCapabilities.Select(c => c.TaxonomyCategory).Distinct().ToList();
            
            if (!categories.Any(c => c.StartsWith("A")))
                suggestions.Add("Consider if any Analysis/Measurement capabilities are needed");
            if (!categories.Any(c => c.StartsWith("C")))
                suggestions.Add("Consider if Power/Electrical capabilities are relevant");
            if (!categories.Any(c => c.StartsWith("D")))
                suggestions.Add("Consider if Data Handling/Communication capabilities apply");

            // Check for missing specifications
            var capsWithFewSpecs = partialCapabilities.Count(c => c.MissingSpecifications.Count < 2);
            if (capsWithFewSpecs > partialCapabilities.Count / 2)
                suggestions.Add("Identify more missing specifications (tolerances, timing, accuracy requirements)");

            // Check rationale quality
            var capsWithoutRationale = partialCapabilities.Count(c => string.IsNullOrWhiteSpace(c.DerivationRationale));
            if (capsWithoutRationale > 0)
                suggestions.Add("Add clear rationale explaining why each capability is derived from the ATP step");

            return suggestions;
        }

        #endregion

        #region Utility Methods

        private double ScoreAtpInputQuality(string atpStepText)
        {
            if (string.IsNullOrWhiteSpace(atpStepText)) return 0.0;

            var score = 0.0;

            // Length scoring
            var length = atpStepText.Trim().Length;
            score += Math.Min(0.3, length / 200.0);

            // Technical content indicators
            if (Regex.IsMatch(atpStepText, @"\b\d+(\.\d+)?\s*(V|A|Hz|GHz|MHz|Ohm|dB)\b"))
                score += 0.2;

            // Action words
            if (Regex.IsMatch(atpStepText, @"\b(measure|test|verify|check|apply|connect|disconnect|monitor)\b", RegexOptions.IgnoreCase))
                score += 0.2;

            // Structure indicators
            if (atpStepText.Split('.').Length > 2)
                score += 0.1;

            // Specific vs vague language
            if (ContainsVagueLanguage(atpStepText))
                score -= 0.2;

            return Math.Max(0.0, Math.Min(1.0, score + 0.2));
        }

        private List<string> ExtractKeywords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();

            return Regex.Matches(text.ToLowerInvariant(), @"\b\w{4,}\b")
                .Cast<Match>()
                .Select(m => m.Value)
                .Where(w => !IsStopWord(w))
                .Distinct()
                .ToList();
        }

        private bool IsStopWord(string word)
        {
            var stopWords = new HashSet<string> { "that", "with", "from", "they", "been", "have", "were", "said", "each", "which", "their", "time", "will", "about", "would", "there", "could", "other", "more", "very", "what", "know", "just", "first", "into", "over", "think", "also", "your", "work", "life", "only", "than", "used", "years", "people", "made", "take", "make", "want", "does", "come", "where", "much", "like", "right", "still", "may" };
            return stopWords.Contains(word);
        }

        private bool ContainsVagueLanguage(string text)
        {
            var vaguePatterns = new[]
            {
                @"\b(appropriate|adequate|reasonable|suitable|proper|sufficient|necessary|required)\b",
                @"\b(good|bad|high|low|fast|slow|small|large|big)\s+(quality|performance|level)\b",
                @"\bas\s+(needed|required|appropriate|necessary)\b",
                @"\b(shall|should|may|might)\s+be\s+(able\s+to|capable\s+of)\b"
            };

            return vaguePatterns.Any(pattern => 
                Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase));
        }

        private bool ContainsSpecificMeasurements(string text)
        {
            return Regex.IsMatch(text, @"\b\d+(\.\d+)?\s*(V|A|Hz|GHz|MHz|Ohm|dB|sec|ms|μs|°C|%|ppm)\b");
        }

        private bool HasTestableCriteria(string text)
        {
            var testablePatterns = new[]
            {
                @"\b(±|tolerance|accuracy|precision)\s*\d+",
                @"\b(minimum|maximum|range|between)\s+\d+",
                @"\b(pass|fail|criteria|threshold|limit)\b",
                @"\b(measured|detected|indicated|displayed)\b"
            };

            return testablePatterns.Any(pattern =>
                Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase));
        }

        private bool ContainsMeasurableCriteria(string text)
        {
            return ContainsSpecificMeasurements(text) || HasTestableCriteria(text);
        }

        private bool ContainsPassFailCriteria(string text)
        {
            return Regex.IsMatch(text, @"\b(pass|fail|accept|reject|within|exceed|below|above)\b.*\d+", RegexOptions.IgnoreCase);
        }

        private bool ContainsUnrealisticPerformance(string text)
        {
            // Check for unrealistic values (this is a simplified implementation)
            var unrealisticPatterns = new[]
            {
                @"\b(100%|zero)\s+(error|loss|noise)\b",
                @"\b(infinite|unlimited|perfect)\b",
                @"\b\d+\s*(THz|PHz)\b" // Unrealistically high frequencies
            };

            return unrealisticPatterns.Any(pattern =>
                Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase));
        }

        private bool ContainsConflictingRequirements(string text)
        {
            // Simple conflict detection - this could be much more sophisticated
            return Regex.IsMatch(text, @"\b(both|simultaneously)\b.*\b(high|maximum|fast)\b.*\b(low|minimum|slow)\b", RegexOptions.IgnoreCase);
        }

        private bool IsSpecificSpecification(string specification)
        {
            return specification.Length > 5 && 
                   !specification.ToLower().Contains("tbd") &&
                   Regex.IsMatch(specification, @"\b\w{3,}\b.*\b\w{3,}\b"); // At least two meaningful words
        }

        private List<string> ExtractComplexityIndicators(string atpStep)
        {
            var indicators = new List<string>();

            if (Regex.IsMatch(atpStep, @"\b\d+(\.\d+)?\s*(V|A|Hz|GHz|MHz)\b"))
                indicators.Add("electrical_parameters");
            
            if (Regex.IsMatch(atpStep, @"\b(measure|test|verify|calibrate)\b", RegexOptions.IgnoreCase))
                indicators.Add("measurement_actions");
            
            if (Regex.IsMatch(atpStep, @"\b(connect|disconnect|interface|communication)\b", RegexOptions.IgnoreCase))
                indicators.Add("connectivity_actions");
            
            if (Regex.IsMatch(atpStep, @"\b(power|supply|voltage|current)\b", RegexOptions.IgnoreCase))
                indicators.Add("power_systems");
            
            if (Regex.IsMatch(atpStep, @"\b(data|signal|protocol|format)\b", RegexOptions.IgnoreCase))
                indicators.Add("data_handling");

            return indicators;
        }

        private int EstimateExpectedCapabilityCount(List<string> complexityIndicators)
        {
            // Base count plus one per complexity area
            return Math.Max(1, 1 + complexityIndicators.Count);
        }

        private int CountComplexityAreasCovered(List<DerivedCapability> capabilities, List<string> complexityIndicators)
        {
            var coveredAreas = 0;

            foreach (var indicator in complexityIndicators)
            {
                var hasCapabilityForArea = capabilities.Any(cap =>
                {
                    var text = (cap.RequirementText + " " + cap.DerivationRationale).ToLower();
                    return indicator switch
                    {
                        "electrical_parameters" => text.Contains("electrical") || text.Contains("voltage") || text.Contains("current"),
                        "measurement_actions" => text.Contains("measure") || text.Contains("test") || text.Contains("verify"),
                        "connectivity_actions" => text.Contains("connect") || text.Contains("interface") || text.Contains("communication"),
                        "power_systems" => text.Contains("power") || text.Contains("supply"),
                        "data_handling" => text.Contains("data") || text.Contains("signal") || text.Contains("protocol"),
                        _ => false
                    };
                });

                if (hasCapabilityForArea) coveredAreas++;
            }

            return coveredAreas;
        }

        private double AssessTerminologyConsistency(List<DerivedCapability> capabilities)
        {
            // Extract technical terms and check for consistent usage
            var allTerms = capabilities.SelectMany(c =>
                ExtractTechnicalTerms(c.RequirementText + " " + c.DerivationRationale)).ToList();

            if (!allTerms.Any()) return 1.0;

            // Check for synonyms or inconsistent terminology
            var termGroups = allTerms.GroupBy(t => t.ToLower()).Where(g => g.Count() > 1);
            var consistentTerms = termGroups.Count(g => g.All(t => t == g.First()));
            var totalTermGroups = termGroups.Count();

            return totalTermGroups == 0 ? 1.0 : (double)consistentTerms / totalTermGroups;
        }

        private double AssessSpecificationFormatConsistency(List<DerivedCapability> capabilities)
        {
            // Check consistency in how specifications are formatted
            var specFormats = capabilities.Select(c => ClassifySpecificationFormat(c.RequirementText)).ToList();
            var dominantFormat = specFormats.GroupBy(f => f).OrderByDescending(g => g.Count()).First().Key;
            var formatConsistency = (double)specFormats.Count(f => f == dominantFormat) / specFormats.Count;

            return formatConsistency;
        }

        private double AssessAbstractionLevelConsistency(List<DerivedCapability> capabilities)
        {
            // Check consistency in abstraction level (system vs component level)
            var abstractionLevels = capabilities.Select(c => ClassifyAbstractionLevel(c.RequirementText)).ToList();
            var levelVariance = CalculateVariance(abstractionLevels.Select(l => (double)l));
            
            return Math.Max(0.0, 1.0 - levelVariance);
        }

        private List<string> ExtractTechnicalTerms(string text)
        {
            // Extract capitalized technical terms and acronyms
            return Regex.Matches(text, @"\b[A-Z][A-Z0-9]{1,}|[A-Z][a-z]+(?:[A-Z][a-z]+)*\b")
                .Cast<Match>()
                .Select(m => m.Value)
                .Where(t => t.Length > 2)
                .Distinct()
                .ToList();
        }

        private string ClassifySpecificationFormat(string requirementText)
        {
            if (Regex.IsMatch(requirementText, @"shall\s+\w+"))
                return "shall_format";
            else if (Regex.IsMatch(requirementText, @"must\s+\w+"))
                return "must_format";
            else if (Regex.IsMatch(requirementText, @"will\s+\w+"))
                return "will_format";
            else if (requirementText.Contains(":"))
                return "colon_format";
            else
                return "other_format";
        }

        private int ClassifyAbstractionLevel(string requirementText)
        {
            // 1 = component level, 2 = subsystem level, 3 = system level
            if (Regex.IsMatch(requirementText, @"\b(component|part|element|device)\b", RegexOptions.IgnoreCase))
                return 1;
            else if (Regex.IsMatch(requirementText, @"\b(subsystem|module|unit)\b", RegexOptions.IgnoreCase))
                return 2;
            else if (Regex.IsMatch(requirementText, @"\b(system|platform|equipment)\b", RegexOptions.IgnoreCase))
                return 3;
            else
                return 2; // Default to subsystem level
        }

        private double CalculateVariance(IEnumerable<double> values)
        {
            var valuesList = values.ToList();
            if (valuesList.Count < 2) return 0.0;

            var mean = valuesList.Average();
            var sumSquaredDeviations = valuesList.Sum(v => Math.Pow(v - mean, 2));
            return sumSquaredDeviations / valuesList.Count;
        }

        private double CalculatePercentile(double value, List<double> distribution)
        {
            if (!distribution.Any()) return 50.0;

            var sortedDistribution = distribution.OrderBy(x => x).ToList();
            var count = 0;

            foreach (var distValue in sortedDistribution)
            {
                if (value > distValue) count++;
                else break;
            }

            return (double)count / sortedDistribution.Count * 100;
        }

        private double CalculateCorrelation(List<double> x, List<double> y)
        {
            if (x.Count != y.Count || x.Count < 2) return 0.0;

            var meanX = x.Average();
            var meanY = y.Average();

            var numerator = x.Zip(y, (xi, yi) => (xi - meanX) * (yi - meanY)).Sum();
            var denominatorX = Math.Sqrt(x.Sum(xi => Math.Pow(xi - meanX, 2)));
            var denominatorY = Math.Sqrt(y.Sum(yi => Math.Pow(yi - meanY, 2)));

            return denominatorX * denominatorY == 0 ? 0 : numerator / (denominatorX * denominatorY);
        }

        private Dictionary<string, int> CalculateScoreDistribution()
        {
            var ranges = new Dictionary<string, int>
            {
                ["0.0-0.1"] = 0, ["0.1-0.2"] = 0, ["0.2-0.3"] = 0, ["0.3-0.4"] = 0, ["0.4-0.5"] = 0,
                ["0.5-0.6"] = 0, ["0.6-0.7"] = 0, ["0.7-0.8"] = 0, ["0.8-0.9"] = 0, ["0.9-1.0"] = 0
            };

            foreach (var score in _historicalScores)
            {
                var bucket = Math.Floor(score.OverallScore * 10) / 10;
                var key = $"{bucket:F1}-{bucket + 0.1:F1}";
                if (ranges.ContainsKey(key))
                    ranges[key]++;
            }

            return ranges;
        }

        #endregion
    }
}