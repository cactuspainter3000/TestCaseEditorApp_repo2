using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        private const string WORKSPACE_SLUG = "test-case-generation";

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

            _logger.LogInformation("Generating test cases for {Count} requirements using workspace '{Workspace}'", 
                requirementList.Count, WORKSPACE_SLUG);

            var stopwatch = Stopwatch.StartNew();
            string generatedPrompt = string.Empty;
            string llmResponse = string.Empty;
            
            try
            {
                // Ensure RAG is configured before generation
                if (_ragContextService != null)
                {
                    progressCallback?.Invoke("Ensuring RAG documents are configured...", 0, requirementList.Count);
                    var ragConfigured = await _ragContextService.EnsureRAGConfiguredAsync(WORKSPACE_SLUG);
                    if (!ragConfigured)
                    {
                        _logger.LogWarning("[TestCaseGeneration] RAG configuration failed, proceeding without RAG context");
                    }
                }

                progressCallback?.Invoke("Preparing test case generation prompt...", 0, requirementList.Count);
                
                // Create prompt for batch generation with similarity detection
                generatedPrompt = CreateBatchGenerationPrompt(requirementList);
                
                progressCallback?.Invoke($"Sending to LLM workspace '{WORKSPACE_SLUG}' (this may take 1-2 minutes)...", 0, requirementList.Count);
                
                // Send to LLM with RAG context
                llmResponse = await _anythingLLMService.SendChatMessageAsync(
                    WORKSPACE_SLUG,
                    generatedPrompt,
                    cancellationToken);

                stopwatch.Stop();

                // Track RAG request
                _ragContextService?.TrackRAGRequest(WORKSPACE_SLUG, generatedPrompt, llmResponse, !string.IsNullOrEmpty(llmResponse), stopwatch.Elapsed);

                if (string.IsNullOrEmpty(llmResponse))
                {
                    var errorMsg = $"LLM did not return a response. Possible causes: " +
                        $"1) Workspace '{WORKSPACE_SLUG}' does not exist in AnythingLLM (create it first), " +
                        "2) Timeout (LLM processing slowly - check AnythingLLM window for activity), " +
                        "3) Connection issues, " +
                        "4) LLM service error. " +
                        "Please check AnythingLLM is running and the workspace exists.";
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
                                WORKSPACE_SLUG,
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
            catch (Exception ex)
            {
                stopwatch.Stop();
                _ragContextService?.TrackRAGRequest(WORKSPACE_SLUG, "", null, false, stopwatch.Elapsed);
                
                var errorMsg = $"Test case generation failed: {ex.Message}";
                _logger.LogError(ex, "[TestCaseGeneration] {ErrorMsg}", errorMsg);
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
                progressCallback?.Invoke($"Analyzing requirement {requirement.Item}...", 0, 1);
                
                var prompt = CreateSingleRequirementPrompt(requirement);
                
                progressCallback?.Invoke($"Generating test cases for {requirement.Item}...", 0, 1);
                
                var response = await _anythingLLMService.SendChatMessageAsync(
                    WORKSPACE_SLUG,
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
6. Return ONLY valid JSON matching this schema - no explanatory text

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
      ""coveredRequirementIds"": [""REQ-1"", ""REQ-2""],
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

        private class TestCaseGenerationResponse
        {
            public List<LLMTestCase> TestCases { get; set; } = new List<LLMTestCase>();
        }
    }
}
