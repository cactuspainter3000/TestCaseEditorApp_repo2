using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Integration service that connects prompt refinement with quality scoring and validation systems.
    /// Implements automated feedback loops for continuous prompt improvement.
    /// </summary>
    public class PromptOptimizationIntegrationService : IPromptOptimizationIntegrationService
    {
        private readonly ILogger<PromptOptimizationIntegrationService> _logger;
        private readonly IPromptRefinementEngine _promptRefinementEngine;
        private readonly IDerivationQualityScorer _qualityScorer;
        private readonly ISystemCapabilityDerivationService _derivationService;
        private readonly ITrainingDataValidationService _validationService;

        // Configuration
        private readonly Dictionary<string, string> _activePromptMappings = new();
        private readonly List<OptimizationFeedbackAutomation> _automations = new();

        public PromptOptimizationIntegrationService(
            ILogger<PromptOptimizationIntegrationService> logger,
            IPromptRefinementEngine promptRefinementEngine,
            IDerivationQualityScorer qualityScorer,
            ISystemCapabilityDerivationService derivationService,
            ITrainingDataValidationService validationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _promptRefinementEngine = promptRefinementEngine ?? throw new ArgumentNullException(nameof(promptRefinementEngine));
            _qualityScorer = qualityScorer ?? throw new ArgumentNullException(nameof(qualityScorer));
            _derivationService = derivationService ?? throw new ArgumentNullException(nameof(derivationService));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));

            InitializePromptMappings();
            SetupAutomationRules();
        }

        /// <summary>
        /// Performs derivation with integrated prompt optimization tracking
        /// </summary>
        public async Task<OptimizedDerivationResult> PerformOptimizedDerivationAsync(
            string atpStepText,
            OptimizationOptions? options = null)
        {
            var sessionId = Guid.NewGuid().ToString();
            _logger.LogInformation("Starting optimized derivation session {SessionId}", sessionId);

            options ??= new OptimizationOptions();

            try
            {
                var result = new OptimizedDerivationResult
                {
                    SessionId = sessionId,
                    StartedAt = DateTime.UtcNow,
                    AtpStepText = atpStepText
                };

                // Step 1: Select optimal prompt for this derivation
                var selectedPrompt = await SelectOptimalPromptAsync(atpStepText, options);
                result.SelectedPromptId = selectedPrompt.PromptId;
                result.PromptSelectionRationale = $"Selected {selectedPrompt.Name} based on performance metrics";

                // Step 2: Perform derivation using selected prompt
                var derivationStartTime = DateTime.UtcNow;
                
                // TODO: Integrate with actual derivation service using the selected prompt
                var derivationResult = await _derivationService.DeriveSingleStepAsync(atpStepText);
                
                var derivationProcessingTime = DateTime.UtcNow - derivationStartTime;
                result.DerivationResult = derivationResult;

                // Step 3: Score quality of derivation
                var qualityScore = await _qualityScorer.ScoreDerivationQualityAsync(
                    derivationResult, 
                    atpStepText, 
                    new QualityScoringOptions());
                
                result.QualityScore = qualityScore;

                // Step 4: Record prompt usage for learning
                await _promptRefinementEngine.RecordPromptUsageAsync(
                    selectedPrompt.PromptId,
                    qualityScore.OverallScore,
                    derivationProcessingTime,
                    contextMetadata: new Dictionary<string, object>
                    {
                        ["atp_step_length"] = atpStepText.Length,
                        ["session_id"] = sessionId,
                        ["capability_count"] = derivationResult.DerivedCapabilities.Count,
                        ["quality_dimensions"] = qualityScore.DimensionScores
                    });

                // Step 5: Check if this derivation should trigger optimization
                await CheckAndTriggerOptimizationAsync(selectedPrompt, qualityScore, options);

                result.CompletedAt = DateTime.UtcNow;
                result.WasSuccessful = true;

                _logger.LogInformation("Completed optimized derivation session {SessionId} with quality score {QualityScore}", 
                    sessionId, qualityScore.OverallScore);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in optimized derivation session {SessionId}", sessionId);
                throw;
            }
        }

        /// <summary>
        /// Integrates validation feedback into prompt optimization loop
        /// </summary>
        public async Task ProcessValidationFeedbackAsync(
            string derivationSessionId,
            TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult validationResult)
        {
            _logger.LogInformation("Processing validation feedback for session {SessionId}", derivationSessionId);

            try
            {
                // Find the prompt that was used for this derivation
                // In a real implementation, this would be stored in a database
                var promptId = await GetPromptIdForSessionAsync(derivationSessionId);
                
                if (string.IsNullOrEmpty(promptId))
                {
                    _logger.LogWarning("Could not find prompt ID for session {SessionId}", derivationSessionId);
                    return;
                }

                // Record validation outcome for the prompt
                await _promptRefinementEngine.RecordPromptUsageAsync(
                    promptId,
                    qualityScore: ConvertValidationToQualityScore(validationResult.Decision),
                    processingTime: TimeSpan.Zero, // Not relevant for validation feedback
                    validationApproved: validationResult.Decision == TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationDecision.Approved,
                    contextMetadata: new Dictionary<string, object>
                    {
                        ["validation_session_id"] = derivationSessionId,
                        ["validation_decision"] = validationResult.Decision.ToString(),
                        ["validation_reason"] = validationResult.Reason,
                        ["confidence_score"] = validationResult.ConfidenceScore
                    });

                // Check if validation feedback triggers refinement
                await CheckValidationTrigger(promptId, validationResult);

                _logger.LogDebug("Processed validation feedback for prompt {PromptId}", promptId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing validation feedback for session {SessionId}", derivationSessionId);
            }
        }

        /// <summary>
        /// Runs automated optimization checks across all active prompts
        /// </summary>
        public async Task<OptimizationSweepResult> RunOptimizationSweepAsync(OptimizationSweepOptions? options = null)
        {
            _logger.LogInformation("Starting automated optimization sweep");

            options ??= new OptimizationSweepOptions();

            var sweepResult = new OptimizationSweepResult
            {
                SweepId = Guid.NewGuid().ToString(),
                StartedAt = DateTime.UtcNow
            };

            try
            {
                // Step 1: Identify prompts needing attention
                var promptsNeedingAttention = await _promptRefinementEngine.IdentifyPromptsNeedingRefinementAsync(
                    new PromptRefinementOptions
                    {
                        PerformanceThreshold = options.PerformanceThreshold,
                        MinSampleSize = options.MinSampleSize,
                        AnalysisWindow = options.AnalysisWindow
                    });

                sweepResult.PromptsAnalyzed = promptsNeedingAttention.Count;

                // Step 2: Process each identified prompt
                foreach (var analysis in promptsNeedingAttention)
                {
                    _logger.LogDebug("Processing optimization for prompt {PromptId}", analysis.AnalyzedPrompt.PromptId);

                    var optimizationResult = await ProcessPromptOptimizationAsync(analysis, options);
                    sweepResult.OptimizationResults.Add(optimizationResult);

                    if (optimizationResult.WasSuccessful)
                    {
                        sweepResult.PromptsOptimized++;
                        
                        if (optimizationResult.ABTestStarted)
                            sweepResult.ABTestsStarted++;
                    }
                }

                // Step 3: Check A/B test results
                await ProcessABTestResultsAsync(sweepResult, options);

                sweepResult.CompletedAt = DateTime.UtcNow;
                sweepResult.WasSuccessful = true;

                _logger.LogInformation("Completed optimization sweep {SweepId}: {PromptsOptimized}/{PromptsAnalyzed} prompts optimized, {ABTestsStarted} A/B tests started", 
                    sweepResult.SweepId, sweepResult.PromptsOptimized, sweepResult.PromptsAnalyzed, sweepResult.ABTestsStarted);

                return sweepResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in optimization sweep {SweepId}", sweepResult.SweepId);
                sweepResult.WasSuccessful = false;
                sweepResult.ErrorMessage = ex.Message;
                sweepResult.CompletedAt = DateTime.UtcNow;
                return sweepResult;
            }
        }

        /// <summary>
        /// Gets optimization status and recommendations for the system
        /// </summary>
        public async Task<OptimizationStatus> GetOptimizationStatusAsync()
        {
            var status = new OptimizationStatus
            {
                StatusCapturedAt = DateTime.UtcNow
            };

            try
            {
                // Get all managed prompts
                var allPrompts = await _promptRefinementEngine.GetAllManagedPromptsAsync();
                status.TotalActivePrompts = allPrompts.Count(p => p.Status == PromptStatus.Active);
                status.TotalTestingPrompts = allPrompts.Count(p => p.Status == PromptStatus.Testing);

                // Get performance monitoring data
                var monitor = await _promptRefinementEngine.GetPerformanceMonitorAsync();
                status.ActivePerformanceMetrics = monitor.CurrentPerformance;

                // Calculate system-wide metrics
                var activeMetrics = monitor.CurrentPerformance.Values.Where(m => m.SampleSize > 0).ToList();
                if (activeMetrics.Any())
                {
                    status.SystemWideAverageQuality = activeMetrics.Average(m => m.AverageQualityScore);
                    status.SystemWideSuccessRate = activeMetrics.Average(m => m.SuccessRate);
                    status.SystemWideValidationApprovalRate = activeMetrics.Average(m => m.ValidationApprovalRate);
                }

                // Identify optimization opportunities
                var needingAttention = await _promptRefinementEngine.IdentifyPromptsNeedingRefinementAsync();
                status.PromptsNeedingAttention = needingAttention.Count;

                // Generate recommendations
                status.Recommendations = GenerateSystemRecommendations(allPrompts, needingAttention, monitor);

                status.HealthStatus = CalculateSystemHealth(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting optimization status");
                status.HealthStatus = OptimizationHealth.Error;
                status.Recommendations.Add($"Error retrieving optimization status: {ex.Message}");
            }

            return status;
        }

        #region Private Helper Methods

        /// <summary>
        /// Initializes mappings between derivation contexts and optimal prompts
        /// </summary>
        private void InitializePromptMappings()
        {
            // Map default derivation operations to prompts
            _activePromptMappings["default-capability-derivation"] = "capability-derivation-v1";
            
            _logger.LogDebug("Initialized {MappingCount} prompt mappings", _activePromptMappings.Count);
        }

        /// <summary>
        /// Sets up automated optimization rules
        /// </summary>
        private void SetupAutomationRules()
        {
            _automations.Add(new OptimizationFeedbackAutomation
            {
                Name = "Low Quality Score Trigger",
                TriggerCondition = (promptId, metrics) => metrics.AverageQualityScore < 0.6,
                Action = OptimizationAction.TriggerRefinement,
                MinSampleSize = 10
            });

            _automations.Add(new OptimizationFeedbackAutomation
            {
                Name = "High Validation Rejection Rate",
                TriggerCondition = (promptId, metrics) => metrics.ValidationApprovalRate < 0.5,
                Action = OptimizationAction.TriggerRefinement,
                MinSampleSize = 5
            });

            _automations.Add(new OptimizationFeedbackAutomation
            {
                Name = "Excellent Performance Promotion",
                TriggerCondition = (promptId, metrics) => metrics.AverageQualityScore > 0.9 && metrics.ValidationApprovalRate > 0.95,
                Action = OptimizationAction.PromoteToProduction,
                MinSampleSize = 20
            });

            _logger.LogDebug("Set up {AutomationCount} optimization automation rules", _automations.Count);
        }

        /// <summary>
        /// Selects the optimal prompt for a given derivation context
        /// </summary>
        private async Task<ManagedPrompt> SelectOptimalPromptAsync(string atpStepText, OptimizationOptions options)
        {
            // Get all active derivation prompts
            var allPrompts = await _promptRefinementEngine.GetAllManagedPromptsAsync();
            var derivationPrompts = allPrompts
                .Where(p => p.Categories.Contains("capability-derivation") && 
                           (p.Status == PromptStatus.Active || p.Status == PromptStatus.Testing))
                .ToList();

            if (!derivationPrompts.Any())
            {
                throw new InvalidOperationException("No active derivation prompts available");
            }

            // If only one prompt, use it
            if (derivationPrompts.Count == 1)
            {
                return derivationPrompts.First();
            }

            // For A/B testing scenarios, select based on traffic distribution
            var testingPrompts = derivationPrompts.Where(p => p.ABTestConfig.IsActive).ToList();
            if (testingPrompts.Any())
            {
                return SelectFromABTest(testingPrompts);
            }

            // Otherwise, select based on performance metrics
            var bestPrompt = derivationPrompts
                .OrderByDescending(p => p.Performance.AverageQualityScore)
                .ThenByDescending(p => p.Performance.SuccessRate)
                .First();

            return bestPrompt;
        }

        /// <summary>
        /// Selects a prompt from active A/B test based on traffic distribution
        /// </summary>
        private ManagedPrompt SelectFromABTest(List<ManagedPrompt> testingPrompts)
        {
            var random = new Random();
            var randomValue = random.NextDouble();
            var cumulativePercentage = 0.0;

            foreach (var prompt in testingPrompts)
            {
                cumulativePercentage += prompt.ABTestConfig.TrafficPercentage;
                if (randomValue <= cumulativePercentage)
                {
                    return prompt;
                }
            }

            // Fallback to first prompt if distribution logic fails
            return testingPrompts.First();
        }

        /// <summary>
        /// Checks if conditions are met to trigger optimization for a prompt
        /// </summary>
        private async Task CheckAndTriggerOptimizationAsync(
            ManagedPrompt prompt, 
            DerivationQualityScore qualityScore, 
            OptimizationOptions options)
        {
            // Check each automation rule
            foreach (var automation in _automations)
            {
                if (prompt.Performance.UsageCount < automation.MinSampleSize)
                    continue;

                if (automation.TriggerCondition(prompt.PromptId, prompt.Performance))
                {
                    _logger.LogInformation("Automation rule '{RuleName}' triggered for prompt {PromptId}", 
                        automation.Name, prompt.PromptId);

                    await ExecuteOptimizationActionAsync(automation.Action, prompt, options);
                }
            }
        }

        /// <summary>
        /// Executes an optimization action
        /// </summary>
        private async Task ExecuteOptimizationActionAsync(
            OptimizationAction action, 
            ManagedPrompt prompt, 
            OptimizationOptions options)
        {
            switch (action)
            {
                case OptimizationAction.TriggerRefinement:
                    var refinementOptions = new PromptRefinementOptions
                    {
                        EnableABTesting = options.EnableABTesting,
                        ABTestDuration = options.ABTestDuration
                    };
                    await _promptRefinementEngine.RefinePromptAsync(prompt.PromptId, refinementOptions);
                    break;

                case OptimizationAction.PromoteToProduction:
                    prompt.Status = PromptStatus.Active;
                    await _promptRefinementEngine.SaveManagedPromptAsync(prompt);
                    break;

                case OptimizationAction.ArchivePrompt:
                    await _promptRefinementEngine.ArchivePromptAsync(prompt.PromptId, "Automated: Poor performance");
                    break;
            }
        }

        /// <summary>
        /// Checks if validation feedback should trigger refinement
        /// </summary>
        private async Task CheckValidationTrigger(string promptId, TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult validationResult)
        {
            if (validationResult.Decision == TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationDecision.Rejected)
            {
                var prompt = await _promptRefinementEngine.GetManagedPromptAsync(promptId);
                if (prompt?.Performance.ValidationApprovalRate < 0.6)
                {
                    _logger.LogInformation("Validation rejection rate triggered refinement for prompt {PromptId}", promptId);
                    await _promptRefinementEngine.RefinePromptAsync(promptId);
                }
            }
        }

        /// <summary>
        /// Processes optimization for a specific prompt
        /// </summary>
        private async Task<PromptOptimizationResult> ProcessPromptOptimizationAsync(
            PromptAnalysisResult analysis, 
            OptimizationSweepOptions options)
        {
            var optimizationResult = new PromptOptimizationResult
            {
                PromptId = analysis.AnalyzedPrompt.PromptId,
                OptimizedAt = DateTime.UtcNow
            };

            try
            {
                var refinementOptions = new PromptRefinementOptions
                {
                    EnableABTesting = options.EnableABTesting,
                    ABTestDuration = options.ABTestDuration,
                    AutoApplyRefinements = options.AutoApplyRefinements
                };

                var refinementResult = await _promptRefinementEngine.RefinePromptAsync(
                    analysis.AnalyzedPrompt.PromptId, 
                    refinementOptions);

                optimizationResult.WasSuccessful = refinementResult.WasSuccessful;
                optimizationResult.VariantsGenerated = refinementResult.RefinedPrompts.Count;
                optimizationResult.ABTestStarted = refinementResult.ABTestConfig.IsActive;

                if (!refinementResult.WasSuccessful)
                {
                    optimizationResult.ErrorMessage = refinementResult.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                optimizationResult.WasSuccessful = false;
                optimizationResult.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error optimizing prompt {PromptId}", analysis.AnalyzedPrompt.PromptId);
            }

            return optimizationResult;
        }

        /// <summary>
        /// Processes A/B test results and promotes winners
        /// </summary>
        private async Task ProcessABTestResultsAsync(OptimizationSweepResult sweepResult, OptimizationSweepOptions options)
        {
            // Get all active A/B tests
            var allPrompts = await _promptRefinementEngine.GetAllManagedPromptsAsync();
            var activeTests = allPrompts
                .Where(p => p.ABTestConfig.IsActive && DateTime.UtcNow > p.ABTestConfig.TestEndDate)
                .GroupBy(p => p.ABTestConfig.TestGroup)
                .ToList();

            foreach (var testGroup in activeTests)
            {
                try
                {
                    var promptIds = testGroup.Select(p => p.PromptId).ToList();
                    var comparison = await _promptRefinementEngine.ComparePromptPerformanceAsync(
                        promptIds, 
                        DateTime.UtcNow - testGroup.First().ABTestConfig.TestStartDate);

                    if (!string.IsNullOrEmpty(comparison.WinningPromptId))
                    {
                        // Promote winner and archive others
                        foreach (var prompt in testGroup)
                        {
                            if (prompt.PromptId == comparison.WinningPromptId)
                            {
                                prompt.Status = PromptStatus.Active;
                                prompt.ABTestConfig.IsActive = false;
                            }
                            else
                            {
                                await _promptRefinementEngine.ArchivePromptAsync(
                                    prompt.PromptId, 
                                    $"Lost A/B test to {comparison.WinningPromptId}");
                            }
                            
                            await _promptRefinementEngine.SaveManagedPromptAsync(prompt);
                        }

                        sweepResult.ABTestsCompleted++;
                        _logger.LogInformation("A/B test completed for group {TestGroup}: winner is {WinnerPromptId}", 
                            testGroup.Key, comparison.WinningPromptId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing A/B test results for group {TestGroup}", testGroup.Key);
                }
            }
        }

        /// <summary>
        /// Helper methods for system analysis
        /// </summary>
        private double ConvertValidationToQualityScore(ValidationDecision decision)
        {
            return decision switch
            {
                ValidationDecision.Approved => 1.0,
                ValidationDecision.RequiresEdits => 0.5,
                ValidationDecision.Rejected => 0.0,
                ValidationDecision.Flagged => 0.3,
                ValidationDecision.Skipped => 0.7, // Neutral
                _ => 0.5
            };
        }

        private List<string> GenerateSystemRecommendations(
            List<ManagedPrompt> allPrompts, 
            List<PromptAnalysisResult> needingAttention, 
            PromptPerformanceMonitor monitor)
        {
            var recommendations = new List<string>();

            if (!allPrompts.Any(p => p.Status == PromptStatus.Active))
            {
                recommendations.Add("CRITICAL: No active prompts found - system cannot function");
            }

            if (needingAttention.Count > allPrompts.Count * 0.5)
            {
                recommendations.Add($"HIGH: {needingAttention.Count} prompts need attention - consider system-wide review");
            }

            var avgQuality = monitor.CurrentPerformance.Values.Where(v => v.SampleSize > 0).Average(v => v.AverageQualityScore);
            if (avgQuality < 0.6)
            {
                recommendations.Add($"MEDIUM: System-wide quality is low ({avgQuality:P0}) - review baseline prompts");
            }

            if (!allPrompts.Any(p => p.Status == PromptStatus.Testing))
            {
                recommendations.Add("LOW: No A/B tests running - consider enabling continuous optimization");
            }

            return recommendations;
        }

        private OptimizationHealth CalculateSystemHealth(OptimizationStatus status)
        {
            var healthScore = 1.0;

            // Deduct for low system performance
            if (status.SystemWideAverageQuality < 0.6) healthScore -= 0.4;
            else if (status.SystemWideAverageQuality < 0.8) healthScore -= 0.2;

            // Deduct for high number of prompts needing attention
            var attentionRatio = status.PromptsNeedingAttention / (double)Math.Max(status.TotalActivePrompts, 1);
            if (attentionRatio > 0.5) healthScore -= 0.3;
            else if (attentionRatio > 0.25) healthScore -= 0.1;

            // Deduct for no active prompts
            if (status.TotalActivePrompts == 0) healthScore = 0.0;

            return healthScore switch
            {
                >= 0.8 => OptimizationHealth.Excellent,
                >= 0.6 => OptimizationHealth.Good,
                >= 0.4 => OptimizationHealth.Fair,
                >= 0.2 => OptimizationHealth.Poor,
                _ => OptimizationHealth.Critical
            };
        }

        private async Task<string> GetPromptIdForSessionAsync(string sessionId)
        {
            // In a real implementation, this would query a database
            // For now, return a default prompt ID
            return "capability-derivation-v1";
        }

        #endregion
    }

    #region Supporting Models and Interfaces

    /// <summary>
    /// Interface for the optimization integration service
    /// </summary>
    public interface IPromptOptimizationIntegrationService
    {
        Task<OptimizedDerivationResult> PerformOptimizedDerivationAsync(string atpStepText, OptimizationOptions? options = null);
        Task ProcessValidationFeedbackAsync(string derivationSessionId, TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult validationResult);
        Task<OptimizationSweepResult> RunOptimizationSweepAsync(OptimizationSweepOptions? options = null);
        Task<OptimizationStatus> GetOptimizationStatusAsync();
    }

    /// <summary>
    /// Result of an optimized derivation operation
    /// </summary>
    public class OptimizedDerivationResult
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public string AtpStepText { get; set; } = string.Empty;
        public string SelectedPromptId { get; set; } = string.Empty;
        public string PromptSelectionRationale { get; set; } = string.Empty;
        public DerivationResult DerivationResult { get; set; } = new DerivationResult();
        public DerivationQualityScore QualityScore { get; set; } = new DerivationQualityScore();
        public bool WasSuccessful { get; set; }
    }

    /// <summary>
    /// Options for optimization operations
    /// </summary>
    public class OptimizationOptions
    {
        public bool EnableABTesting { get; set; } = true;
        public TimeSpan ABTestDuration { get; set; } = TimeSpan.FromDays(7);
        public double PerformanceThreshold { get; set; } = 0.7;
    }

    /// <summary>
    /// Options for optimization sweeps
    /// </summary>
    public class OptimizationSweepOptions : OptimizationOptions
    {
        public int MinSampleSize { get; set; } = 10;
        public TimeSpan AnalysisWindow { get; set; } = TimeSpan.FromDays(30);
        public bool AutoApplyRefinements { get; set; } = false;
    }

    /// <summary>
    /// Result of an optimization sweep
    /// </summary>
    public class OptimizationSweepResult
    {
        public string SweepId { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public int PromptsAnalyzed { get; set; }
        public int PromptsOptimized { get; set; }
        public int ABTestsStarted { get; set; }
        public int ABTestsCompleted { get; set; }
        public bool WasSuccessful { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<PromptOptimizationResult> OptimizationResults { get; set; } = new List<PromptOptimizationResult>();
    }

    /// <summary>
    /// Result of optimizing a single prompt
    /// </summary>
    public class PromptOptimizationResult
    {
        public string PromptId { get; set; } = string.Empty;
        public DateTime OptimizedAt { get; set; }
        public bool WasSuccessful { get; set; }
        public int VariantsGenerated { get; set; }
        public bool ABTestStarted { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Overall optimization status for the system
    /// </summary>
    public class OptimizationStatus
    {
        public DateTime StatusCapturedAt { get; set; }
        public int TotalActivePrompts { get; set; }
        public int TotalTestingPrompts { get; set; }
        public int PromptsNeedingAttention { get; set; }
        public double SystemWideAverageQuality { get; set; }
        public double SystemWideSuccessRate { get; set; }
        public double SystemWideValidationApprovalRate { get; set; }
        public OptimizationHealth HealthStatus { get; set; }
        public Dictionary<string, PromptPerformanceSnapshot> ActivePerformanceMetrics { get; set; } = new Dictionary<string, PromptPerformanceSnapshot>();
        public List<string> Recommendations { get; set; } = new List<string>();
    }

    /// <summary>
    /// System health levels
    /// </summary>
    public enum OptimizationHealth
    {
        Critical,
        Poor,
        Fair,
        Good,
        Excellent,
        Error
    }

    /// <summary>
    /// Automation rule for optimization feedback
    /// </summary>
    internal class OptimizationFeedbackAutomation
    {
        public string Name { get; set; } = string.Empty;
        public Func<string, PromptPerformanceMetrics, bool> TriggerCondition { get; set; } = (_, _) => false;
        public OptimizationAction Action { get; set; }
        public int MinSampleSize { get; set; } = 10;
    }

    /// <summary>
    /// Actions that can be taken automatically
    /// </summary>
    public enum OptimizationAction
    {
        TriggerRefinement,
        PromoteToProduction,
        ArchivePrompt,
        StartABTest
    }

    #endregion
}