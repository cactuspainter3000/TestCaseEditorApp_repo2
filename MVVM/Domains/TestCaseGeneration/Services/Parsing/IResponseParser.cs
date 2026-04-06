using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.Parsing
{
    /// <summary>
    /// Interface for parsing different types of LLM analysis responses.
    /// Supports multiple response formats: JSON, natural language, self-reflection.
    /// </summary>
    public interface IResponseParser
    {
        /// <summary>
        /// Checks if this parser can handle the given response format.
        /// </summary>
        /// <param name="response">Raw LLM response text</param>
        /// <returns>True if this parser can handle the response format</returns>
        bool CanParse(string response);

        /// <summary>
        /// Parses the LLM response into a structured RequirementAnalysis object.
        /// </summary>
        /// <param name="response">Raw LLM response text</param>
        /// <param name="requirementId">ID of the requirement being analyzed (for logging)</param>
        /// <returns>Parsed RequirementAnalysis or null if parsing fails</returns>
        RequirementAnalysis? ParseResponse(string response, string requirementId);

        /// <summary>
        /// Parser type name for logging and debugging.
        /// </summary>
        string ParserName { get; }
    }
}