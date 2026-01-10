using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Implementation of LLM learning feedback service
    /// Handles user consent prompts and sending feedback to AnythingLLM
    /// </summary>
    public class LLMLearningService : ILLMLearningService
    {
        private readonly ILogger<LLMLearningService> _logger;

        public LLMLearningService(ILogger<LLMLearningService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> SendLearningFeedbackAsync(LLMLearningFeedback feedback)
        {
            try
            {
                _logger.LogInformation("Sending learning feedback to AnythingLLM for requirement {RequirementId}", 
                    feedback.RequirementId);

                // TODO: Implement actual AnythingLLM learning endpoint call
                // For now, log the feedback data
                _logger.LogInformation("LLM Learning Feedback - Change: {ChangePercentage}%, Original: {Original}, Improved: {Improved}",
                    feedback.ChangePercentage,
                    feedback.LLMGeneratedRewrite?.Substring(0, Math.Min(50, feedback.LLMGeneratedRewrite.Length)) + "...",
                    feedback.UserImprovedVersion?.Substring(0, Math.Min(50, feedback.UserImprovedVersion.Length)) + "...");

                // Simulate API call delay
                await Task.Delay(500);

                _logger.LogInformation("Learning feedback sent successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send learning feedback to AnythingLLM");
                return false;
            }
        }

        public async Task<bool> IsLearningFeedbackAvailableAsync()
        {
            try
            {
                // For now, always return true - can be enhanced to check AnythingLLM status
                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check learning feedback availability");
                return false;
            }
        }

        public async Task<(bool userConsent, LLMLearningFeedback? feedback)> PromptUserForLearningConsentAsync(
            string originalText, 
            string userEditedText, 
            double changePercentage)
        {
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var message = $"You've made significant improvements ({changePercentage:F1}% changes) to the AI-generated requirement text.\n\n" +
                                  "Would you like to send your improved version back to the AI to help it learn and provide better suggestions in the future?\n\n" +
                                  "Your feedback will help improve the AI's requirement analysis for everyone.";

                    var result = MessageBox.Show(
                        message,
                        "Help Improve AI Learning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.No);

                    if (result == MessageBoxResult.Yes)
                    {
                        var feedback = new LLMLearningFeedback
                        {
                            LLMGeneratedRewrite = originalText,
                            UserImprovedVersion = userEditedText,
                            ChangePercentage = changePercentage,
                            FeedbackCategory = "User Improvement",
                            CreatedAt = DateTime.UtcNow
                        };

                        _logger.LogInformation("User consented to send learning feedback with {ChangePercentage}% changes", 
                            changePercentage);

                        return (true, feedback);
                    }
                    else
                    {
                        _logger.LogInformation("User declined to send learning feedback");
                        return (false, (LLMLearningFeedback?)null);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error prompting user for learning consent");
                    return (false, (LLMLearningFeedback?)null);
                }
            });
        }
    }
}