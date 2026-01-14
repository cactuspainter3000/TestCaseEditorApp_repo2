namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for calculating text similarity between original and edited content
    /// Used for detecting significant user edits to LLM-generated text
    /// </summary>
    public interface ITextSimilarityService
    {
        /// <summary>
        /// Calculate similarity percentage between original and modified text
        /// </summary>
        /// <param name="originalText">Original text (e.g., LLM-generated rewrite)</param>
        /// <param name="modifiedText">User-modified text</param>
        /// <returns>Similarity percentage (0-100). Lower values indicate more changes.</returns>
        double CalculateSimilarityPercentage(string originalText, string modifiedText);

        /// <summary>
        /// Check if the edit exceeds the threshold for learning feedback
        /// </summary>
        /// <param name="originalText">Original LLM text</param>
        /// <param name="modifiedText">User-edited text</param>
        /// <param name="threshold">Threshold percentage (default 15%)</param>
        /// <returns>True if changes exceed threshold (should trigger learning feedback)</returns>
        bool ExceedsChangeThreshold(string originalText, string modifiedText, double threshold = 15.0);
    }
}