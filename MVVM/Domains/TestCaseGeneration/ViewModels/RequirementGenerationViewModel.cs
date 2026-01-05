using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// ViewModel responsible for generating learning prompts and ChatGPT commands for requirement analysis and test case generation.
    /// Handles prompt creation, clipboard operations, and command generation for external AI tools.
    /// </summary>
    public partial class RequirementGenerationViewModel : ObservableObject
    {
        private readonly Action<string, int> _setTransientStatus;
        private readonly Func<IEnumerable<Requirement>> _getRequirements;
        private readonly Func<Requirement?> _getCurrentRequirement;

        public RequirementGenerationViewModel(
            Func<IEnumerable<Requirement>> getRequirements,
            Func<Requirement?> getCurrentRequirement,
            Action<string, int> setTransientStatus)
        {
            _getRequirements = getRequirements ?? throw new ArgumentNullException(nameof(getRequirements));
            _getCurrentRequirement = getCurrentRequirement ?? throw new ArgumentNullException(nameof(getCurrentRequirement));
            _setTransientStatus = setTransientStatus ?? throw new ArgumentNullException(nameof(setTransientStatus));
        }

        /// <summary>
        /// Command to generate a comprehensive learning prompt for ChatGPT based on current requirements.
        /// Creates a structured prompt that teaches ChatGPT about our requirement analysis patterns and formats.
        /// </summary>
        [RelayCommand]
        private void GenerateLearningPrompt()
        {
            try
            {
                var requirements = _getRequirements()?.ToList();
                if (requirements?.Any() != true)
                {
                    _setTransientStatus("⚠️ No requirements to generate learning prompt from", 3);
                    return;
                }

                // Generate a comprehensive learning prompt
                var promptBuilder = new System.Text.StringBuilder();
                promptBuilder.AppendLine("I'm working with a requirement analysis project and would like you to learn from these requirements to help me with future analysis. Please analyze the following requirements and identify patterns, structures, and characteristics that would be useful for test case generation and requirement analysis:");
                promptBuilder.AppendLine();

                // Add requirement details
                int counter = 1;
                foreach (var req in requirements.Take(20)) // Limit to avoid huge prompts
                {
                    promptBuilder.AppendLine($"**Requirement {counter}:**");
                    promptBuilder.AppendLine($"- Item: {req.Item ?? "N/A"}");
                    promptBuilder.AppendLine($"- Name: {req.Name ?? "N/A"}");
                    if (!string.IsNullOrEmpty(req.Description))
                    {
                        promptBuilder.AppendLine($"- Description: {req.Description}");
                    }
                    if (req.Analysis?.Issues?.Any() == true)
                    {
                        promptBuilder.AppendLine($"- Analysis Issues: {string.Join("; ", req.Analysis.Issues.Take(3).Select(i => i.Description))}");
                    }
                    if (req.Analysis?.Recommendations?.Any() == true)
                    {
                        promptBuilder.AppendLine($"- Recommendations: {string.Join("; ", req.Analysis.Recommendations.Take(3).Select(r => r.Description))}");
                    }
                    promptBuilder.AppendLine();
                    counter++;
                }

                if (requirements.Count > 20)
                {
                    promptBuilder.AppendLine($"*(Plus {requirements.Count - 20} additional requirements not shown)*");
                    promptBuilder.AppendLine();
                }

                promptBuilder.AppendLine("Based on these requirements, please learn our analysis patterns for future use:");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("**ANALYSIS TRAINING:**");
                promptBuilder.AppendLine("When I send you a requirement for analysis, please always respond with EXACTLY this JSON format:");
                promptBuilder.AppendLine("{\"QualityScore\": <1-10>, \"Issues\": [{\"Category\": \"<category>\", \"Severity\": \"<High|Medium|Low>\", \"Description\": \"...\"}], \"Recommendations\": [{\"Category\": \"<category>\", \"Description\": \"...\", \"Example\": \"...\"}], \"FreeformFeedback\": \"...\"}");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("**TEST CASE TRAINING:**");
                promptBuilder.AppendLine("When I send you a requirement for test case generation, please always respond with test cases in this format:");
                promptBuilder.AppendLine("- Objective: To verify [specific requirement aspect]");
                promptBuilder.AppendLine("- Input: [what will be provided/configured]");
                promptBuilder.AppendLine("- Expected Output: [what should happen - be specific]");
                promptBuilder.AppendLine("- Pass Criteria: [how to determine success/failure]");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("**PATTERN ANALYSIS:**");
                promptBuilder.AppendLine("1. Identify common patterns and structures in the above requirements");
                promptBuilder.AppendLine("2. Note the types of analysis that would be most valuable for this domain");
                promptBuilder.AppendLine("3. Suggest test case generation strategies specific to these requirement types");
                promptBuilder.AppendLine("4. Highlight domain-specific terminology and concepts I use");
                promptBuilder.AppendLine("5. Remember these patterns for consistent future analysis");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("**FUTURE COMMUNICATION:**");
                promptBuilder.AppendLine("- When I paste a requirement and say 'ANALYZE', use the JSON format above");
                promptBuilder.AppendLine("- When I paste a requirement and say 'GENERATE TEST CASES', use the test case format above");
                promptBuilder.AppendLine("- Always be consistent with the formatting and approach you learn from this training data");

                string prompt = promptBuilder.ToString();

                // Copy to clipboard
                System.Windows.Clipboard.SetText(prompt);

                _setTransientStatus("📋 Learning prompt copied to clipboard - ready for ChatGPT!", 4);
                TestCaseEditorApp.Services.Logging.Log.Info($"[LEARNING] Generated learning prompt from {requirements.Count} requirements");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[LEARNING] Failed to generate learning prompt: {ex.Message}");
                _setTransientStatus("❌ Failed to generate learning prompt", 3);
            }
        }

        /// <summary>
        /// Command to generate an analysis command for the current requirement that can be pasted into ChatGPT.
        /// Creates a standardized "ANALYZE:" command with requirement details.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteCurrentRequirementCommands))]
        private void GenerateAnalysisCommand()
        {
            try
            {
                var currentRequirement = _getCurrentRequirement();
                if (currentRequirement == null)
                {
                    _setTransientStatus("⚠️ No requirement selected", 3);
                    return;
                }

                var command = $"ANALYZE: {currentRequirement.Item} - {currentRequirement.Name}\n\n{currentRequirement.Description}";
                System.Windows.Clipboard.SetText(command);
                
                _setTransientStatus($"📋 Analysis command copied for {currentRequirement.Item}", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[QUICK_CMD] Generated analysis command for {currentRequirement.Item}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[QUICK_CMD] Failed to generate analysis command: {ex.Message}");
                _setTransientStatus("❌ Failed to generate command", 3);
            }
        }

        /// <summary>
        /// Command to generate a test case generation command for the current requirement that can be pasted into ChatGPT.
        /// Creates a standardized "GENERATE TEST CASES:" command with requirement details.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteCurrentRequirementCommands))]
        private void GenerateTestCaseCommand()
        {
            try
            {
                var currentRequirement = _getCurrentRequirement();
                if (currentRequirement == null)
                {
                    _setTransientStatus("⚠️ No requirement selected", 3);
                    return;
                }

                var command = $"GENERATE TEST CASES: {currentRequirement.Item} - {currentRequirement.Name}\n\n{currentRequirement.Description}";
                System.Windows.Clipboard.SetText(command);
                
                _setTransientStatus($"📋 Test case command copied for {currentRequirement.Item}", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[QUICK_CMD] Generated test case command for {currentRequirement.Item}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[QUICK_CMD] Failed to generate test case command: {ex.Message}");
                _setTransientStatus("❌ Failed to generate command", 3);
            }
        }

        /// <summary>
        /// Can-execute condition for commands that require a current requirement to be selected.
        /// </summary>
        private bool CanExecuteCurrentRequirementCommands() => _getCurrentRequirement() != null;
    }
}