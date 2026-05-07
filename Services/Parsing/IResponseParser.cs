using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services.Parsing
{
    /// <summary>
    /// Interface for parsing different formats of LLM responses into structured RequirementAnalysis objects.
    /// Implements chain-of-responsibility pattern for handling various response formats.
    /// </summary>
    public interface IResponseParser
    {
        /// <summary>
        /// Name of this parser (for logging and debugging purposes).
        /// </summary>
        string ParserName { get; }

        /// <summary>
        /// Check if this parser can handle the given response format.
        /// Should be a fast check (e.g., looking for specific markers or patterns).
        /// </summary>
        /// <param name="response">Raw LLM response text to check</param>
        /// <returns>True if this parser can handle the response, false otherwise</returns>
        bool CanParse(string response);

        /// <summary>
        /// Parse an LLM response into a structured RequirementAnalysis object.
        /// Should only be called if CanParse returns true for the response.
        /// </summary>
        /// <param name="response">Raw LLM response text</param>
        /// <param name="requirementId">ID of the requirement being analyzed (for logging)</param>
        /// <returns>
        /// Parsed RequirementAnalysis object if successful, or null if parsing fails.
        /// The returned object should have all required fields populated.
        /// </returns>
        RequirementAnalysis? ParseResponse(string response, string requirementId);
    }
}