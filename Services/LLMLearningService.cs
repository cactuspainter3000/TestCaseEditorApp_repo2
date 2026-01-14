using System;
using System.Linq;
using System.Text;
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
        private readonly AnythingLLMService _anythingLLMService;
        private const string LEARNING_WORKSPACE_NAME = "test-case-editor-learning";

        public LLMLearningService(
            ILogger<LLMLearningService> logger,
            AnythingLLMService anythingLLMService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
        }

        public async Task<bool> SendLearningFeedbackAsync(LLMLearningFeedback feedback)
        {
            try
            {
                _logger.LogInformation("Sending learning feedback to AnythingLLM for requirement {RequirementId}", 
                    feedback.RequirementId);

                // Ensure we have a learning workspace
                var workspaceSlug = await EnsureLearningWorkspaceAsync();
                if (string.IsNullOrEmpty(workspaceSlug))
                {
                    _logger.LogWarning("Failed to create or find learning workspace");
                    return false;
                }

                // Create learning feedback message
                var learningMessage = CreateLearningFeedbackMessage(feedback);
                
                // Send feedback as chat message to learning workspace
                var response = await _anythingLLMService.SendChatMessageAsync(
                    workspaceSlug, 
                    learningMessage);

                if (!string.IsNullOrEmpty(response))
                {
                    _logger.LogInformation("Learning feedback sent successfully to workspace {Workspace}", workspaceSlug);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Failed to send learning feedback message to workspace {Workspace}", workspaceSlug);
                    return false;
                }
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

        /// <summary>
        /// Ensures that a learning workspace exists, creating one if necessary
        /// </summary>
        private async Task<string?> EnsureLearningWorkspaceAsync()
        {
            try
            {
                // Check if learning workspace already exists
                var workspaces = await _anythingLLMService.GetWorkspacesAsync();
                var learningWorkspace = workspaces.FirstOrDefault(w => 
                    w.Name.Equals(LEARNING_WORKSPACE_NAME, StringComparison.OrdinalIgnoreCase));

                if (learningWorkspace != null)
                {
                    _logger.LogDebug("Found existing learning workspace: {Slug}", learningWorkspace.Slug);
                    return learningWorkspace.Slug;
                }

                // Create new learning workspace
                _logger.LogInformation("Creating new learning workspace: {Name}", LEARNING_WORKSPACE_NAME);
                var newWorkspace = await _anythingLLMService.CreateWorkspaceAsync(LEARNING_WORKSPACE_NAME);
                
                if (newWorkspace != null && !string.IsNullOrEmpty(newWorkspace.Slug))
                {
                    _logger.LogInformation("Created learning workspace: {Slug}", newWorkspace.Slug);
                    return newWorkspace.Slug;
                }

                _logger.LogWarning("Failed to create learning workspace");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring learning workspace exists");
                return null;
            }
        }

        /// <summary>
        /// Creates a structured learning feedback message for AnythingLLM
        /// </summary>
        private string CreateLearningFeedbackMessage(LLMLearningFeedback feedback)
        {
            var messageBuilder = new StringBuilder();
            
            messageBuilder.AppendLine("LEARNING FEEDBACK FROM TEST CASE EDITOR");
            messageBuilder.AppendLine("========================================");
            messageBuilder.AppendLine();
            messageBuilder.AppendLine($"**Feedback Category:** {feedback.FeedbackCategory ?? "User Improvement"}");
            messageBuilder.AppendLine($"**Requirement ID:** {feedback.RequirementId ?? "N/A"}");
            messageBuilder.AppendLine($"**Change Percentage:** {feedback.ChangePercentage:F1}%");
            messageBuilder.AppendLine($"**Timestamp:** {feedback.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            
            if (!string.IsNullOrEmpty(feedback.Context))
            {
                messageBuilder.AppendLine($"**Context:** {feedback.Context}");
            }
            
            messageBuilder.AppendLine();
            messageBuilder.AppendLine("**ORIGINAL REQUIREMENT:**");
            messageBuilder.AppendLine($"```");
            messageBuilder.AppendLine(feedback.OriginalRequirement ?? "N/A");
            messageBuilder.AppendLine("```");
            messageBuilder.AppendLine();
            
            messageBuilder.AppendLine("**LLM GENERATED REWRITE:**");
            messageBuilder.AppendLine("```");
            messageBuilder.AppendLine(feedback.LLMGeneratedRewrite ?? "N/A");
            messageBuilder.AppendLine("```");
            messageBuilder.AppendLine();
            
            messageBuilder.AppendLine("**USER IMPROVED VERSION:**");
            messageBuilder.AppendLine("```");
            messageBuilder.AppendLine(feedback.UserImprovedVersion ?? "N/A");
            messageBuilder.AppendLine("```");
            
            if (!string.IsNullOrEmpty(feedback.UserComments))
            {
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("**USER COMMENTS:**");
                messageBuilder.AppendLine(feedback.UserComments);
            }
            
            messageBuilder.AppendLine();
            messageBuilder.AppendLine("Please learn from this feedback to improve future requirement rewrites. " +
                "Pay attention to what the user improved and try to incorporate similar patterns in future analyses.");
            
            return messageBuilder.ToString();
        }
    }
}