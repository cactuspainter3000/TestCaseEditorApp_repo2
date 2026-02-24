using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Intelligent prompt refinement and optimization engine.
    /// Analyzes prompt performance, generates improvements, and manages A/B testing.
    /// </summary>
    public class PromptRefinementEngine : IPromptRefinementEngine
    {
        private readonly ILogger<PromptRefinementEngine> _logger;
        private readonly IDerivationQualityScorer _qualityScorer;
        private readonly ITrainingDataValidationService _validationService;
        private readonly IAnythingLLMService _llmService;
        
        // In-memory storage for prompts and performance data
        // In production, this would be backed by a database
        private readonly Dictionary<string, ManagedPrompt> _managedPrompts = new();
        private readonly List<PromptUsageRecord> _usageHistory = new();
        private readonly Dictionary<string, ABTestSession> _activeABTests = new();

        public PromptRefinementEngine(
            ILogger<PromptRefinementEngine> logger,
            IDerivationQualityScorer qualityScorer,
            ITrainingDataValidationService validationService,
            IAnythingLLMService llmService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _qualityScorer = qualityScorer ?? throw new ArgumentNullException(nameof(qualityScorer));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));

            // Initialize with some default prompts for capability derivation
            InitializeDefaultPrompts();
        }

        /// <summary>
        /// Analyzes the performance of a managed prompt over a specified time window
        /// </summary>
        public async Task<PromptAnalysisResult> AnalyzePromptPerformanceAsync(
            string promptId, 
            TimeSpan? analysisWindow = null)
        {
            _logger.LogInformation("Starting performance analysis for prompt {PromptId}", promptId);

            try
            {
                var prompt = await GetManagedPromptAsync(promptId);
                if (prompt == null)
                {
                    throw new ArgumentException($"Prompt {promptId} not found", nameof(promptId));
                }

                var window = analysisWindow ?? TimeSpan.FromDays(30);
                var cutoffDate = DateTime.UtcNow - window;

                // Get usage records within the analysis window
                var usageRecords = _usageHistory
                    .Where(r => r.PromptId == promptId && r.UsedAt >= cutoffDate)
                    .OrderBy(r => r.UsedAt)
                    .ToList();

                if (!usageRecords.Any())
                {
                    _logger.LogWarning("No usage data found for prompt {PromptId} in the analysis window", promptId);
                    return new PromptAnalysisResult
                    {
                        AnalysisId = Guid.NewGuid().ToString(),
                        AnalyzedAt = DateTime.UtcNow,
                        AnalyzedPrompt = prompt,
                        AnalysisPeriod = window,
                        AnalysisConfidence = 0.0,
                        RecommendedActions = new List<string> { "Insufficient usage data for analysis" }
                    };
                }

                var analysisResult = new PromptAnalysisResult
                {
                    AnalysisId = Guid.NewGuid().ToString(),
                    AnalyzedAt = DateTime.UtcNow,
                    AnalyzedPrompt = prompt,
                    AnalysisPeriod = window
                };

                // Analyze performance metrics
                var avgQualityScore = usageRecords.Average(r => r.QualityScore);
                var successRate = usageRecords.Count(r => r.QualityScore >= 0.7) / (double)usageRecords.Count;
                var avgProcessingTime = TimeSpan.FromMilliseconds(usageRecords.Average(r => r.ProcessingTime.TotalMilliseconds));
                var validationApprovalRate = usageRecords
                    .Where(r => r.ValidationApproved.HasValue)
                    .Count(r => r.ValidationApproved.Value) / (double)usageRecords.Count(r => r.ValidationApproved.HasValue);

                // Update prompt performance metrics
                prompt.Performance = new PromptPerformanceMetrics
                {
                    UsageCount = usageRecords.Count,
                    AverageQualityScore = avgQualityScore,
                    SuccessRate = successRate,
                    AverageProcessingTime = avgProcessingTime,
                    ValidationApprovalRate = validationApprovalRate,
                    LastUpdated = DateTime.UtcNow
                };

                // Identify issues
                analysisResult.IdentifiedIssues = await IdentifyPromptIssuesAsync(prompt, usageRecords);

                // Generate improvement suggestions
                analysisResult.SuggestedImprovements = await GenerateImprovementSuggestionsAsync(prompt, analysisResult.IdentifiedIssues);

                // Calculate analysis confidence based on sample size and data consistency
                analysisResult.AnalysisConfidence = CalculateAnalysisConfidence(usageRecords);

                // Generate recommended actions
                analysisResult.RecommendedActions = GenerateRecommendedActions(analysisResult.IdentifiedIssues, analysisResult.SuggestedImprovements);

                _logger.LogInformation("Completed performance analysis for prompt {PromptId} with {IssueCount} issues identified", 
                    promptId, analysisResult.IdentifiedIssues.Count);

                return analysisResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing prompt performance for {PromptId}", promptId);
                throw;
            }
        }

        /// <summary>
        /// Generates refined variants of a prompt based on identified issues and improvements
        /// </summary>
        public async Task<PromptRefinementResult> RefinePromptAsync(
            string promptId, 
            PromptRefinementOptions? options = null)
        {
            _logger.LogInformation("Starting prompt refinement for {PromptId}", promptId);

            try
            {
                options ??= new PromptRefinementOptions();

                var originalPrompt = await GetManagedPromptAsync(promptId);
                if (originalPrompt == null)
                {
                    throw new ArgumentException($"Prompt {promptId} not found", nameof(promptId));
                }

                // Analyze current performance
                var analysisResult = await AnalyzePromptPerformanceAsync(promptId, options.AnalysisWindow);

                var refinementResult = new PromptRefinementResult
                {
                    RefinementOperationId = Guid.NewGuid().ToString(),
                    RefinedAt = DateTime.UtcNow,
                    OriginalPrompt = originalPrompt,
                    AnalysisResult = analysisResult
                };

                // Generate refined variants using LLM
                var refinedVariants = await GenerateRefinedVariantsAsync(originalPrompt, analysisResult, options);
                refinementResult.RefinedPrompts = refinedVariants;

                // Set up A/B testing if enabled
                if (options.EnableABTesting && refinedVariants.Any())
                {
                    var abTestConfig = new ABTestConfiguration
                    {
                        IsActive = true,
                        TestGroup = $"refinement-{refinementResult.RefinementOperationId}",
                        TestStartDate = DateTime.UtcNow,
                        TestEndDate = DateTime.UtcNow.Add(options.ABTestDuration),
                        KeyMetrics = new List<string> { "quality_score", "validation_approval_rate", "processing_time" },
                        SignificanceThreshold = 0.05
                    };

                    refinementResult.ABTestConfig = abTestConfig;

                    // Save refined prompts with A/B test configuration
                    foreach (var variant in refinedVariants)
                    {
                        variant.ABTestConfig = abTestConfig;
                        variant.Status = PromptStatus.Testing;
                        await SaveManagedPromptAsync(variant);
                    }
                }
                else if (options.AutoApplyRefinements && refinedVariants.Any())
                {
                    // Automatically apply the best variant
                    var bestVariant = refinedVariants.First(); // TODO: Implement selection logic
                    bestVariant.Status = PromptStatus.Active;
                    originalPrompt.Status = PromptStatus.Deprecated;

                    await SaveManagedPromptAsync(bestVariant);
                    await SaveManagedPromptAsync(originalPrompt);
                }

                refinementResult.WasSuccessful = refinedVariants.Any();

                _logger.LogInformation("Completed prompt refinement for {PromptId} with {VariantCount} variants generated", 
                    promptId, refinedVariants.Count);

                return refinementResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refining prompt {PromptId}", promptId);
                return new PromptRefinementResult
                {
                    RefinementOperationId = Guid.NewGuid().ToString(),
                    RefinedAt = DateTime.UtcNow,
                    WasSuccessful = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Compares performance between multiple prompts
        /// </summary>
        public async Task<PromptPerformanceComparison> ComparePromptPerformanceAsync(
            IEnumerable<string> promptIds, 
            TimeSpan comparisonPeriod)
        {
            _logger.LogInformation("Comparing performance for {PromptCount} prompts", promptIds.Count());

            var comparison = new PromptPerformanceComparison
            {
                ComparisonPeriod = comparisonPeriod
            };

            var cutoffDate = DateTime.UtcNow - comparisonPeriod;
            var bestScore = 0.0;
            var bestPromptId = string.Empty;

            foreach (var promptId in promptIds)
            {
                var prompt = await GetManagedPromptAsync(promptId);
                if (prompt == null) continue;

                comparison.ComparedPrompts.Add(prompt);

                var usageRecords = _usageHistory
                    .Where(r => r.PromptId == promptId && r.UsedAt >= cutoffDate)
                    .ToList();

                if (usageRecords.Any())
                {
                    var metrics = new PromptPerformanceMetrics
                    {
                        UsageCount = usageRecords.Count,
                        AverageQualityScore = usageRecords.Average(r => r.QualityScore),
                        SuccessRate = usageRecords.Count(r => r.QualityScore >= 0.7) / (double)usageRecords.Count,
                        AverageProcessingTime = TimeSpan.FromMilliseconds(usageRecords.Average(r => r.ProcessingTime.TotalMilliseconds)),
                        ValidationApprovalRate = usageRecords
                            .Where(r => r.ValidationApproved.HasValue)
                            .Count(r => r.ValidationApproved.Value) / (double)usageRecords.Count(r => r.ValidationApproved.HasValue)
                    };

                    comparison.DetailedMetrics[promptId] = metrics;

                    if (metrics.AverageQualityScore > bestScore)
                    {
                        bestScore = metrics.AverageQualityScore;
                        bestPromptId = promptId;
                    }
                }
            }

            comparison.WinningPromptId = bestPromptId;
            comparison.Recommendations = GenerateComparisonRecommendations(comparison);

            return comparison;
        }

        /// <summary>
        /// Records usage of a prompt for performance tracking
        /// </summary>
        public async Task RecordPromptUsageAsync(
            string promptId,
            double qualityScore,
            TimeSpan processingTime,
            bool? validationApproved = null,
            Dictionary<string, object>? contextMetadata = null)
        {
            var usageRecord = new PromptUsageRecord
            {
                PromptId = promptId,
                UsedAt = DateTime.UtcNow,
                QualityScore = qualityScore,
                ProcessingTime = processingTime,
                ValidationApproved = validationApproved,
                ContextMetadata = contextMetadata ?? new Dictionary<string, object>()
            };

            _usageHistory.Add(usageRecord);

            // Keep only recent history to prevent memory growth
            if (_usageHistory.Count > 10000)
            {
                var cutoff = DateTime.UtcNow.AddDays(-90);
                _usageHistory.RemoveAll(r => r.UsedAt < cutoff);
            }

            _logger.LogDebug("Recorded usage for prompt {PromptId} with quality score {QualityScore}", promptId, qualityScore);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Executes the complete refinement loop
        /// </summary>
        public async Task<PromptRefinementLoopResult> ExecuteRefinementLoopAsync(
            string promptId, 
            PromptRefinementOptions? options = null)
        {
            _logger.LogInformation("Executing complete refinement loop for prompt {PromptId}", promptId);

            var loopResult = new PromptRefinementLoopResult
            {
                LoopExecutionId = Guid.NewGuid().ToString(),
                StartedAt = DateTime.UtcNow
            };

            try
            {
                // Step 1: Get original prompt
                loopResult.OriginalPrompt = await GetManagedPromptAsync(promptId) ?? throw new ArgumentException($"Prompt {promptId} not found");
                loopResult.ActionsTaken.Add("Retrieved original prompt");

                // Step 2: Analyze performance
                loopResult.AnalysisResult = await AnalyzePromptPerformanceAsync(promptId, options?.AnalysisWindow);
                loopResult.ActionsTaken.Add($"Analyzed performance: {loopResult.AnalysisResult.IdentifiedIssues.Count} issues found");

                // Step 3: Check if refinement is needed
                var needsRefinement = loopResult.AnalysisResult.IdentifiedIssues.Any(i => i.Severity >= TestCaseEditorApp.MVVM.Models.IssueSeverity.Medium) ||
                                    loopResult.OriginalPrompt.Performance.AverageQualityScore < (options?.PerformanceThreshold ?? 0.7);

                if (!needsRefinement)
                {
                    loopResult.ActionsTaken.Add("No refinement needed - performance meets thresholds");
                    loopResult.NextStepsRecommendations.Add("Continue monitoring performance");
                    loopResult.WasSuccessful = true;
                }
                else
                {
                    // Step 4: Generate refinements
                    loopResult.RefinementResult = await RefinePromptAsync(promptId, options);
                    loopResult.ActionsTaken.Add($"Generated {loopResult.RefinementResult.RefinedPrompts.Count} refined variants");

                    // Step 5: Set up A/B testing if enabled
                    if (options?.EnableABTesting == true && loopResult.RefinementResult.RefinedPrompts.Any())
                    {
                        var allVariants = new List<ManagedPrompt> { loopResult.OriginalPrompt };
                        allVariants.AddRange(loopResult.RefinementResult.RefinedPrompts);

                        loopResult.ABTestSessionId = await StartABTestAsync(allVariants, loopResult.RefinementResult.ABTestConfig);
                        loopResult.ActionsTaken.Add($"Started A/B test session {loopResult.ABTestSessionId}");
                        loopResult.NextStepsRecommendations.Add($"Monitor A/B test results for session {loopResult.ABTestSessionId}");
                    }

                    loopResult.WasSuccessful = loopResult.RefinementResult.WasSuccessful;
                }

                loopResult.CompletedAt = DateTime.UtcNow;
                
                _logger.LogInformation("Completed refinement loop for prompt {PromptId} in {ProcessingTime}ms", 
                    promptId, loopResult.ProcessingTime.TotalMilliseconds);

                return loopResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in refinement loop for prompt {PromptId}", promptId);
                loopResult.WasSuccessful = false;
                loopResult.Errors.Add(ex.Message);
                loopResult.CompletedAt = DateTime.UtcNow;
                return loopResult;
            }
        }

        #region A/B Testing Methods

        public async Task<string> StartABTestAsync(
            IEnumerable<ManagedPrompt> promptVariants, 
            ABTestConfiguration testConfiguration)
        {
            var testId = Guid.NewGuid().ToString();
            var variants = promptVariants.ToList();

            var abTestSession = new ABTestSession
            {
                TestId = testId,
                Configuration = testConfiguration,
                Variants = variants,
                StartedAt = DateTime.UtcNow,
                IsActive = true
            };

            // Distribute traffic evenly among variants
            var trafficPerVariant = 1.0 / variants.Count;
            for (int i = 0; i < variants.Count; i++)
            {
                variants[i].ABTestConfig = new ABTestConfiguration
                {
                    IsActive = true,
                    TestGroup = testConfiguration.TestGroup,
                    TrafficPercentage = trafficPerVariant,
                    TestStartDate = testConfiguration.TestStartDate,
                    TestEndDate = testConfiguration.TestEndDate,
                    KeyMetrics = testConfiguration.KeyMetrics,
                    SignificanceThreshold = testConfiguration.SignificanceThreshold
                };
            }

            _activeABTests[testId] = abTestSession;

            _logger.LogInformation("Started A/B test {TestId} with {VariantCount} variants", testId, variants.Count);

            return testId;
        }

        public async Task<PromptPerformanceComparison> GetABTestResultsAsync(string abTestId)
        {
            if (!_activeABTests.TryGetValue(abTestId, out var testSession))
            {
                throw new ArgumentException($"A/B test {abTestId} not found", nameof(abTestId));
            }

            var promptIds = testSession.Variants.Select(v => v.PromptId);
            var comparisonPeriod = DateTime.UtcNow - testSession.StartedAt;

            return await ComparePromptPerformanceAsync(promptIds, comparisonPeriod);
        }

        #endregion

        #region Prompt Management Methods

        public async Task<List<ManagedPrompt>> GetAllManagedPromptsAsync(bool includeArchived = false)
        {
            var prompts = _managedPrompts.Values.ToList();
            
            if (!includeArchived)
            {
                prompts = prompts.Where(p => p.Status != PromptStatus.Archived).ToList();
            }

            return prompts;
        }

        public async Task<ManagedPrompt> SaveManagedPromptAsync(ManagedPrompt prompt)
        {
            if (string.IsNullOrEmpty(prompt.PromptId))
            {
                prompt.PromptId = Guid.NewGuid().ToString();
                prompt.CreatedAt = DateTime.UtcNow;
            }

            _managedPrompts[prompt.PromptId] = prompt;
            
            _logger.LogDebug("Saved managed prompt {PromptId}", prompt.PromptId);
            
            return prompt;
        }

        public async Task<ManagedPrompt?> GetManagedPromptAsync(string promptId)
        {
            _managedPrompts.TryGetValue(promptId, out var prompt);
            return prompt;
        }

        public async Task ArchivePromptAsync(string promptId, string reason)
        {
            var prompt = await GetManagedPromptAsync(promptId);
            if (prompt != null)
            {
                prompt.Status = PromptStatus.Archived;
                
                // Add archival record to refinement history
                var archivalRefinement = new PromptRefinement
                {
                    RefinementId = Guid.NewGuid().ToString(),
                    AppliedAt = DateTime.UtcNow,
                    Trigger = RefinementTrigger.ManualReview,
                    RefinementSource = "System",
                    ChangeDescription = "Archived prompt",
                    Rationale = reason
                };
                
                prompt.RefinementHistory.Add(archivalRefinement);
                await SaveManagedPromptAsync(prompt);
                
                _logger.LogInformation("Archived prompt {PromptId}: {Reason}", promptId, reason);
            }
        }

        public async Task<PromptPerformanceMonitor> GetPerformanceMonitorAsync(IEnumerable<string>? promptIds = null)
        {
            var targetPromptIds = promptIds?.ToList() ?? _managedPrompts.Keys.Where(id => 
                _managedPrompts[id].Status == PromptStatus.Active || 
                _managedPrompts[id].Status == PromptStatus.Testing).ToList();

            var monitor = new PromptPerformanceMonitor
            {
                ActivePromptIds = targetPromptIds,
                LastUpdated = DateTime.UtcNow
            };

            var recentCutoff = DateTime.UtcNow.AddHours(-1); // Last hour performance

            foreach (var promptId in targetPromptIds)
            {
                var recentUsage = _usageHistory
                    .Where(r => r.PromptId == promptId && r.UsedAt >= recentCutoff)
                    .ToList();

                if (recentUsage.Any())
                {
                    monitor.CurrentPerformance[promptId] = new PromptPerformanceSnapshot
                    {
                        CapturedAt = DateTime.UtcNow,
                        AverageQualityScore = recentUsage.Average(r => r.QualityScore),
                        SuccessRate = recentUsage.Count(r => r.QualityScore >= 0.7) / (double)recentUsage.Count,
                        SampleSize = recentUsage.Count,
                        AverageProcessingTime = TimeSpan.FromMilliseconds(recentUsage.Average(r => r.ProcessingTime.TotalMilliseconds)),
                        ValidationApprovalRate = recentUsage
                            .Where(r => r.ValidationApproved.HasValue)
                            .Count(r => r.ValidationApproved.Value) / (double)recentUsage.Count(r => r.ValidationApproved.HasValue)
                    };
                }
            }

            return monitor;
        }

        public async Task<List<PromptAnalysisResult>> IdentifyPromptsNeedingRefinementAsync(
            PromptRefinementOptions? options = null)
        {
            options ??= new PromptRefinementOptions();
            var needingRefinement = new List<PromptAnalysisResult>();

            var activePrompts = await GetAllManagedPromptsAsync();
            
            foreach (var prompt in activePrompts.Where(p => p.Status == PromptStatus.Active))
            {
                if (prompt.Performance.UsageCount < options.MinSampleSize)
                    continue;

                if (prompt.Performance.AverageQualityScore < options.PerformanceThreshold)
                {
                    var analysis = await AnalyzePromptPerformanceAsync(prompt.PromptId, options.AnalysisWindow);
                    needingRefinement.Add(analysis);
                }
            }

            return needingRefinement;
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Initializes the system with default prompts for capability derivation
        /// </summary>
        private void InitializeDefaultPrompts()
        {
            var defaultPrompts = new[]
            {
                new ManagedPrompt
                {
                    PromptId = "capability-derivation-v1",
                    Name = "Standard Capability Derivation",
                    Description = "Main prompt for deriving system capabilities from ATP steps",
                    TemplateText = @"Analyze the following ATP step and derive system-level capabilities using the A-N taxonomy:

ATP Step: {atpStepText}

Instructions:
1. Identify testable system behaviors implied by this ATP step
2. Classify each capability using the A-N taxonomy (A through N categories)
3. Write atomic, testable requirements for each capability
4. Specify missing parameters that need to be defined

Return your analysis in structured format.",
                    Version = "1.0",
                    CreatedBy = "System",
                    Status = PromptStatus.Active,
                    Categories = new List<string> { "capability-derivation", "atp-analysis" }
                }
            };

            foreach (var prompt in defaultPrompts)
            {
                prompt.CreatedAt = DateTime.UtcNow;
                _managedPrompts[prompt.PromptId] = prompt;
            }

            _logger.LogInformation("Initialized {PromptCount} default prompts", defaultPrompts.Length);
        }

        /// <summary>
        /// Identifies issues with a prompt based on usage patterns
        /// </summary>
        private async Task<List<PromptIssue>> IdentifyPromptIssuesAsync(
            ManagedPrompt prompt, 
            List<PromptUsageRecord> usageRecords)
        {
            var issues = new List<PromptIssue>();

            // Low quality outputs
            var lowQualityRate = usageRecords.Count(r => r.QualityScore < 0.5) / (double)usageRecords.Count;
            if (lowQualityRate > 0.3) // More than 30% low quality
            {
                issues.Add(new PromptIssue
                {
                    IssueId = Guid.NewGuid().ToString(),
                    Type = TestCaseEditorApp.MVVM.Models.IssueType.LowQualityOutputs,
                    Severity = lowQualityRate > 0.5 ? TestCaseEditorApp.MVVM.Models.IssueSeverity.High : TestCaseEditorApp.MVVM.Models.IssueSeverity.Medium,
                    Description = $"{lowQualityRate:P0} of outputs have low quality scores",
                    Evidence = $"Rate of quality scores below 0.5: {lowQualityRate:P2}",
                    ImpactScore = lowQualityRate
                });
            }

            // Inconsistent results (high variance in quality scores)
            var qualityScores = usageRecords.Select(r => r.QualityScore).ToArray();
            var variance = CalculateVariance(qualityScores);
            if (variance > 0.1) // High variance threshold
            {
                issues.Add(new PromptIssue
                {
                    IssueId = Guid.NewGuid().ToString(),
                    Type = TestCaseEditorApp.MVVM.Models.IssueType.InconsistentResults,
                    Severity = variance > 0.2 ? TestCaseEditorApp.MVVM.Models.IssueSeverity.High : TestCaseEditorApp.MVVM.Models.IssueSeverity.Medium,
                    Description = "High variability in output quality",
                    Evidence = $"Quality score variance: {variance:F3}",
                    ImpactScore = Math.Min(variance * 5, 1.0) // Scale to 0-1
                });
            }

            // High processing time
            var avgProcessingMs = usageRecords.Average(r => r.ProcessingTime.TotalMilliseconds);
            if (avgProcessingMs > 10000) // More than 10 seconds
            {
                issues.Add(new PromptIssue
                {
                    IssueId = Guid.NewGuid().ToString(),
                    Type = TestCaseEditorApp.MVVM.Models.IssueType.HighProcessingTime,
                    Severity = avgProcessingMs > 20000 ? TestCaseEditorApp.MVVM.Models.IssueSeverity.High : TestCaseEditorApp.MVVM.Models.IssueSeverity.Medium,
                    Description = "Processing time is higher than expected",
                    Evidence = $"Average processing time: {avgProcessingMs:F0}ms",
                    ImpactScore = Math.Min(avgProcessingMs / 20000.0, 1.0)
                });
            }

            // Validation rejections
            var validationRecords = usageRecords.Where(r => r.ValidationApproved.HasValue).ToList();
            if (validationRecords.Any())
            {
                var rejectionRate = validationRecords.Count(r => !r.ValidationApproved.Value) / (double)validationRecords.Count;
                if (rejectionRate > 0.4) // More than 40% rejected
                {
                    issues.Add(new PromptIssue
                    {
                        IssueId = Guid.NewGuid().ToString(),
                        Type = TestCaseEditorApp.MVVM.Models.IssueType.ValidationRejections,
                        Severity = rejectionRate > 0.6 ? TestCaseEditorApp.MVVM.Models.IssueSeverity.High : TestCaseEditorApp.MVVM.Models.IssueSeverity.Medium,
                        Description = $"{rejectionRate:P0} of outputs are rejected in validation",
                        Evidence = $"Validation rejection rate: {rejectionRate:P2}",
                        ImpactScore = rejectionRate
                    });
                }
            }

            return issues;
        }

        /// <summary>
        /// Generates improvement suggestions based on identified issues
        /// </summary>
        private async Task<List<PromptImprovement>> GenerateImprovementSuggestionsAsync(
            ManagedPrompt prompt, 
            List<PromptIssue> issues)
        {
            var improvements = new List<PromptImprovement>();

            foreach (var issue in issues)
            {
                switch (issue.Type)
                {
                    case TestCaseEditorApp.MVVM.Models.IssueType.LowQualityOutputs:
                        improvements.Add(new PromptImprovement
                        {
                            ImprovementId = Guid.NewGuid().ToString(),
                            Type = ImprovementType.ClarifyInstructions,
                            Description = "Add more specific instructions and quality criteria",
                            ProposedChange = "Include explicit quality checkpoints and examples of good outputs",
                            ExpectedImpact = 0.7,
                            ImplementationEffort = 0.3,
                            RelatedIssueIds = new List<string> { issue.IssueId }
                        });
                        break;

                    case TestCaseEditorApp.MVVM.Models.IssueType.InconsistentResults:
                        improvements.Add(new PromptImprovement
                        {
                            ImprovementId = Guid.NewGuid().ToString(),
                            Type = ImprovementType.AddConstraints,
                            Description = "Add constraints to ensure consistent output format",
                            ProposedChange = "Specify exact output structure and validation criteria",
                            ExpectedImpact = 0.6,
                            ImplementationEffort = 0.4,
                            RelatedIssueIds = new List<string> { issue.IssueId }
                        });
                        break;

                    case TestCaseEditorApp.MVVM.Models.IssueType.HighProcessingTime:
                        improvements.Add(new PromptImprovement
                        {
                            ImprovementId = Guid.NewGuid().ToString(),
                            Type = ImprovementType.SimplifyLanguage,
                            Description = "Simplify prompt to reduce processing complexity",
                            ProposedChange = "Break down complex instructions into simpler steps",
                            ExpectedImpact = 0.5,
                            ImplementationEffort = 0.5,
                            RelatedIssueIds = new List<string> { issue.IssueId }
                        });
                        break;

                    case TestCaseEditorApp.MVVM.Models.IssueType.ValidationRejections:
                        improvements.Add(new PromptImprovement
                        {
                            ImprovementId = Guid.NewGuid().ToString(),
                            Type = ImprovementType.AddExamples,
                            Description = "Provide more examples of acceptable outputs",
                            ProposedChange = "Include specific examples that pass validation",
                            ExpectedImpact = 0.8,
                            ImplementationEffort = 0.6,
                            RelatedIssueIds = new List<string> { issue.IssueId }
                        });
                        break;
                }
            }

            return improvements;
        }

        /// <summary>
        /// Generates refined prompt variants using LLM
        /// </summary>
        private async Task<List<ManagedPrompt>> GenerateRefinedVariantsAsync(
            ManagedPrompt originalPrompt, 
            PromptAnalysisResult analysisResult, 
            PromptRefinementOptions options)
        {
            var refinedVariants = new List<ManagedPrompt>();

            try
            {
                // Build refinement prompt for LLM
                var refinementPrompt = BuildRefinementPrompt(originalPrompt, analysisResult);

                // Generate refined variants using LLM
                var llmResponse = await _llmService.SendChatMessageAsync("prompt-refinement", refinementPrompt);
                
                // Parse LLM response to extract refined prompts
                var variants = await ParseRefinedPromptsFromResponse(llmResponse, originalPrompt);
                
                // Limit number of variants
                refinedVariants = variants.Take(options.MaxVariants).ToList();

                _logger.LogInformation("Generated {Count} refined variants for prompt {PromptId}", 
                    refinedVariants.Count, originalPrompt.PromptId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating refined variants for prompt {PromptId}", originalPrompt.PromptId);
            }

            return refinedVariants;
        }

        /// <summary>
        /// Builds a refinement prompt for the LLM to generate improved versions
        /// </summary>
        private string BuildRefinementPrompt(ManagedPrompt originalPrompt, PromptAnalysisResult analysisResult)
        {
            var issueDescriptions = string.Join("\n", analysisResult.IdentifiedIssues.Select(i => $"- {i.Description}: {i.Evidence}"));
            var improvements = string.Join("\n", analysisResult.SuggestedImprovements.Select(i => $"- {i.Description}: {i.ProposedChange}"));

            return $@"You are an expert prompt engineer. Your task is to improve the following prompt based on identified performance issues.

ORIGINAL PROMPT:
{originalPrompt.TemplateText}

IDENTIFIED ISSUES:
{issueDescriptions}

SUGGESTED IMPROVEMENTS:
{improvements}

Please generate 3 improved versions of this prompt that address the identified issues. Each version should:
1. Maintain the same core functionality
2. Address the performance issues mentioned
3. Be clear, specific, and actionable
4. Include the same parameter placeholders (e.g., {{atpStepText}})

Return each improved prompt in this format:
### VARIANT 1
[improved prompt text]

### VARIANT 2
[improved prompt text]

### VARIANT 3
[improved prompt text]";
        }

        /// <summary>
        /// Parses refined prompts from LLM response
        /// </summary>
        private async Task<List<ManagedPrompt>> ParseRefinedPromptsFromResponse(
            string llmResponse, 
            ManagedPrompt originalPrompt)
        {
            var variants = new List<ManagedPrompt>();

            try
            {
                var sections = llmResponse.Split(new[] { "### VARIANT" }, StringSplitOptions.RemoveEmptyEntries);
                
                for (int i = 1; i < sections.Length && i <= 3; i++) // Skip first section (header), max 3 variants
                {
                    var sectionText = sections[i].Trim();
                    var lines = sectionText.Split('\n');
                    if (lines.Length > 1)
                    {
                        var promptText = string.Join('\n', lines.Skip(1)).Trim();
                        
                        var variant = new ManagedPrompt
                        {
                            PromptId = Guid.NewGuid().ToString(),
                            Name = $"{originalPrompt.Name} - Refined V{i}",
                            Description = $"Refined version of {originalPrompt.Name}",
                            TemplateText = promptText,
                            Version = $"{originalPrompt.Version}.{i}",
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = "PromptRefinementEngine",
                            Status = PromptStatus.Draft,
                            Categories = originalPrompt.Categories.ToList(),
                            Parameters = originalPrompt.Parameters.ToList(),
                            ParentPromptId = originalPrompt.PromptId
                        };

                        // Add refinement record
                        variant.RefinementHistory.Add(new PromptRefinement
                        {
                            RefinementId = Guid.NewGuid().ToString(),
                            AppliedAt = DateTime.UtcNow,
                            Trigger = RefinementTrigger.PoorPerformance,
                            RefinementSource = "PromptRefinementEngine",
                            ChangeDescription = $"Generated refined variant {i}",
                            OldPromptText = originalPrompt.TemplateText,
                            NewPromptText = promptText,
                            Rationale = "Performance improvement through LLM-based refinement"
                        });

                        variants.Add(variant);
                        
                        // Update parent-child relationship
                        originalPrompt.ChildPromptIds.Add(variant.PromptId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing refined prompts from LLM response");
            }

            return variants;
        }

        /// <summary>
        /// Helper methods for statistical calculations
        /// </summary>
        private double CalculateVariance(double[] values)
        {
            if (values.Length < 2) return 0.0;
            var mean = values.Average();
            return values.Select(v => Math.Pow(v - mean, 2)).Average();
        }

        private double CalculateAnalysisConfidence(List<PromptUsageRecord> usageRecords)
        {
            // Confidence based on sample size and data consistency
            var sampleSizeScore = Math.Min(usageRecords.Count / 50.0, 1.0); // Full confidence at 50+ samples
            var dataConsistency = 1.0 - CalculateVariance(usageRecords.Select(r => r.QualityScore).ToArray());
            return (sampleSizeScore + dataConsistency) / 2.0;
        }

        private List<string> GenerateRecommendedActions(
            List<PromptIssue> issues, 
            List<PromptImprovement> improvements)
        {
            var actions = new List<string>();

            var criticalIssues = issues.Where(i => i.Severity == TestCaseEditorApp.MVVM.Models.IssueSeverity.Critical).ToList();
            if (criticalIssues.Any())
            {
                actions.Add("URGENT: Address critical issues immediately");
            }

            var highImpactImprovements = improvements.Where(i => i.ExpectedImpact > 0.7).ToList();
            if (highImpactImprovements.Any())
            {
                actions.Add($"Implement {highImpactImprovements.Count} high-impact improvements");
            }

            if (issues.Count > 3)
            {
                actions.Add("Consider comprehensive prompt redesign due to multiple issues");
            }
            else if (improvements.Any())
            {
                actions.Add("Apply suggested improvements and monitor performance");
            }
            else
            {
                actions.Add("Continue monitoring - no immediate action required");
            }

            return actions;
        }

        private List<string> GenerateComparisonRecommendations(PromptPerformanceComparison comparison)
        {
            var recommendations = new List<string>();

            if (!string.IsNullOrEmpty(comparison.WinningPromptId))
            {
                recommendations.Add($"Promote prompt {comparison.WinningPromptId} as the primary variant");
                
                var winnerMetrics = comparison.DetailedMetrics[comparison.WinningPromptId];
                if (winnerMetrics.AverageQualityScore > 0.8)
                {
                    recommendations.Add("Winner shows strong performance - consider it as baseline for future refinements");
                }
            }

            var poorPerformers = comparison.DetailedMetrics
                .Where(kvp => kvp.Value.AverageQualityScore < 0.6)
                .ToList();

            if (poorPerformers.Any())
            {
                recommendations.Add($"Consider retiring {poorPerformers.Count} poor-performing variants");
            }

            return recommendations;
        }

        #endregion
    }

    #region Internal Models

    /// <summary>
    /// Internal model for tracking prompt usage
    /// </summary>
    internal class PromptUsageRecord
    {
        public string PromptId { get; set; } = string.Empty;
        public DateTime UsedAt { get; set; }
        public double QualityScore { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public bool? ValidationApproved { get; set; }
        public Dictionary<string, object> ContextMetadata { get; set; } = new();
    }

    /// <summary>
    /// Internal model for tracking A/B test sessions
    /// </summary>
    internal class ABTestSession
    {
        public string TestId { get; set; } = string.Empty;
        public ABTestConfiguration Configuration { get; set; } = new();
        public List<ManagedPrompt> Variants { get; set; } = new();
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public bool IsActive { get; set; }
    }

    #endregion
}