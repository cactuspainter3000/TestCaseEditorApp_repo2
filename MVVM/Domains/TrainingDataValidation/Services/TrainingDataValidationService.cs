using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services
{
    /// <summary>
    /// Implementation of training data validation service.
    /// Manages human validation workflows and quality assessment.
    /// </summary>
    public class TrainingDataValidationService : ITrainingDataValidationService
    {
        private readonly ILogger<TrainingDataValidationService> _logger;
        private readonly TaxonomyValidator _taxonomyValidator;
        private readonly string _validationDataPath;
        private readonly List<ValidationResult> _validationResults;
        private readonly Dictionary<string, ValidationSession> _activeSessions;

        public TrainingDataValidationService(
            ILogger<TrainingDataValidationService> logger,
            TaxonomyValidator taxonomyValidator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _taxonomyValidator = taxonomyValidator ?? throw new ArgumentNullException(nameof(taxonomyValidator));
            
            _validationDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "TestCaseEditorApp", "TrainingDataValidation");
            
            _validationResults = new List<ValidationResult>();
            _activeSessions = new Dictionary<string, ValidationSession>();
            
            EnsureDirectoryExists();
        }

        /// <summary>
        /// Records a human validation decision for a synthetic training example
        /// </summary>
        public async Task RecordValidationAsync(ValidationResult validationResult)
        {
            try
            {
                if (validationResult == null)
                    throw new ArgumentNullException(nameof(validationResult));

                // Add timestamp and metadata
                validationResult.ValidatedAt = DateTime.UtcNow;
                if (string.IsNullOrEmpty(validationResult.ValidatedBy))
                    validationResult.ValidatedBy = Environment.UserName;

                // Store in memory
                _validationResults.Add(validationResult);

                // Persist to file
                await PersistValidationResultAsync(validationResult);

                _logger.LogInformation("Recorded validation for example {ExampleId}: {Decision}", 
                    validationResult.ExampleId, validationResult.Decision);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record validation result for example {ExampleId}", 
                    validationResult.ExampleId);
                throw;
            }
        }

        /// <summary>
        /// Saves the current validation session state for resuming later
        /// </summary>
        public async Task SaveValidationSessionAsync(ValidationSession session)
        {
            try
            {
                if (session == null)
                    throw new ArgumentNullException(nameof(session));

                // Update session metadata
                session.SessionMetadata["SavedAt"] = DateTime.UtcNow;
                session.SessionMetadata["UserName"] = Environment.UserName;
                session.SessionMetadata["MachineName"] = Environment.MachineName;

                // Store in memory
                _activeSessions[session.Id] = session;

                // Serialize and save to file
                var sessionPath = Path.Combine(_validationDataPath, "sessions", $"{session.Id}.json");
                var sessionJson = JsonSerializer.Serialize(session, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await File.WriteAllTextAsync(sessionPath, sessionJson);

                _logger.LogInformation("Saved validation session {SessionId} with {PendingCount} pending examples", 
                    session.Id, session.PendingExamples.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save validation session {SessionId}", session.Id);
                throw;
            }
        }

        /// <summary>
        /// Loads a previously saved validation session
        /// </summary>
        public async Task<ValidationSession?> LoadValidationSessionAsync(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                    return null;

                // Check memory first
                if (_activeSessions.TryGetValue(sessionId, out var session))
                    return session;

                // Load from file
                var sessionPath = Path.Combine(_validationDataPath, "sessions", $"{sessionId}.json");
                if (!File.Exists(sessionPath))
                    return null;

                var sessionJson = await File.ReadAllTextAsync(sessionPath);
                var loadedSession = JsonSerializer.Deserialize<ValidationSession>(sessionJson, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (loadedSession != null)
                {
                    _activeSessions[sessionId] = loadedSession;
                    _logger.LogInformation("Loaded validation session {SessionId}", sessionId);
                }

                return loadedSession;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load validation session {SessionId}", sessionId);
                return null;
            }
        }

        /// <summary>
        /// Gets all validation sessions for the current user
        /// </summary>
        public async Task<List<ValidationSession>> GetUserValidationSessionsAsync()
        {
            try
            {
                var sessions = new List<ValidationSession>();
                var sessionsPath = Path.Combine(_validationDataPath, "sessions");
                
                if (!Directory.Exists(sessionsPath))
                    return sessions;

                var sessionFiles = Directory.GetFiles(sessionsPath, "*.json");
                
                foreach (var file in sessionFiles)
                {
                    try
                    {
                        var sessionJson = await File.ReadAllTextAsync(file);
                        var session = JsonSerializer.Deserialize<ValidationSession>(sessionJson, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });

                        if (session != null && 
                            session.SessionMetadata.TryGetValue("UserName", out var userName) &&
                            userName.ToString() == Environment.UserName)
                        {
                            sessions.Add(session);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load validation session from {File}", file);
                    }
                }

                return sessions.OrderByDescending(s => s.StartedAt).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user validation sessions");
                return new List<ValidationSession>();
            }
        }

        /// <summary>
        /// Exports validated training data to the specified format
        /// </summary>
        public async Task ExportTrainingDataAsync(List<SyntheticTrainingExample> approvedExamples, string outputPath)
        {
            try
            {
                if (approvedExamples == null)
                    throw new ArgumentNullException(nameof(approvedExamples));

                if (string.IsNullOrEmpty(outputPath))
                    throw new ArgumentNullException(nameof(outputPath));

                var exportData = new
                {
                    ExportedAt = DateTime.UtcNow,
                    ExportedBy = Environment.UserName,
                    TotalExamples = approvedExamples.Count,
                    Examples = approvedExamples.Select(e => new
                    {
                        e.ExampleId,
                        e.ATPStepText,
                        e.ExpectedCapability,
                        e.QualityScore,
                        e.GeneratedAt,
                        e.SourceCategory,
                        e.SourceSubcategory,
                        e.ValidationStatus,
                        Metadata = new
                        {
                            e.DomainContext,
                            e.GenerationMethod,
                            e.QualityScore
                        }
                    })
                };

                var exportJson = JsonSerializer.Serialize(exportData, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                await File.WriteAllTextAsync(outputPath, exportJson);

                _logger.LogInformation("Exported {Count} validated training examples to {OutputPath}", 
                    approvedExamples.Count, outputPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export training data to {OutputPath}", outputPath);
                throw;
            }
        }

        /// <summary>
        /// Gets validation statistics and metrics
        /// </summary>
        public async Task<ValidationMetrics> GetValidationMetricsAsync()
        {
            try
            {
                var metrics = new ValidationMetrics
                {
                    TotalExamplesValidated = _validationResults.Count
                };

                if (_validationResults.Any())
                {
                    metrics.ApprovedCount = _validationResults.Count(v => v.Decision == ValidationDecision.Approved);
                    metrics.RejectedCount = _validationResults.Count(v => v.Decision == ValidationDecision.Rejected);
                    metrics.RequiresEditsCount = _validationResults.Count(v => v.Decision == ValidationDecision.RequiresEdits);
                    metrics.SkippedCount = _validationResults.Count(v => v.Decision == ValidationDecision.Skipped);

                    // Calculate category breakdown
                    var categoryGroups = _validationResults
                        .Where(v => v.OriginalExample != null)
                        .GroupBy(v => v.OriginalExample.SourceCategory)
                        .ToDictionary(g => g.Key, g => g.Count());
                    
                    metrics.CategoryBreakdown = categoryGroups;
                    metrics.LastValidationDate = _validationResults.Max(v => v.ValidatedAt);
                    
                    // Calculate average validation time (simplified)
                    metrics.AverageValidationTime = TimeSpan.FromMinutes(2); // Placeholder
                }

                return metrics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to calculate validation metrics");
                throw;
            }
        }

        /// <summary>
        /// Analyzes validation patterns to identify improvement opportunities
        /// </summary>
        public async Task<ValidationAnalysis> AnalyzeValidationPatternsAsync()
        {
            try
            {
                var analysis = new ValidationAnalysis();

                if (!_validationResults.Any())
                    return analysis;

                // Analyze rejection reasons
                var rejectedResults = _validationResults.Where(v => v.Decision == ValidationDecision.Rejected);
                var commonReasons = rejectedResults
                    .GroupBy(v => v.Reason)
                    .OrderByDescending(g => g.Count())
                    .Take(5)
                    .Select(g => g.Key)
                    .ToList();
                
                analysis.CommonRejectionReasons = commonReasons;

                // Generate improvement suggestions
                analysis.QualityImprovementSuggestions = GenerateImprovementSuggestions(commonReasons);

                // Calculate category performance
                var categoryPerformance = _validationResults
                    .Where(v => v.OriginalExample != null)
                    .GroupBy(v => v.OriginalExample.SourceCategory)
                    .ToDictionary(
                        g => g.Key,
                        g => (double)g.Count(v => v.Decision == ValidationDecision.Approved) / g.Count()
                    );
                
                analysis.CategoryPerformance = categoryPerformance;

                // Placeholder for consistency score
                analysis.OverallValidationConsistency = 0.85;

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze validation patterns");
                throw;
            }
        }

        /// <summary>
        /// Validates the quality of a synthetic training example
        /// </summary>
        public async Task<QualityAssessment> AssessExampleQualityAsync(SyntheticTrainingExample example)
        {
            try
            {
                if (example == null)
                    throw new ArgumentNullException(nameof(example));

                var assessment = new QualityAssessment();
                var dimensionScores = new Dictionary<string, double>();

                // Assess ATP step quality
                var atpQuality = AssessATPStepQuality(example.ATPStepText);
                dimensionScores["ATPQuality"] = atpQuality;

                // Assess capability derivation quality using taxonomy validator
                var capabilityQuality = await AssessCapabilityQuality(example.ExpectedCapability);
                dimensionScores["CapabilityQuality"] = capabilityQuality;

                // Assess alignment between ATP and capability
                var alignmentScore = AssessATPCapabilityAlignment(example.ATPStepText, example.ExpectedCapability);
                dimensionScores["Alignment"] = alignmentScore;

                // Calculate overall score
                assessment.DimensionScores = dimensionScores;
                assessment.OverallScore = dimensionScores.Values.Average();
                assessment.MeetsThreshold = assessment.OverallScore >= 0.7;

                // Generate assessment text
                assessment.Assessment = GenerateQualityAssessmentText(assessment);

                // Identify strengths and improvement areas
                (assessment.StrengthAreas, assessment.ImprovementAreas) = IdentifyQualityAreas(dimensionScores);

                return assessment;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to assess example quality for {ExampleId}", example.ExampleId);
                throw;
            }
        }

        #region Private Helper Methods

        private void EnsureDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(_validationDataPath))
                    Directory.CreateDirectory(_validationDataPath);

                var sessionsPath = Path.Combine(_validationDataPath, "sessions");
                if (!Directory.Exists(sessionsPath))
                    Directory.CreateDirectory(sessionsPath);

                var validationsPath = Path.Combine(_validationDataPath, "validations");
                if (!Directory.Exists(validationsPath))
                    Directory.CreateDirectory(validationsPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create validation data directories");
                throw;
            }
        }

        private async Task PersistValidationResultAsync(ValidationResult validationResult)
        {
            try
            {
                var validationPath = Path.Combine(_validationDataPath, "validations", 
                    $"{validationResult.ExampleId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");

                var validationJson = JsonSerializer.Serialize(validationResult, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await File.WriteAllTextAsync(validationPath, validationJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist validation result for {ExampleId}", validationResult.ExampleId);
            }
        }

        private List<string> GenerateImprovementSuggestions(List<string> commonReasons)
        {
            var suggestions = new List<string>();

            if (commonReasons.Any(r => r.Contains("unclear") || r.Contains("ambiguous")))
            {
                suggestions.Add("Improve ATP step clarity and specificity");
            }

            if (commonReasons.Any(r => r.Contains("capability") || r.Contains("derivation")))
            {
                suggestions.Add("Enhance capability derivation accuracy");
            }

            if (commonReasons.Any(r => r.Contains("taxonomy") || r.Contains("category")))
            {
                suggestions.Add("Better taxonomy category alignment");
            }

            return suggestions;
        }

        private double AssessATPStepQuality(string atpStep)
        {
            if (string.IsNullOrWhiteSpace(atpStep))
                return 0.0;

            double score = 0.5; // Base score

            // Check for specificity
            if (atpStep.Contains("verify") || atpStep.Contains("test") || atpStep.Contains("measure"))
                score += 0.2;

            // Check for clear action words
            if (atpStep.Contains("shall") || atpStep.Contains("must") || atpStep.Contains("should"))
                score += 0.1;

            // Check for measurable criteria
            if (System.Text.RegularExpressions.Regex.IsMatch(atpStep, @"\d+"))
                score += 0.1;

            // Check length (not too short, not too long)
            if (atpStep.Length >= 20 && atpStep.Length <= 200)
                score += 0.1;

            return Math.Min(1.0, score);
        }

        private async Task<double> AssessCapabilityQuality(ExpectedCapabilityDerivation expectedCapability)
        {
            try
            {
                if (expectedCapability == null)
                    return 0.0;

                // Convert ExpectedCapabilityDerivation to DerivedCapability for validation
                var derivedCapability = new DerivedCapability
                {
                    RequirementText = expectedCapability.RequirementText,
                    TaxonomyCategory = expectedCapability.TaxonomyCategory,
                    TaxonomySubcategory = expectedCapability.TaxonomySubcategory,
                    DerivationRationale = expectedCapability.DerivationRationale ?? "",
                    MissingSpecifications = expectedCapability.MissingSpecifications ?? new List<string>(),
                    AllocationTargets = expectedCapability.AllocationTargets
                };

                // Use taxonomy validator for quality assessment
                var validationOptions = new TaxonomyValidationOptions
                {
                    RequireSpecificSubcategories = true,
                    ValidateExpectedCategories = false
                };

                var validationResults = _taxonomyValidator.ValidateSingleCapability(derivedCapability, validationOptions);
                
                if (!validationResults.Any())
                    return 0.9; // No issues found

                // Convert validation issues to quality score
                var errorCount = validationResults.Count(v => v.Severity == TaxonomyValidationSeverity.Error);
                var warningCount = validationResults.Count(v => v.Severity == TaxonomyValidationSeverity.Warning);

                var qualityScore = 1.0 - (errorCount * 0.3) - (warningCount * 0.1);
                return Math.Max(0.0, qualityScore);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to assess capability quality");
                return 0.5; // Default moderate score
            }
        }

        private double AssessATPCapabilityAlignment(string atpStep, ExpectedCapabilityDerivation capability)
        {
            if (string.IsNullOrWhiteSpace(atpStep) || capability == null)
                return 0.0;

            // Simple keyword overlap assessment
            var atpWords = atpStep.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var capabilityWords = (capability.RequirementText + " " + capability.DerivationRationale)
                .ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var commonWords = atpWords.Intersect(capabilityWords).Count();
            var totalWords = atpWords.Union(capabilityWords).Count();

            return totalWords > 0 ? (double)commonWords / totalWords : 0.0;
        }

        private string GenerateQualityAssessmentText(QualityAssessment assessment)
        {
            if (assessment.OverallScore >= 0.8)
                return "High quality training example with strong alignment and clarity";
            else if (assessment.OverallScore >= 0.6)
                return "Good quality example with minor areas for improvement";
            else if (assessment.OverallScore >= 0.4)
                return "Moderate quality example requiring significant refinement";
            else
                return "Low quality example not suitable for training without major revision";
        }

        private (List<string> strengths, List<string> improvements) IdentifyQualityAreas(Dictionary<string, double> scores)
        {
            var strengths = new List<string>();
            var improvements = new List<string>();

            foreach (var (dimension, score) in scores)
            {
                if (score >= 0.7)
                    strengths.Add(dimension);
                else if (score < 0.5)
                    improvements.Add(dimension);
            }

            return (strengths, improvements);
        }

        #endregion
    }
}