using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services
{
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
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>
        /// List of generated test cases with CoveredRequirementIds populated.
        /// Some test cases may cover multiple requirements if they are similar.
        /// </returns>
        Task<List<LLMTestCase>> GenerateTestCasesAsync(
            IEnumerable<Requirement> requirements,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generate test cases for a single requirement.
        /// Useful when adding test cases to one requirement at a time.
        /// </summary>
        /// <param name="requirement">Requirement to generate test cases for</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of generated test cases covering this requirement</returns>
        Task<List<LLMTestCase>> GenerateTestCasesForSingleRequirementAsync(
            Requirement requirement,
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
    }
}
