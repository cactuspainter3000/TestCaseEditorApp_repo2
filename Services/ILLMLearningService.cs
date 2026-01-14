using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for sending learning feedback to AnythingLLM
    /// Enables continuous improvement of LLM requirement analysis
    /// </summary>
    public interface ILLMLearningService
    {
        /// <summary>
        /// Send learning feedback to AnythingLLM for model improvement
        /// </summary>
        /// <param name="feedback">Feedback data containing original and improved versions</param>
        /// <returns>True if feedback was successfully sent</returns>
        Task<bool> SendLearningFeedbackAsync(LLMLearningFeedback feedback);

        /// <summary>
        /// Check if learning feedback is available/enabled
        /// </summary>
        /// <returns>True if AnythingLLM supports learning feedback</returns>
        Task<bool> IsLearningFeedbackAvailableAsync();

        /// <summary>
        /// Prompt user for consent to send learning feedback
        /// </summary>
        /// <param name="originalText">Original LLM text</param>
        /// <param name="userEditedText">User's improved version</param>
        /// <param name="changePercentage">Percentage of changes made</param>
        /// <returns>User consent and optional feedback data</returns>
        Task<(bool userConsent, LLMLearningFeedback? feedback)> PromptUserForLearningConsentAsync(
            string originalText, 
            string userEditedText, 
            double changePercentage);
    }
}