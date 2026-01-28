using System;
using System.Collections.Generic;
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
    /// </summary>
    public class TestCaseGenerationService : ITestCaseGenerationService
    {
        private readonly ILogger<TestCaseGenerationService> _logger;
        private readonly AnythingLLMService _anythingLLMService;
        private const string WORKSPACE_SLUG = "test-case-generation";

        public TestCaseGenerationService(
            ILogger<TestCaseGenerationService> logger,
            AnythingLLMService anythingLLMService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
        }

        public async Task<List<LLMTestCase>> GenerateTestCasesAsync(
            IEnumerable<Requirement> requirements,
            Action<string, int, int>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            var requirementList = requirements.ToList();
            if (!requirementList.Any())
                return new List<LLMTestCase>();

            _logger.LogInformation("Generating test cases for {Count} requirements", requirementList.Count);

            try
            {
                progressCallback?.Invoke("Preparing test case generation prompt...", 0, requirementList.Count);
                
                // Create prompt for batch generation with similarity detection
                var prompt = CreateBatchGenerationPrompt(requirementList);
                
                progressCallback?.Invoke("Sending to LLM for test case generation...", 0, requirementList.Count);
                
                // Send to LLM with RAG context
                var response = await _anythingLLMService.SendChatMessageAsync(
                    WORKSPACE_SLUG,
                    prompt,
                    cancellationToken);

                if (string.IsNullOrEmpty(response))
                {
                    _logger.LogWarning("Empty response from LLM for test case generation");
                    progressCallback?.Invoke("No response from LLM", requirementList.Count, requirementList.Count);
                    return new List<LLMTestCase>();
                }

                progressCallback?.Invoke("Parsing test cases from response...", requirementList.Count, requirementList.Count);
                
                // Parse JSON response into test cases
                var testCases = ParseTestCasesFromResponse(response, requirementList);
                
                progressCallback?.Invoke($"Completed: Generated {testCases.Count} test cases", requirementList.Count, requirementList.Count);
                
                _logger.LogInformation("Generated {Count} test cases covering {ReqCount} requirements",
                    testCases.Count, requirementList.Count);
                
                return testCases;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate test cases");
                return new List<LLMTestCase>();
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
                    _logger.LogWarning("Empty response from LLM for requirement {Id}", requirement.Item);
                    return new List<LLMTestCase>();
                }

                var testCases = ParseTestCasesFromResponse(response, new[] { requirement });
                
                _logger.LogInformation("Generated {Count} test cases for requirement {Id}",
                    testCases.Count, requirement.Item);
                
                return testCases;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate test cases for requirement {Id}", requirement.Item);
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
5. Return ONLY valid JSON matching this schema - no explanatory text

RESPOND WITH JSON ONLY:
{{
  ""testCases"": [
    {{
      ""id"": ""TC-001"",
      ""title"": ""Test case title"",
      ""description"": ""What this test validates"",
      ""preconditions"": ""Setup required"",
      ""steps"": [
        {{
          ""stepNumber"": 1,
          ""action"": ""Do something"",
          ""expectedResult"": ""Expected outcome"",
          ""testData"": ""Data needed (optional)""
        }}
      ],
      ""expectedResult"": ""Overall expected outcome"",
      ""postconditions"": ""Cleanup (optional)"",
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
3. Return ONLY valid JSON matching this schema - no explanatory text

RESPOND WITH JSON ONLY:
{{
  ""testCases"": [
    {{
      ""id"": ""TC-001"",
      ""title"": ""Test case title"",
      ""description"": ""What this test validates"",
      ""preconditions"": ""Setup required"",
      ""steps"": [
        {{
          ""stepNumber"": 1,
          ""action"": ""Do something"",
          ""expectedResult"": ""Expected outcome"",
          ""testData"": ""Data needed (optional)""
        }}
      ],
      ""expectedResult"": ""Overall expected outcome"",
      ""postconditions"": ""Cleanup (optional)"",
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
                // Extract JSON from response (LLM might include markdown code fences)
                var json = ExtractJsonFromResponse(response);
                
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var result = JsonSerializer.Deserialize<TestCaseGenerationResponse>(json, options);
                
                if (result?.TestCases == null)
                {
                    _logger.LogWarning("Failed to parse test cases from response");
                    return new List<LLMTestCase>();
                }

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
                        }
                    }
                }

                return result.TestCases;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse test cases from response");
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
