using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for converting requirements and test cases to Jama format
    /// </summary>
    public interface IJamaTestCaseConversionService
    {
        /// <summary>
        /// Convert SINGLE requirement's first test case to Jama format (for debugging)
        /// </summary>
        List<JamaTestCaseRequest> ConvertSingleTestCaseToJamaFormat(Requirement requirement);

        /// <summary>
        /// Convert requirements with test cases (both AI-generated and saved) to Jama test case format
        /// </summary>
        List<JamaTestCaseRequest> ConvertAllTestCasesToJamaFormat(List<Requirement> requirements);

        /// <summary>
        /// Convert an AI-generated test case from requirement output to Jama format
        /// </summary>
        JamaTestCaseRequest? ConvertAIGeneratedTestCase(Requirement requirement);

        /// <summary>
        /// Convert a saved test case to Jama format
        /// </summary>
        JamaTestCaseRequest? ConvertSavedTestCase(TestCase savedTestCase, Requirement requirement);

        /// <summary>
        /// Parse test steps from requirement output text
        /// </summary>
        List<JamaTestStep> ParseTestStepsFromOutput(string output);

        /// <summary>
        /// Extract description/objective from requirement output
        /// </summary>
        string ExtractDescriptionFromOutput(string output);
    }
}