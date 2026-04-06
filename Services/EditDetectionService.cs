using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for detecting significant edits to LLM-generated text
    /// Triggers learning feedback when user makes substantial improvements
    /// </summary>
    public interface IEditDetectionService
    {
        /// <summary>
        /// Process a text edit to determine if it represents significant changes that could provide learning value
        /// </summary>
        /// <param name="originalText">Original text (e.g., from LLM suggestion)</param>
        /// <param name="editedText">User-edited version</param>
        /// <param name="context">Context about the edit (e.g., "requirement rewrite", "analysis suggestion")</param>
        /// <returns>True if feedback was triggered, false if no significant change detected</returns>
        Task<bool> ProcessTextEditAsync(string originalText, string editedText, string context);
    }

    /// <summary>
    /// Implementation of edit detection service
    /// Coordinates between text similarity detection and learning feedback
    /// </summary>
    public class EditDetectionService : IEditDetectionService
    {
        private readonly ITextSimilarityService _textSimilarityService;
        private readonly ILLMLearningService _learningService;
        private readonly ILogger<EditDetectionService> _logger;

        public EditDetectionService(
            ITextSimilarityService textSimilarityService,
            ILLMLearningService learningService,
            ILogger<EditDetectionService> logger)
        {
            _textSimilarityService = textSimilarityService ?? throw new ArgumentNullException(nameof(textSimilarityService));
            _learningService = learningService ?? throw new ArgumentNullException(nameof(learningService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> ProcessTextEditAsync(string originalText, string editedText, string context)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(originalText) || string.IsNullOrWhiteSpace(editedText))
                {
                    _logger.LogDebug("Skipping edit detection - empty text provided");
                    return false;
                }

                // Check if learning feedback is available
                var learningAvailable = await _learningService.IsLearningFeedbackAvailableAsync();
                if (!learningAvailable)
                {
                    _logger.LogDebug("Learning feedback not available - skipping edit detection");
                    return false;
                }

                // Calculate changes
                var exceedsThreshold = _textSimilarityService.ExceedsChangeThreshold(originalText, editedText, 15.0);
                
                if (!exceedsThreshold)
                {
                    _logger.LogDebug("Edit does not exceed 15% threshold - no learning feedback triggered");
                    return false;
                }

                var similarity = _textSimilarityService.CalculateSimilarityPercentage(originalText, editedText);
                var changePercentage = 100.0 - similarity;

                _logger.LogInformation("Significant edit detected - {ChangePercentage}% changes in {Context}, triggering learning feedback prompt", 
                    changePercentage, context);

                // Prompt user for consent and send feedback
                var (userConsent, feedback) = await _learningService.PromptUserForLearningConsentAsync(
                    originalText, editedText, changePercentage);

                if (userConsent && feedback != null)
                {
                    // Add context information
                    feedback.Context = context;

                    var success = await _learningService.SendLearningFeedbackAsync(feedback);
                    if (success)
                    {
                        _logger.LogInformation("Learning feedback sent successfully for {ChangePercentage}% change in {Context}", changePercentage, context);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to send learning feedback for {Context}", context);
                    }
                    
                    return success;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing text edit for learning feedback in {Context}", context);
                return false;
            }
        }
    }
}