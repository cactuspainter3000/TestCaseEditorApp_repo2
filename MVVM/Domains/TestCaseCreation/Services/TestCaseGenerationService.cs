using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services
{
    /// <summary>
    /// Generates test cases from requirements using LLM with RAG-optimized prompts.
    /// Automatically detects requirement overlap and creates shared test cases.
    /// Integrates RAG (Retrieval-Augmented Generation) diagnostics for performance optimization.
    /// </summary>
    public class TestCaseGenerationService : ITestCaseGenerationService
    {
        private readonly ILogger<TestCaseGenerationService> _logger;
        private readonly AnythingLLMService _anythingLLMService;
        private readonly RAGContextService _ragContextService;
        private readonly RAGFeedbackIntegrationService _ragFeedbackService;
        private string? _projectWorkspaceName;
        
        /// <summary>
        /// Gets whether the service has a valid workspace context configured
        /// </summary>
        public bool HasWorkspaceContext => !string.IsNullOrEmpty(_projectWorkspaceName);

        public TestCaseGenerationService(
            ILogger<TestCaseGenerationService> logger,
            AnythingLLMService anythingLLMService,
            RAGContextService? ragContextService = null,
            RAGFeedbackIntegrationService? ragFeedbackService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            _ragContextService = ragContextService;
            _ragFeedbackService = ragFeedbackService;
        }

        /// <summary>
        /// Sets the workspace context for project-specific test case generation
        /// </summary>
        /// <param name="workspaceName">Name of the project workspace to use</param>
        public void SetWorkspaceContext(string? workspaceName)
        {
            _projectWorkspaceName = workspaceName;
            _logger.LogInformation("[TestCaseGeneration] Workspace context set to: {WorkspaceName}, HasWorkspaceContext now: {HasContext}", 
                workspaceName ?? "<none>", HasWorkspaceContext);
        }

        public async Task<List<LLMTestCase>> GenerateTestCasesAsync(
            IEnumerable<Requirement> requirements,
            Action<string, int, int>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            var result = await GenerateTestCasesWithDiagnosticsAsync(requirements, progressCallback, cancellationToken);
            return result.TestCases;
        }

        public async Task<TestCaseGenerationResult> GenerateTestCasesWithDiagnosticsAsync(
            IEnumerable<Requirement> requirements,
            Action<string, int, int>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            var requirementList = requirements.ToList();
            if (!requirementList.Any())
                return new TestCaseGenerationResult(new List<LLMTestCase>(), string.Empty, string.Empty);

            var stopwatch = Stopwatch.StartNew();
            string generatedPrompt = string.Empty;
            string llmResponse = string.Empty;
            string? workspaceSlug = null;
            
            try
            {
                // Get workspace slug from project context
                workspaceSlug = await GetWorkspaceSlugAsync();
                if (string.IsNullOrEmpty(workspaceSlug))
                {
                    // Generate prompt anyway for potential external LLM use
                    progressCallback?.Invoke("Preparing test case generation prompt...", 0, requirementList.Count);
                    generatedPrompt = CreateBatchGenerationPrompt(requirementList);
                    
                    // Offer to export prompt for external LLM since workspace context is missing
                    await OfferWorkspaceContextHelpAsync(generatedPrompt);
                    
                    var errorMsg = "No workspace context set. The generation prompt has been offered for external LLM use. Please open or create a project first to use internal LLM generation.";
                    _logger.LogWarning("[TestCaseGeneration] {ErrorMsg}", errorMsg);
                    progressCallback?.Invoke(errorMsg, requirementList.Count, requirementList.Count);
                    return new TestCaseGenerationResult(new List<LLMTestCase>(), generatedPrompt, string.Empty);
                }

                _logger.LogInformation("Generating test cases for {Count} requirements using workspace '{Workspace}'", 
                    requirementList.Count, workspaceSlug);
                // Ensure RAG is configured before generation
                if (_ragContextService != null)
                {
                    progressCallback?.Invoke("Ensuring RAG documents are configured...", 0, requirementList.Count);
                    var ragConfigured = await _ragContextService.EnsureRAGConfiguredAsync(workspaceSlug);
                    if (!ragConfigured)
                    {
                        _logger.LogWarning("[TestCaseGeneration] RAG configuration failed, proceeding without RAG context");
                    }
                }

                progressCallback?.Invoke("Preparing test case generation prompt...", 0, requirementList.Count);
                
                // Create prompt for batch generation with similarity detection
                generatedPrompt = CreateBatchGenerationPrompt(requirementList);
                
                progressCallback?.Invoke($"Sending to LLM workspace '{workspaceSlug}' (this may take 1-2 minutes)...", 0, requirementList.Count);
                
                // Send to LLM with interactive timeout handling
                llmResponse = await SendWithInteractiveTimeoutAsync(
                    workspaceSlug, 
                    generatedPrompt, 
                    cancellationToken, 
                    progressCallback, 
                    requirementList.Count);

                stopwatch.Stop();

                // Track RAG request
                _ragContextService?.TrackRAGRequest(workspaceSlug, generatedPrompt, llmResponse, !string.IsNullOrEmpty(llmResponse), stopwatch.Elapsed);

                if (string.IsNullOrEmpty(llmResponse))
                {
                    _logger.LogError("[TestCaseGeneration] Empty response from LLM workspace '{WorkspaceSlug}'", workspaceSlug);
                    
                    // Offer prompt export for empty LLM responses (often due to timeout or processing issues)
                    await OfferPromptExportForEmptyResponseAsync(workspaceSlug, generatedPrompt, stopwatch.Elapsed);
                    
                    var errorMsg = $"LLM did not return a response. Possible causes: " +
                        $"1) Workspace '{workspaceSlug}' does not exist in AnythingLLM (should have been created with project), " +
                        "2) Timeout (LLM processing slowly - check AnythingLLM window for activity), " +
                        "3) Connection issues, " +
                        "4) LLM service error. " +
                        "The generation prompt has been offered for external LLM use.";
                    _logger.LogWarning("[TestCaseGeneration] {ErrorMsg}", errorMsg);
                    progressCallback?.Invoke(errorMsg, requirementList.Count, requirementList.Count);
                    return new TestCaseGenerationResult(new List<LLMTestCase>(), generatedPrompt, llmResponse ?? string.Empty);
                }

                progressCallback?.Invoke("Parsing test cases from response...", requirementList.Count, requirementList.Count);
                
                // Parse JSON response into test cases
                var testCases = ParseTestCasesFromResponse(llmResponse, requirementList);
                
                if (!testCases.Any())
                {
                    var errorMsg = "LLM returned a response but no valid test cases were generated. " +
                        "The LLM may have: 1) Failed to parse the request, 2) Generated invalid JSON, " +
                        "3) Returned an error message instead of test cases. " +
                        "Check the debug logs for the raw LLM response.";
                    _logger.LogWarning("[TestCaseGeneration] {ErrorMsg}", errorMsg);
                    progressCallback?.Invoke(errorMsg, requirementList.Count, requirementList.Count);
                    return new TestCaseGenerationResult(new List<LLMTestCase>(), generatedPrompt, llmResponse);
                }
                
                progressCallback?.Invoke($"Completed: Generated {testCases.Count} test cases", requirementList.Count, requirementList.Count);
                
                _logger.LogInformation("Generated {Count} test cases covering {ReqCount} requirements in {Duration}ms",
                    testCases.Count, requirementList.Count, stopwatch.ElapsedMilliseconds);
                
                // Collect RAG feedback for optimization
                if (_ragFeedbackService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _ragFeedbackService.CollectGenerationFeedbackAsync(
                                workspaceSlug,
                                testCases,
                                requirementList,
                                usedDocuments: null, // TODO: Extract from AnythingLLM response
                                cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[TestCaseGeneration] Error collecting RAG feedback");
                        }
                    });
                }

                return new TestCaseGenerationResult(testCases, generatedPrompt, llmResponse);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                _logger.LogInformation("[TestCaseGeneration] Test case generation was cancelled by user or timed out after 3 minutes");
                progressCallback?.Invoke("Generation cancelled or timed out after 3 minutes", requirementList.Count, requirementList.Count);
                return new TestCaseGenerationResult(new List<LLMTestCase>(), generatedPrompt, "Generation cancelled or timed out");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _ragContextService?.TrackRAGRequest(workspaceSlug ?? "unknown", "", null, false, stopwatch.Elapsed);
                
                var errorMsg = $"Test case generation failed: {ex.Message}";
                _logger.LogError(ex, "[TestCaseGeneration] {ErrorMsg}", errorMsg);
                
                // If we have a prompt, offer to export it for external LLM use
                if (!string.IsNullOrEmpty(generatedPrompt))
                {
                    await OfferPromptExportForErrorAsync(generatedPrompt, ex.Message);
                }
                
                progressCallback?.Invoke(errorMsg, requirementList.Count, requirementList.Count);
                return new TestCaseGenerationResult(new List<LLMTestCase>(), generatedPrompt, llmResponse);
            }
        }

        public async Task<List<LLMTestCase>> GenerateTestCasesForSingleRequirementAsync(
            Requirement requirement,
            Action<string, int, int>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Generating test cases for requirement {Id}", requirement.Item);

            try
            {
                // Get workspace slug from project context
                var workspaceSlug = await GetWorkspaceSlugAsync();
                if (string.IsNullOrEmpty(workspaceSlug))
                {
                    throw new InvalidOperationException("No workspace context set. Please open or create a project first.");
                }

                progressCallback?.Invoke($"Analyzing requirement {requirement.Item}...", 0, 1);
                
                var prompt = CreateSingleRequirementPrompt(requirement);
                
                progressCallback?.Invoke($"Generating test cases for {requirement.Item}...", 0, 1);
                
                var response = await _anythingLLMService.SendChatMessageAsync(
                    workspaceSlug,
                    prompt,
                    cancellationToken);

                if (string.IsNullOrEmpty(response))
                {
                    var errorMsg = $"LLM did not return a response for requirement {requirement.Item}. " +
                        "This could be due to timeout, connection issues, or LLM service problems.";
                    _logger.LogWarning("[TestCaseGeneration] {ErrorMsg}", errorMsg);
                    progressCallback?.Invoke(errorMsg, 1, 1);
                    return new List<LLMTestCase>();
                }

                var testCases = ParseTestCasesFromResponse(response, new[] { requirement });
                
                if (!testCases.Any())
                {
                    var errorMsg = $"No valid test cases generated for requirement {requirement.Item}. " +
                        "The LLM may have returned invalid JSON or an error message.";
                    _logger.LogWarning("[TestCaseGeneration] {ErrorMsg}", errorMsg);
                    progressCallback?.Invoke(errorMsg, 1, 1);
                    return new List<LLMTestCase>();
                }
                
                _logger.LogInformation("Generated {Count} test cases for requirement {Id}",
                    testCases.Count, requirement.Item);
                
                progressCallback?.Invoke($"Generated {testCases.Count} test cases", 1, 1);
                return testCases;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to generate test cases for requirement {requirement.Item}: {ex.Message}";
                _logger.LogError(ex, "[TestCaseGeneration] {ErrorMsg}", errorMsg);
                progressCallback?.Invoke(errorMsg, 1, 1);
                return new List<LLMTestCase>();
            }
        }

        public TestCaseCoverageSummary CalculateCoverage(
            IEnumerable<Requirement> requirements,
            IEnumerable<LLMTestCase> testCases)
        {
            var reqList = requirements.ToList();
            var tcList = testCases.ToList();

            var relationships = new List<RequirementTestCaseMapping>();
            var coveredCount = 0;

            foreach (var req in reqList)
            {
                var coveringTestCases = tcList
                    .Where(tc => tc.CoveredRequirementIds.Contains(req.Item))
                    .Select(tc => tc.Id)
                    .ToList();

                var relationship = new RequirementTestCaseMapping
                {
                    RequirementId = req.Item,
                    TestCaseIds = coveringTestCases,
                    CoveragePercentage = coveringTestCases.Any() ? 100 : 0
                };

                relationships.Add(relationship);

                if (coveringTestCases.Any())
                    coveredCount++;
            }

            return new TestCaseCoverageSummary
            {
                TotalRequirements = reqList.Count,
                CoveredRequirements = coveredCount,
                UncoveredRequirements = reqList.Count - coveredCount,
                TotalTestCases = tcList.Count,
                AverageTestCasesPerRequirement = reqList.Count > 0 
                    ? (double)tcList.Sum(tc => tc.CoveredRequirementIds.Count) / reqList.Count 
                    : 0,
                CoveragePercentage = reqList.Count > 0 
                    ? (double)coveredCount / reqList.Count * 100 
                    : 0,
                Relationships = relationships
            };
        }

        public List<string> ValidateTestCaseCoverage(
            LLMTestCase testCase,
            IEnumerable<Requirement> requirements)
        {
            var issues = new List<string>();
            var reqList = requirements.ToList();

            if (!testCase.CoveredRequirementIds.Any())
            {
                issues.Add("Test case does not cover any requirements");
            }

            foreach (var reqId in testCase.CoveredRequirementIds)
            {
                if (!reqList.Any(r => r.Item == reqId))
                {
                    issues.Add($"Test case references non-existent requirement: {reqId}");
                }
            }

            if (string.IsNullOrWhiteSpace(testCase.Title))
            {
                issues.Add("Test case title is empty");
            }

            if (!testCase.Steps.Any())
            {
                issues.Add("Test case has no steps");
            }

            return issues;
        }

        private string CreateBatchGenerationPrompt(List<Requirement> requirements)
        {
            var reqDetails = string.Join("\n\n", requirements.Select((r, i) => 
                $"[{i + 1}] ID: {r.Item}\n" +
                $"    Description: {r.Description}\n" +
                $"    Verification: {string.Join(", ", r.VerificationMethods)}"));

            return $@"Generate test cases for the following requirements. IMPORTANT: Analyze all requirements for similarity and overlap. When requirements are similar enough, create ONE shared test case that covers MULTIPLE requirements instead of duplicating test cases.

REQUIREMENTS:
{reqDetails}

INSTRUCTIONS:
1. Identify groups of similar requirements that can share test cases
2. For similar requirements, create ONE test case with multiple CoveredRequirementIds
3. For unique requirements, create dedicated test cases
4. Each test case should have clear steps and expected results
5. Preconditions and postconditions MUST be a single string (not an array). If multiple items, join with a semicolon and a space
6. **CRITICAL**: In ""coveredRequirementIds"", use the EXACT requirement IDs provided above (e.g., ""RTU4220-REQ_RC-3""). Do NOT use simplified IDs like ""REQ-1"" or custom shortcuts.
7. Return ONLY valid JSON matching this schema - no explanatory text

RESPOND WITH JSON ONLY:
{{
  ""testCases"": [
    {{
      ""id"": ""TC-001"",
      ""title"": ""Test case title"",
      ""description"": ""What this test validates"",
    ""preconditions"": ""Setup required (single string)"",
      ""steps"": [
        {{
          ""stepNumber"": 1,
          ""action"": ""Do something"",
          ""expectedResult"": ""Expected outcome"",
          ""testData"": ""Data needed (optional)""
        }}
      ],
      ""expectedResult"": ""Overall expected outcome"",
    ""postconditions"": ""Cleanup (optional, single string)"",
      ""coveredRequirementIds"": [""EXACT-REQ-ID-FROM-ABOVE"", ""ANOTHER-EXACT-REQ-ID-FROM-ABOVE""],
      ""priority"": ""High"",
      ""testType"": ""Functional"",
      ""estimatedDurationMinutes"": 15,
      ""tags"": [""tag1"", ""tag2""],
      ""generationConfidence"": 85
    }}
  ]
}}";
        }

        private string CreateSingleRequirementPrompt(Requirement requirement)
        {
            return $@"Generate test cases for this requirement:

ID: {requirement.Item}
Description: {requirement.Description}
Verification Method: {string.Join(", ", requirement.VerificationMethods)}

INSTRUCTIONS:
1. Create comprehensive test cases covering all aspects of this requirement
2. Include clear preconditions, steps, and expected results
3. Preconditions and postconditions MUST be a single string (not an array). If multiple items, join with a semicolon and a space
4. Return ONLY valid JSON matching this schema - no explanatory text

RESPOND WITH JSON ONLY:
{{
  ""testCases"": [
    {{
      ""id"": ""TC-001"",
      ""title"": ""Test case title"",
      ""description"": ""What this test validates"",
    ""preconditions"": ""Setup required (single string)"",
      ""steps"": [
        {{
          ""stepNumber"": 1,
          ""action"": ""Do something"",
          ""expectedResult"": ""Expected outcome"",
          ""testData"": ""Data needed (optional)""
        }}
      ],
      ""expectedResult"": ""Overall expected outcome"",
    ""postconditions"": ""Cleanup (optional, single string)"",
      ""coveredRequirementIds"": [""{requirement.Item}""],
      ""priority"": ""Medium"",
      ""testType"": ""Functional"",
      ""estimatedDurationMinutes"": 10,
      ""tags"": [],
      ""generationConfidence"": 80
    }}
  ]
}}";
        }

        private List<LLMTestCase> ParseTestCasesFromResponse(string response, IEnumerable<Requirement> requirements)
        {
            try
            {
                _logger.LogInformation("[TestCaseGeneration] Parsing response (length: {Length} chars)", response.Length);
                
                // Log first 500 chars for debugging
                var preview = response.Length > 500 ? response.Substring(0, 500) + "..." : response;
                _logger.LogDebug("[TestCaseGeneration] Response preview: {Preview}", preview);
                
                // Extract JSON from response (LLM might include markdown code fences)
                var json = ExtractJsonFromResponse(response);
                
                _logger.LogDebug("[TestCaseGeneration] Extracted JSON (length: {Length} chars)", json.Length);
                
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var result = JsonSerializer.Deserialize<TestCaseGenerationResponse>(json, options);
                
                if (result?.TestCases == null)
                {
                    _logger.LogWarning("[TestCaseGeneration] Failed to deserialize test cases from response");
                    return new List<LLMTestCase>();
                }

                _logger.LogInformation("[TestCaseGeneration] Parsed {Count} test cases from response", result.TestCases.Count);

                // Validate and assign default values
                foreach (var tc in result.TestCases)
                {
                    tc.CreatedDate = DateTime.UtcNow;
                    tc.LastModifiedDate = DateTime.UtcNow;
                    tc.IsAutoGenerated = true;
                    tc.CreatedBy = "AI-Generated";

                    // Ensure CoveredRequirementIds is populated
                    if (!tc.CoveredRequirementIds.Any())
                    {
                        // Fallback: assign to first requirement if not specified
                        var firstReq = requirements.FirstOrDefault();
                        if (firstReq != null)
                        {
                            tc.CoveredRequirementIds.Add(firstReq.Item);
                            _logger.LogDebug("[TestCaseGeneration] Test case {Id} had no covered requirements, assigned to {ReqId}", tc.Id, firstReq.Item);
                        }
                    }
                }

                return result.TestCases;
            }
            catch (JsonException jex)
            {
                _logger.LogError(jex, "[TestCaseGeneration] Failed to parse JSON from response - invalid format");
                _logger.LogDebug("[TestCaseGeneration] JSON error details: {Details}", jex.Message);
                return new List<LLMTestCase>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TestCaseGeneration] Failed to parse test cases from response");
                return new List<LLMTestCase>();
            }
        }

        private string ExtractJsonFromResponse(string response)
        {
            // Remove markdown code fences if present
            var cleaned = response.Trim();
            
            if (cleaned.StartsWith("```json"))
            {
                cleaned = cleaned.Substring(7);
            }
            else if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned.Substring(3);
            }
            
            if (cleaned.EndsWith("```"))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 3);
            }
            
            return cleaned.Trim();
        }

        /// <summary>
        /// Sends LLM request with interactive timeout handling - prompts user to continue waiting or cancel
        /// </summary>
        private async Task<string> SendWithInteractiveTimeoutAsync(
            string workspaceSlug, 
            string prompt, 
            CancellationToken cancellationToken, 
            Action<string, int, int>? progressCallback = null,
            int totalSteps = 1)
        {
            var timeoutSeconds = 180; // Increased timeout for first-time LLM processing (includes setup/warmup)
            var attempt = 0;
            
            while (!cancellationToken.IsCancellationRequested)
            {
                attempt++;
                _logger.LogInformation("[TestCaseGeneration] LLM request attempt {Attempt}, timeout: {Timeout}s", attempt, timeoutSeconds);
                
                try
                {
                    // Create timeout for this attempt
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                    
                    // Update progress with timeout info
                    var timeoutMsg = attempt == 1 
                        ? $"Sending to LLM workspace '{workspaceSlug}' ({timeoutSeconds}s timeout)..."
                        : $"Continuing LLM request (attempt {attempt}, {timeoutSeconds}s timeout)...";
                    progressCallback?.Invoke(timeoutMsg, 0, totalSteps);
                    
                    // Send request with timeout
                    var response = await _anythingLLMService.SendChatMessageAsync(
                        workspaceSlug,
                        prompt,
                        combinedCts.Token);
                    
                    // Success - return response
                    _logger.LogInformation("[TestCaseGeneration] LLM request succeeded on attempt {Attempt}", attempt);
                    return response;
                }
                catch (TaskCanceledException ex) when (ex.CancellationToken == cancellationToken)
                {
                    // User cancellation - rethrow
                    _logger.LogInformation("[TestCaseGeneration] LLM request cancelled by user");
                    throw;
                }
                catch (TaskCanceledException)
                {
                    // Timeout occurred - prompt user
                    _logger.LogWarning("[TestCaseGeneration] LLM request timed out after {Timeout}s (attempt {Attempt})", timeoutSeconds, attempt);
                    
                    // Show timeout dialog on UI thread
                    var userChoice = await ShowTimeoutDialogAsync(attempt, timeoutSeconds, workspaceSlug, prompt);
                    
                    if (userChoice == TimeoutChoice.Cancel)
                    {
                        _logger.LogInformation("[TestCaseGeneration] User chose to cancel after timeout");
                        throw new OperationCanceledException("User cancelled operation after timeout");
                    }
                    
                    // User chose to continue - loop will retry with same timeout
                    _logger.LogInformation("[TestCaseGeneration] User chose to continue waiting, retrying...");
                    continue;
                }
            }
            
            throw new OperationCanceledException("Operation was cancelled");
        }

        /// <summary>
        /// Shows timeout dialog to user and returns their choice
        /// </summary>
        private async Task<TimeoutChoice> ShowTimeoutDialogAsync(int attempt, int timeoutSeconds, string workspaceSlug, string prompt)
        {
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var message = attempt == 1
                    ? $"The LLM request is taking longer than expected (>{timeoutSeconds} seconds).\n\n" +
                      "This can happen when:\n" +
                      "• The LLM is processing a complex request\n" +
                      "• The model is busy with other requests\n" +
                      "• Network connectivity is slow\n\n" +
                      "Would you like to continue waiting for another {timeoutSeconds} seconds?"
                    : $"The LLM request is still processing (attempt {attempt}, >{timeoutSeconds * attempt} seconds total).\n\n" +
                      "Would you like to continue waiting for another {timeoutSeconds} seconds?";

                var result = System.Windows.MessageBox.Show(
                    message,
                    "LLM Request Taking Longer Than Expected",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    return TimeoutChoice.Continue;
                }
                else
                {
                    // User chose not to continue - offer to copy prompt for external LLM
                    var copyPromptMessage = $"Would you like to copy the test case generation prompt to use in an external LLM?\n\n" +
                                          "This will copy the complete prompt to your clipboard so you can:\n" +
                                          "• Paste it into ChatGPT, Claude, or other LLMs\n" +
                                          "• Get the test case generation response from an external source\n" +
                                          "• Continue your work without losing progress\n\n" +
                                          $"Workspace: {workspaceSlug}";

                    var copyResult = System.Windows.MessageBox.Show(
                        copyPromptMessage,
                        "Copy Test Case Generation Prompt for External LLM?",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);

                    if (copyResult == System.Windows.MessageBoxResult.Yes)
                    {
                        CopyTestCasePromptToClipboard(workspaceSlug, prompt);
                        
                        System.Windows.MessageBox.Show(
                            "✅ Test case generation prompt copied to clipboard!\n\n" +
                            "You can now:\n" +
                            "1. Paste the prompt into an external LLM (ChatGPT, Claude, etc.)\n" +
                            "2. Get the test case generation response\n" +
                            "3. Use 'Import External LLM Response' → '⚡ Parse as Test Cases' to import the results\n\n" +
                            "The generation has been cancelled for now.",
                            "Prompt Copied Successfully",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                    }

                    return TimeoutChoice.Cancel;
                }
            });
        }

        /// <summary>
        /// Copies the test case generation prompt to clipboard for external LLM use
        /// </summary>
        private void CopyTestCasePromptToClipboard(string workspaceSlug, string prompt)
        {
            try
            {
                var clipboardContent = new System.Text.StringBuilder();
                clipboardContent.AppendLine("=== TEST CASE GENERATION PROMPT ===");
                clipboardContent.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                clipboardContent.AppendLine($"Workspace: {workspaceSlug}");
                clipboardContent.AppendLine();
                clipboardContent.AppendLine("=== GENERATION PROMPT ===");
                clipboardContent.AppendLine(prompt);
                clipboardContent.AppendLine();
                clipboardContent.AppendLine("=== INSTRUCTIONS FOR EXTERNAL LLM ===");
                clipboardContent.AppendLine("1. Copy the GENERATION PROMPT section above");
                clipboardContent.AppendLine("2. Paste it into your external LLM (ChatGPT, Claude, etc.)");
                clipboardContent.AppendLine("3. The LLM should respond with JSON containing generated test cases");
                clipboardContent.AppendLine("4. Copy the LLM response and use 'Import External LLM Response' → '⚡ Parse as Test Cases' to import the results");
                clipboardContent.AppendLine("5. The test cases will be automatically added to your project");

                System.Windows.Clipboard.SetText(clipboardContent.ToString());
                
                _logger.LogInformation($"[TestCaseGeneration] Test case generation prompt copied to clipboard for workspace {workspaceSlug}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[TestCaseGeneration] Failed to copy test case generation prompt to clipboard for workspace {workspaceSlug}");
                
                System.Windows.MessageBox.Show(
                    $"Failed to copy prompt to clipboard: {ex.Message}\n\nYou can manually copy the prompt from the application logs.",
                    "Copy Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Offers workspace context help and prompt export when AnythingLLM workspace is not available
        /// </summary>
        private async Task OfferWorkspaceContextHelpAsync(string prompt)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var helpMessage = "⚠️ No AnythingLLM workspace context found.\n\n" +
                                "This can happen when:\n" +
                                "• No project is open in the application\n" +
                                "• AnythingLLM is not running or accessible\n" +
                                "• The project workspace hasn't been created in AnythingLLM\n\n" +
                                "Would you like to copy the test case generation prompt for use in an external LLM?";

                var result = System.Windows.MessageBox.Show(
                    helpMessage,
                    "Workspace Context Missing - Use External LLM?",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    CopyTestCasePromptToClipboard("external-llm", prompt);
                    
                    System.Windows.MessageBox.Show(
                        "✅ Test case generation prompt copied to clipboard!\n\n" +
                        "You can now:\n" +
                        "1. Paste the prompt into an external LLM (ChatGPT, Claude, etc.)\n" +
                        "2. Get the test case generation response\n" +
                        "3. Use 'Import External LLM Response' → '⚡ Parse as Test Cases' to import the results\n\n" +
                        "To fix the workspace context issue:\n" +
                        "• Ensure AnythingLLM is running\n" +
                        "• Open or create a project in the application\n" +
                        "• Check that the project workspace exists in AnythingLLM",
                        "Prompt Copied - Next Steps",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            });
        }

        /// <summary>
        /// Offers prompt export when generation fails for other reasons
        /// </summary>
        private async Task OfferPromptExportForErrorAsync(string prompt, string errorMessage)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var exportMessage = $"❌ Test case generation failed: {errorMessage}\n\n" +
                                  "Would you like to copy the generation prompt for use in an external LLM?\n\n" +
                                  "This will allow you to:\n" +
                                  "• Get test case generation from ChatGPT, Claude, or other LLMs\n" +
                                  "• Import the results using '⚡ Parse as Test Cases'\n" +
                                  "• Continue your work despite the internal generation failure";

                var result = System.Windows.MessageBox.Show(
                    exportMessage,
                    "Generation Failed - Use External LLM?",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    CopyTestCasePromptToClipboard("external-llm-failed", prompt);
                    
                    System.Windows.MessageBox.Show(
                        "✅ Test case generation prompt copied to clipboard!\n\n" +
                        "You can now:\n" +
                        "1. Paste the prompt into an external LLM (ChatGPT, Claude, etc.)\n" +
                        "2. Get the test case generation response\n" +
                        "3. Use 'Import External LLM Response' → '⚡ Parse as Test Cases' to import the results\n\n" +
                        "The generation has been cancelled for now.",
                        "Prompt Copied Successfully",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            });
        }

        /// <summary>
        /// Offers prompt export when LLM returns empty response (often due to timeout/processing issues)
        /// </summary>
        private async Task OfferPromptExportForEmptyResponseAsync(string workspaceSlug, string prompt, TimeSpan duration)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var durationSeconds = Math.Round(duration.TotalSeconds, 1);
                var emptyResponseMessage = $"⚠️ LLM returned empty response after {durationSeconds} seconds.\n\n" +
                                          $"Workspace: {workspaceSlug}\n" +
                                          $"This commonly happens when:\n" +
                                          "• The LLM request timed out internally\n" +
                                          "• The LLM is overloaded or unavailable\n" +
                                          "• Network connectivity issues occurred\n" +
                                          "• The workspace configuration has issues\n\n" +
                                          "Would you like to copy the generation prompt for use in an external LLM?";

                var result = System.Windows.MessageBox.Show(
                    emptyResponseMessage,
                    "Empty LLM Response - Use External LLM?",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    CopyTestCasePromptToClipboard(workspaceSlug, prompt);
                    
                    System.Windows.MessageBox.Show(
                        "✅ Test case generation prompt copied to clipboard!\n\n" +
                        "You can now:\n" +
                        "1. Paste the prompt into an external LLM (ChatGPT, Claude, etc.)\n" +
                        "2. Get the test case generation response\n" +
                        "3. Use 'Import External LLM Response' → '⚡ Parse as Test Cases' to import the results\n\n" +
                        "Tips for external LLM use:\n" +
                        "• Make sure to copy the entire JSON response\n" +
                        "• The response should contain a 'testCases' array\n" +
                        "• Use the Parse button to import the results back into the app",
                        "Prompt Copied Successfully",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            });
        }

        /// <summary>
        /// User choice when timeout occurs
        /// </summary>
        private enum TimeoutChoice
        {
            Continue,
            Cancel
        }

        /// <summary>
        /// Gets the workspace slug from the project context
        /// </summary>
        private async Task<string?> GetWorkspaceSlugAsync()
        {
            if (string.IsNullOrEmpty(_projectWorkspaceName))
            {
                _logger.LogWarning("[TestCaseGeneration] No project workspace name set");
                return null;
            }

            try
            {
                // Get all workspaces and find the one matching our project
                var workspaces = await _anythingLLMService.GetWorkspacesAsync();
                
                // Log all workspace names for diagnostic purposes
                if (workspaces != null && workspaces.Any())
                {
                    var workspaceNames = string.Join(", ", workspaces.Select(w => $"'{w.Name}'"));
                    _logger.LogInformation("[TestCaseGeneration] Available workspaces: {WorkspaceNames}", workspaceNames);
                }
                else
                {
                    _logger.LogWarning("[TestCaseGeneration] No workspaces found in AnythingLLM");
                    return null;
                }
                
                _logger.LogInformation("[TestCaseGeneration] Searching for workspace matching project: '{ProjectName}'", _projectWorkspaceName);
                
                AnythingLLMService.Workspace? targetWorkspace = null;
                
                if (workspaces != null)
                {
                    // Look for exact project workspace match first
                    targetWorkspace = workspaces.FirstOrDefault(w => 
                        string.Equals(w.Name, _projectWorkspaceName, StringComparison.OrdinalIgnoreCase));
                    
                    _logger.LogDebug("[TestCaseGeneration] Exact match found: {Found}", targetWorkspace != null);
                    
                    // If no exact match, try fuzzy matching for common variations
                    if (targetWorkspace == null)
                    {
                        var normalizedProjectName = _projectWorkspaceName.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
                        _logger.LogDebug("[TestCaseGeneration] No exact match, trying fuzzy match for normalized name: '{NormalizedName}'", normalizedProjectName);
                        
                        // Try exact fuzzy match first
                        targetWorkspace = workspaces.FirstOrDefault(w => 
                        {
                            var normalizedWorkspaceName = w.Name.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
                            return string.Equals(normalizedWorkspaceName, normalizedProjectName, StringComparison.OrdinalIgnoreCase);
                        });
                        
                        // If still no match, try partial matching (workspace name is contained in project name or vice versa)
                        if (targetWorkspace == null)
                        {
                            _logger.LogDebug("[TestCaseGeneration] No exact fuzzy match, trying partial matching");
                            targetWorkspace = workspaces.FirstOrDefault(w => 
                            {
                                var normalizedWorkspaceName = w.Name.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
                                // Check if workspace name is a substring of project name or project name contains workspace name
                                return normalizedProjectName.Contains(normalizedWorkspaceName) || normalizedWorkspaceName.Contains(normalizedProjectName);
                            });
                            
                            if (targetWorkspace != null)
                            {
                                _logger.LogDebug("[TestCaseGeneration] Partial match found: '{WorkspaceName}'", targetWorkspace.Name);
                            }
                        }
                        else
                        {
                            _logger.LogDebug("[TestCaseGeneration] Exact fuzzy match found: '{WorkspaceName}'", targetWorkspace.Name);
                        }
                    }
                }

                if (targetWorkspace != null)
                {
                    _logger.LogInformation("[TestCaseGeneration] Using workspace: {Name} (Slug: {Slug})", 
                        targetWorkspace.Name, targetWorkspace.Slug);
                    return targetWorkspace.Slug;
                }

                _logger.LogWarning("[TestCaseGeneration] No workspace found matching project name: {Name}", 
                    _projectWorkspaceName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TestCaseGeneration] Error resolving workspace slug");
                return null;
            }
        }

        private class TestCaseGenerationResponse
        {
            public List<LLMTestCase> TestCases { get; set; } = new List<LLMTestCase>();
        }
    }
}
