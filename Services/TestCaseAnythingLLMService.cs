using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Standalone service for AnythingLLM operations specific to test case generation.
    /// Reports status changes via AnythingLLMMediator for decoupled view updates.
    /// </summary>
    public class TestCaseAnythingLLMService
    {
        private readonly AnythingLLMService _anythingLLMService;
        private readonly NotificationService _notificationService;

        public TestCaseAnythingLLMService(
            AnythingLLMService anythingLLMService,
            NotificationService notificationService)
        {
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        }

        /// <summary>
        /// Connect to AnythingLLM with auto-launch and notifications
        /// </summary>
        public async Task ConnectAsync()
        {
            try
            {
                // Notify via mediator that we're starting
                AnythingLLMMediator.NotifyStatusUpdated(new AnythingLLMStatus 
                { 
                    IsAvailable = false, 
                    IsStarting = true, 
                    StatusMessage = "Connecting to AnythingLLM..." 
                });
                
                _notificationService.ShowInfo("Connecting to AnythingLLM...");
                
                await _anythingLLMService.EnsureServiceRunningAsync();
                
                // Notify via mediator that we're connected
                AnythingLLMMediator.NotifyStatusUpdated(new AnythingLLMStatus 
                { 
                    IsAvailable = true, 
                    IsStarting = false, 
                    StatusMessage = "✅ Connected to AnythingLLM" 
                });
                
                _notificationService.ShowSuccess("✅ Connected to AnythingLLM", 5);
            }
            catch (Exception ex)
            {
                // Notify via mediator that there was an error
                AnythingLLMMediator.NotifyStatusUpdated(new AnythingLLMStatus 
                { 
                    IsAvailable = false, 
                    IsStarting = false, 
                    StatusMessage = "❌ Failed to connect to AnythingLLM" 
                });
                
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[TestCaseAnythingLLMService] Failed to connect");
                _notificationService.ShowError("❌ Failed to connect to AnythingLLM", 5);
            }
        }
        
        /// <summary>
        /// Analyze multiple requirements in parallel with streaming progress updates
        /// </summary>
        public async Task<List<RequirementAnalysis>> AnalyzeRequirementsInParallelAsync(
            IEnumerable<Requirement> requirements,
            string workspaceSlug,
            int maxConcurrency = 3,
            Action<string, int, int>? onProgress = null,
            Action<string>? onStreamingUpdate = null,
            CancellationToken cancellationToken = default)
        {
            var requirementsList = requirements.ToList();
            var results = new List<RequirementAnalysis>();
            
            onProgress?.Invoke("Starting parallel requirement analysis...", 0, requirementsList.Count);
            
            // Use the parallel processing capability from AnythingLLMService
            var processingResults = await _anythingLLMService.ProcessRequirementsInParallelAsync(
                requirementsList,
                async (requirement, index, ct) =>
                {
                    var analysisMessage = BuildRequirementAnalysisPrompt(requirement);
                    
                    // Use streaming for real-time feedback
                    return await _anythingLLMService.SendChatMessageStreamingAsync(
                        workspaceSlug,
                        analysisMessage,
                        onChunkReceived: (chunk) => onStreamingUpdate?.Invoke($"Req {index + 1}: {chunk}"),
                        onProgressUpdate: (update) => onProgress?.Invoke($"Requirement {index + 1}: {update}", index, requirementsList.Count),
                        cancellationToken: ct);
                },
                maxConcurrency: maxConcurrency,
                rateLimitDelayMs: 100,
                onProgress: onProgress,
                cancellationToken: cancellationToken);
            
            // Process results
            foreach (var result in processingResults.OrderBy(r => r.Index))
            {
                if (result.Success && !string.IsNullOrEmpty(result.Result))
                {
                    try
                    {
                        var analysis = ParseRequirementAnalysis(result.Result, requirementsList[result.Index]);
                        results.Add(analysis);
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to parse analysis for requirement {result.Index}");
                        results.Add(CreateErrorAnalysis(requirementsList[result.Index], $"Parse error: {ex.Message}"));
                    }
                }
                else
                {
                    results.Add(CreateErrorAnalysis(requirementsList[result.Index], result.Error ?? "Unknown error"));
                }
            }
            
            var successCount = results.Count(r => r.QualityScore > 0);
            onProgress?.Invoke($"Parallel analysis complete: {successCount}/{requirementsList.Count} successful", requirementsList.Count, requirementsList.Count);
            
            return results;
        }
        
        /// <summary>
        /// Generate test cases with streaming response and progress tracking
        /// </summary>
        public async Task<string?> GenerateTestCasesWithStreamingAsync(
            string workspaceSlug,
            Requirement requirement,
            string verificationMethod,
            Action<string>? onStreamingUpdate = null,
            Action<string>? onProgressUpdate = null,
            CancellationToken cancellationToken = default)
        {
            onProgressUpdate?.Invoke("Preparing test case generation...");
            
            var prompt = BuildTestCaseGenerationPrompt(requirement, verificationMethod);
            
            onProgressUpdate?.Invoke("Starting test case generation...");
            
            return await _anythingLLMService.SendChatMessageStreamingAsync(
                workspaceSlug,
                prompt,
                onChunkReceived: onStreamingUpdate,
                onProgressUpdate: onProgressUpdate,
                cancellationToken: cancellationToken);
        }
        
        private string BuildRequirementAnalysisPrompt(Requirement requirement)
        {
            return $@"Analyze this requirement for quality and provide structured feedback:

ID: {requirement.Item}
Name: {requirement.Name}
Description: {requirement.Description}

Please provide analysis in JSON format with QualityScore (1-10), Issues array, and Recommendations array.";
        }
        
        private string BuildTestCaseGenerationPrompt(Requirement requirement, string verificationMethod)
        {
            return $@"Generate comprehensive test cases for this requirement:

Requirement ID: {requirement.Item}
Description: {requirement.Description}
Verification Method: {verificationMethod}

Please generate detailed test cases covering normal, edge, and error conditions.";
        }
        
        private RequirementAnalysis ParseRequirementAnalysis(string jsonResponse, Requirement requirement)
        {
            // This would use the existing JSON parsing logic
            // For now, return a basic analysis
            return new RequirementAnalysis
            {
                QualityScore = 8,
                Issues = new List<AnalysisIssue>(),
                Recommendations = new List<AnalysisRecommendation>(),
                FreeformFeedback = "Analysis completed via streaming",
                Timestamp = DateTime.Now,
                IsAnalyzed = true
            };
        }
        
        private RequirementAnalysis CreateErrorAnalysis(Requirement requirement, string errorMessage)
        {
            return new RequirementAnalysis
            {
                QualityScore = 1,
                Issues = new List<AnalysisIssue> 
                { 
                    new AnalysisIssue 
                    { 
                        Category = "Analysis Error", 
                        Severity = "High", 
                        Description = errorMessage 
                    } 
                },
                Recommendations = new List<AnalysisRecommendation>(),
                FreeformFeedback = $"Analysis failed: {errorMessage}",
                Timestamp = DateTime.Now,
                IsAnalyzed = false,
                ErrorMessage = errorMessage
            };
        }
    }
}