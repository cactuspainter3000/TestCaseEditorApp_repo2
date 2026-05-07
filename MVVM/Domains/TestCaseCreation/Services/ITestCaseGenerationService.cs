using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services
{
    /// <summary>
    /// Result of test case generation including diagnostics
    /// </summary>
    public record TestCaseGenerationResult(
        List<LLMTestCase> TestCases,
        string GeneratedPrompt,
        string LLMResponse);

    /// <summary>
    /// Service for generating test cases from requirements using LLM.
    /// Automatically detects requirement overlap and creates shared test cases when appropriate.
    /// </summary>
    public interface ITestCaseGenerationService
    {
        /// <summary>
        /// Generate test cases for a batch of requirements.
        /// The LLM will automatically detect overlapping/similar requirements and create
        /// shared test cases with multiple CoveredRequirementIds when appropriate.
        /// </summary>
        /// <param name="requirements">Requirements to generate test cases for</param>
        /// <param name="progressCallback">Optional callback for progress updates</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>
        /// List of generated test cases with CoveredRequirementIds populated.
        /// Some test cases may cover multiple requirements if they are similar.
        /// </returns>
        Task<List<LLMTestCase>> GenerateTestCasesAsync(
            IEnumerable<Requirement> requirements,
            Action<string, int, int>? progressCallback = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generate test cases with full diagnostic information including prompt and response.
        /// Use this for debugging and prompt analysis.
        /// </summary>
        Task<TestCaseGenerationResult> GenerateTestCasesWithDiagnosticsAsync(
            IEnumerable<Requirement> requirements,
            Action<string, int, int>? progressCallback = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generate test cases for a single requirement.
        /// Useful when adding test cases to one requirement at a time.
        /// </summary>
        /// <param name="requirement">Requirement to generate test cases for</param>
        /// <param name="progressCallback">Optional callback for progress updates</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of generated test cases covering this requirement</returns>
        Task<List<LLMTestCase>> GenerateTestCasesForSingleRequirementAsync(
            Requirement requirement,
            Action<string, int, int>? progressCallback = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Calculate coverage statistics for requirements and their test cases.
        /// Shows which requirements have test cases and how many.
        /// </summary>
        /// <param name="requirements">Requirements to analyze</param>
        /// <param name="testCases">Generated test cases</param>
        /// <returns>Coverage summary with statistics and relationships</returns>
        TestCaseCoverageSummary CalculateCoverage(
            IEnumerable<Requirement> requirements,
            IEnumerable<LLMTestCase> testCases);

        /// <summary>
        /// Validate that a generated test case properly covers its assigned requirements.
        /// Returns validation issues if coverage is insufficient.
        /// </summary>
        /// <param name="testCase">Test case to validate</param>
        /// <param name="requirements">Requirements it claims to cover</param>
        /// <returns>List of validation issues, empty if valid</returns>
        List<string> ValidateTestCaseCoverage(
            LLMTestCase testCase,
            IEnumerable<Requirement> requirements);

        /// <summary>
        /// Sets the workspace context for project-specific test case generation
        /// </summary>
        /// <param name="workspaceName">Name of the project workspace to use</param>
        void SetWorkspaceContext(string? workspaceName);
        
        /// <summary>
        /// Gets whether the service has a valid workspace context configured
        /// </summary>
        bool HasWorkspaceContext { get; }
    }
}
