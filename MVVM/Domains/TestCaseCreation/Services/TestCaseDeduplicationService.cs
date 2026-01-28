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
    /// Service for detecting similar requirements and deduplicating test cases using LLM.
    /// Uses RAG-optimized prompts for semantic similarity analysis.
    /// </summary>
    public class TestCaseDeduplicationService : ITestCaseDeduplicationService
    {
        private readonly ILogger<TestCaseDeduplicationService> _logger;
        private readonly AnythingLLMService _anythingLLMService;
        private const string WORKSPACE_SLUG = "test-case-generation";

        public TestCaseDeduplicationService(
            ILogger<TestCaseDeduplicationService> logger,
            AnythingLLMService anythingLLMService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
        }

        public async Task<List<List<string>>> FindSimilarRequirementGroupsAsync(
            IEnumerable<Requirement> requirements,
            double similarityThreshold = 0.7,
            CancellationToken cancellationToken = default)
        {
            var reqList = requirements.ToList();
            if (reqList.Count < 2)
                return new List<List<string>>();

            _logger.LogInformation("Finding similar requirement groups among {Count} requirements with threshold {Threshold}",
                reqList.Count, similarityThreshold);

            try
            {
                var prompt = CreateSimilarityGroupingPrompt(reqList, similarityThreshold);
                
                var response = await _anythingLLMService.SendChatMessageAsync(
                    WORKSPACE_SLUG,
                    prompt,
                    cancellationToken);

                if (string.IsNullOrEmpty(response))
                {
                    _logger.LogWarning("Empty response from LLM for similarity grouping");
                    return new List<List<string>>();
                }

                var groups = ParseSimilarityGroupsFromResponse(response);
                
                _logger.LogInformation("Found {GroupCount} similar requirement groups", groups.Count);
                
                return groups;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to find similar requirement groups");
                return new List<List<string>>();
            }
        }

        public async Task<(double similarityScore, string explanation)> CalculateSimilarityAsync(
            Requirement requirement1,
            Requirement requirement2,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Calculating similarity between {Id1} and {Id2}",
                requirement1.Item, requirement2.Item);

            try
            {
                var prompt = CreatePairwiseSimilarityPrompt(requirement1, requirement2);
                
                var response = await _anythingLLMService.SendChatMessageAsync(
                    WORKSPACE_SLUG,
                    prompt,
                    cancellationToken);

                if (string.IsNullOrEmpty(response))
                {
                    _logger.LogWarning("Empty response from LLM for similarity calculation");
                    return (0.0, "No response from LLM");
                }

                var result = ParseSimilarityScoreFromResponse(response);
                
                _logger.LogInformation("Similarity between {Id1} and {Id2}: {Score:F2}",
                    requirement1.Item, requirement2.Item, result.score);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to calculate similarity");
                return (0.0, $"Error: {ex.Message}");
            }
        }

        public async Task<List<LLMTestCase>> DeduplicateTestCasesAsync(
            IEnumerable<LLMTestCase> testCases,
            CancellationToken cancellationToken = default)
        {
            var tcList = testCases.ToList();
            if (tcList.Count < 2)
                return tcList;

            _logger.LogInformation("Deduplicating {Count} test cases", tcList.Count);

            try
            {
                var prompt = CreateDeduplicationPrompt(tcList);
                
                var response = await _anythingLLMService.SendChatMessageAsync(
                    WORKSPACE_SLUG,
                    prompt,
                    cancellationToken);

                if (string.IsNullOrEmpty(response))
                {
                    _logger.LogWarning("Empty response from LLM for deduplication");
                    return tcList;
                }

                var deduplicated = ParseDeduplicatedTestCasesFromResponse(response);
                
                _logger.LogInformation("Deduplicated {OriginalCount} test cases to {NewCount}",
                    tcList.Count, deduplicated.Count);
                
                return deduplicated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deduplicate test cases");
                return tcList;
            }
        }

        public async Task<(string? testCaseId, double confidence)?> SuggestExistingTestCaseAsync(
            Requirement requirement,
            IEnumerable<LLMTestCase> existingTestCases,
            CancellationToken cancellationToken = default)
        {
            var tcList = existingTestCases.ToList();
            if (!tcList.Any())
                return null;

            _logger.LogInformation("Finding existing test case for requirement {Id} among {Count} test cases",
                requirement.Item, tcList.Count);

            try
            {
                var prompt = CreateTestCaseMatchingPrompt(requirement, tcList);
                
                var response = await _anythingLLMService.SendChatMessageAsync(
                    WORKSPACE_SLUG,
                    prompt,
                    cancellationToken);

                if (string.IsNullOrEmpty(response))
                {
                    _logger.LogWarning("Empty response from LLM for test case matching");
                    return null;
                }

                var match = ParseTestCaseMatchFromResponse(response);
                
                if (match.HasValue)
                {
                    _logger.LogInformation("Suggested test case {TestCaseId} for requirement {ReqId} with confidence {Confidence:F2}",
                        match.Value.testCaseId, requirement.Item, match.Value.confidence);
                }
                else
                {
                    _logger.LogInformation("No suitable existing test case found for requirement {ReqId}", requirement.Item);
                }
                
                return match;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to suggest existing test case");
                return null;
            }
        }

        private string CreateSimilarityGroupingPrompt(List<Requirement> requirements, double threshold)
        {
            var reqDetails = string.Join("\n\n", requirements.Select((r, i) =>
                $"[{i + 1}] ID: {r.Item}\n" +
                $"    Description: {r.Description}"));

            return $@"Analyze these requirements and group together those that are similar enough to share test cases. Requirements should be grouped if their similarity is >= {threshold:F1} (on a 0.0-1.0 scale).

REQUIREMENTS:
{reqDetails}

INSTRUCTIONS:
1. Compare all requirements for semantic similarity
2. Group requirements that test similar functionality or behavior
3. Each requirement can only appear in ONE group
4. Return ONLY valid JSON - no explanatory text

RESPOND WITH JSON ONLY:
{{
  ""groups"": [
    [""REQ-1"", ""REQ-3""],
    [""REQ-5"", ""REQ-7"", ""REQ-8""]
  ]
}}";
        }

        private string CreatePairwiseSimilarityPrompt(Requirement req1, Requirement req2)
        {
            return $@"Calculate the semantic similarity between these two requirements. Score from 0.0 (completely different) to 1.0 (virtually identical).

REQUIREMENT 1:
ID: {req1.Item}
Description: {req1.Description}

REQUIREMENT 2:
ID: {req2.Item}
Description: {req2.Description}

INSTRUCTIONS:
1. Analyze functionality, behavior, and testing needs
2. Score >= 0.7 means they should share test cases
3. Provide brief explanation of similarity/differences
4. Return ONLY valid JSON - no explanatory text

RESPOND WITH JSON ONLY:
{{
  ""similarityScore"": 0.85,
  ""explanation"": ""Brief explanation of why they are similar or different""
}}";
        }

        private string CreateDeduplicationPrompt(List<LLMTestCase> testCases)
        {
            var tcDetails = string.Join("\n\n", testCases.Select(tc =>
                $"ID: {tc.Id}\n" +
                $"Title: {tc.Title}\n" +
                $"Description: {tc.Description}\n" +
                $"Covers: [{string.Join(", ", tc.CoveredRequirementIds)}]"));

            return $@"Analyze these test cases and merge any that are duplicates or near-duplicates. When merging, combine the CoveredRequirementIds lists.

TEST CASES:
{tcDetails}

INSTRUCTIONS:
1. Identify test cases that validate the same functionality
2. Merge duplicates into single test cases with combined CoveredRequirementIds
3. Keep unique test cases unchanged
4. Return ONLY valid JSON with the deduplicated list - no explanatory text

RESPOND WITH JSON ONLY:
{{
  ""deduplicatedTestCases"": [
    {{
      ""id"": ""TC-001"",
      ""title"": ""Merged test case title"",
      ""description"": ""What this validates"",
      ""preconditions"": """",
      ""steps"": [],
      ""expectedResult"": """",
      ""postconditions"": """",
      ""coveredRequirementIds"": [""REQ-1"", ""REQ-2""],
      ""priority"": ""Medium"",
      ""testType"": ""Functional"",
      ""estimatedDurationMinutes"": 10,
      ""tags"": [],
      ""generationConfidence"": 80
    }}
  ]
}}";
        }

        private string CreateTestCaseMatchingPrompt(Requirement requirement, List<LLMTestCase> testCases)
        {
            var tcDetails = string.Join("\n\n", testCases.Select(tc =>
                $"ID: {tc.Id}\n" +
                $"Title: {tc.Title}\n" +
                $"Description: {tc.Description}"));

            return $@"Find the best existing test case to cover this new requirement. Return the test case ID and confidence score (0.0-1.0).

NEW REQUIREMENT:
ID: {requirement.Item}
Description: {requirement.Description}

EXISTING TEST CASES:
{tcDetails}

INSTRUCTIONS:
1. Find test case that best covers this requirement's functionality
2. Return null if no suitable test case exists (confidence < 0.6)
3. Higher confidence means better match
4. Return ONLY valid JSON - no explanatory text

RESPOND WITH JSON ONLY:
{{
  ""testCaseId"": ""TC-003"",
  ""confidence"": 0.85
}}

OR if no match:
{{
  ""testCaseId"": null,
  ""confidence"": 0.0
}}";
        }

        private List<List<string>> ParseSimilarityGroupsFromResponse(string response)
        {
            try
            {
                var json = ExtractJsonFromResponse(response);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<SimilarityGroupsResponse>(json, options);
                return result?.Groups ?? new List<List<string>>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse similarity groups from response");
                return new List<List<string>>();
            }
        }

        private (double score, string explanation) ParseSimilarityScoreFromResponse(string response)
        {
            try
            {
                var json = ExtractJsonFromResponse(response);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<SimilarityScoreResponse>(json, options);
                return (result?.SimilarityScore ?? 0.0, result?.Explanation ?? "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse similarity score from response");
                return (0.0, "Parse error");
            }
        }

        private List<LLMTestCase> ParseDeduplicatedTestCasesFromResponse(string response)
        {
            try
            {
                var json = ExtractJsonFromResponse(response);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<DeduplicationResponse>(json, options);
                return result?.DeduplicatedTestCases ?? new List<LLMTestCase>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse deduplicated test cases from response");
                return new List<LLMTestCase>();
            }
        }

        private (string? testCaseId, double confidence)? ParseTestCaseMatchFromResponse(string response)
        {
            try
            {
                var json = ExtractJsonFromResponse(response);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<TestCaseMatchResponse>(json, options);
                
                if (result?.TestCaseId == null || result.Confidence < 0.6)
                    return null;
                
                return (result.TestCaseId, result.Confidence);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse test case match from response");
                return null;
            }
        }

        private string ExtractJsonFromResponse(string response)
        {
            var cleaned = response.Trim();
            
            if (cleaned.StartsWith("```json"))
                cleaned = cleaned.Substring(7);
            else if (cleaned.StartsWith("```"))
                cleaned = cleaned.Substring(3);
            
            if (cleaned.EndsWith("```"))
                cleaned = cleaned.Substring(0, cleaned.Length - 3);
            
            return cleaned.Trim();
        }

        private class SimilarityGroupsResponse
        {
            public List<List<string>> Groups { get; set; } = new List<List<string>>();
        }

        private class SimilarityScoreResponse
        {
            public double SimilarityScore { get; set; }
            public string Explanation { get; set; } = string.Empty;
        }

        private class DeduplicationResponse
        {
            public List<LLMTestCase> DeduplicatedTestCases { get; set; } = new List<LLMTestCase>();
        }

        private class TestCaseMatchResponse
        {
            public string? TestCaseId { get; set; }
            public double Confidence { get; set; }
        }
    }
}
