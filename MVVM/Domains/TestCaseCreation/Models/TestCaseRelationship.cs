using System.Collections.Generic;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models
{
    /// <summary>
    /// Represents the many-to-many relationship between requirements and generated test cases.
    /// This helps track coverage and identify which requirements are validated by which test cases.
    /// </summary>
    public class RequirementTestCaseMapping
    {
        /// <summary>
        /// Requirement ID (e.g., "DECAGON-REQ_RC-5")
        /// </summary>
        public string RequirementId { get; set; } = string.Empty;

        /// <summary>
        /// List of test case IDs that cover this requirement
        /// </summary>
        public List<string> TestCaseIds { get; set; } = new List<string>();

        /// <summary>
        /// Coverage percentage (0-100)
        /// 100 = fully covered, < 100 = partially covered, 0 = not covered
        /// </summary>
        public int CoveragePercentage { get; set; } = 0;

        /// <summary>
        /// Notes about the coverage relationship
        /// (e.g., "Requires additional manual testing for edge cases")
        /// </summary>
        public string Notes { get; set; } = string.Empty;
    }

    /// <summary>
    /// Summary statistics for test case coverage across all requirements
    /// </summary>
    public class TestCaseCoverageSummary
    {
        /// <summary>
        /// Total number of requirements
        /// </summary>
        public int TotalRequirements { get; set; }

        /// <summary>
        /// Number of requirements with at least one test case
        /// </summary>
        public int CoveredRequirements { get; set; }

        /// <summary>
        /// Number of requirements with no test cases
        /// </summary>
        public int UncoveredRequirements { get; set; }

        /// <summary>
        /// Total number of test cases generated
        /// </summary>
        public int TotalTestCases { get; set; }

        /// <summary>
        /// Average number of test cases per requirement
        /// </summary>
        public double AverageTestCasesPerRequirement { get; set; }

        /// <summary>
        /// Overall coverage percentage
        /// </summary>
        public double CoveragePercentage { get; set; }

        /// <summary>
        /// List of requirement relationships
        /// </summary>
        public List<RequirementTestCaseMapping> Relationships { get; set; } = new List<RequirementTestCaseMapping>();
    }
}
