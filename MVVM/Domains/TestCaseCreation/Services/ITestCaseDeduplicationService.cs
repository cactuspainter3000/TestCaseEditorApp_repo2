using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services
{
    /// <summary>
    /// Service for detecting similar requirements and deduplicating test cases.
    /// Uses LLM to analyze semantic similarity and suggest when requirements can share test cases.
    /// </summary>
    public interface ITestCaseDeduplicationService
    {
        /// <summary>
        /// Analyze requirements to find groups that are similar enough to share test cases.
        /// Returns clusters of requirement IDs that should be tested together.
        /// </summary>
        /// <param name="requirements">Requirements to analyze for similarity</param>
        /// <param name="similarityThreshold">
        /// Similarity threshold (0.0-1.0). Higher values require more similarity.
        /// Default 0.7 means 70% similar or more.
        /// </param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>
        /// Groups of similar requirement IDs. Each group should share test cases.
        /// Example: [["REQ-1", "REQ-2"], ["REQ-5", "REQ-7", "REQ-8"]]
        /// </returns>
        Task<List<List<string>>> FindSimilarRequirementGroupsAsync(
            IEnumerable<Requirement> requirements,
            double similarityThreshold = 0.7,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Determine if two requirements are similar enough to share test cases.
        /// Uses LLM to analyze semantic similarity.
        /// </summary>
        /// <param name="requirement1">First requirement</param>
        /// <param name="requirement2">Second requirement</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>
        /// Similarity score (0.0-1.0) and explanation.
        /// Score >= 0.7 typically indicates they should share test cases.
        /// </returns>
        Task<(double similarityScore, string explanation)> CalculateSimilarityAsync(
            Requirement requirement1,
            Requirement requirement2,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Merge test cases that are duplicates or near-duplicates.
        /// When multiple test cases cover the same scenarios, combine them into one
        /// with all covered requirement IDs.
        /// </summary>
        /// <param name="testCases">Test cases to deduplicate</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>
        /// Deduplicated list of test cases with merged CoveredRequirementIds.
        /// Example: If TC-1 and TC-2 test the same thing, returns one test case
        /// with both sets of covered requirement IDs.
        /// </returns>
        Task<List<LLMTestCase>> DeduplicateTestCasesAsync(
            IEnumerable<LLMTestCase> testCases,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Suggest which existing test case should be used for a new requirement
        /// instead of generating a new one.
        /// </summary>
        /// <param name="requirement">New requirement to add coverage for</param>
        /// <param name="existingTestCases">Existing test cases</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>
        /// Recommended test case ID and confidence score (0.0-1.0).
        /// Returns null if no suitable existing test case found.
        /// </returns>
        Task<(string? testCaseId, double confidence)?> SuggestExistingTestCaseAsync(
            Requirement requirement,
            IEnumerable<LLMTestCase> existingTestCases,
            CancellationToken cancellationToken = default);
    }
}
