using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels
{
    /// <summary>
    /// ViewModel for prompt diagnostics - displays and compares LLM prompts and responses.
    /// Allows copying prompts to test in external LLMs and compare results.
    /// </summary>
    public partial class PromptDiagnosticsViewModel : ObservableRecipient
    {
        private readonly ILogger<PromptDiagnosticsViewModel> _logger;

        [ObservableProperty]
        private string generatedPrompt = "No prompt generated yet. Generate test cases to see the prompt.";

        [ObservableProperty]
        private string anythingLLMResponse = "No response yet.";

        [ObservableProperty]
        private string externalLLMResponse = "Paste external LLM response here for comparison...";

        [ObservableProperty]
        private string comparisonResults = string.Empty;

        [ObservableProperty]
        private bool hasPrompt = false;

        [ObservableProperty]
        private bool hasAnythingLLMResponse = false;

        [ObservableProperty]
        private bool hasExternalResponse = false;

        [ObservableProperty]
        private string promptMetadata = string.Empty;

        [ObservableProperty]
        private int promptLength = 0;

        [ObservableProperty]
        private int anythingLLMResponseLength = 0;

        [ObservableProperty]
        private int externalResponseLength = 0;

        public PromptDiagnosticsViewModel(ILogger<PromptDiagnosticsViewModel> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Updates the displayed prompt with metadata
        /// </summary>
        public void UpdatePrompt(string prompt, int requirementCount, DateTime timestamp)
        {
            GeneratedPrompt = prompt;
            PromptLength = prompt?.Length ?? 0;
            HasPrompt = !string.IsNullOrEmpty(prompt);

            PromptMetadata = $"Generated: {timestamp:yyyy-MM-dd HH:mm:ss}\n" +
                           $"Requirements: {requirementCount}\n" +
                           $"Prompt Size: {PromptLength:N0} characters\n" +
                           $"Estimated Tokens: ~{PromptLength / 4:N0}";

            _logger.LogInformation("[PromptDiagnostics] Prompt updated: {Length} chars, {Requirements} requirements",
                PromptLength, requirementCount);
        }

        /// <summary>
        /// Updates the AnythingLLM response
        /// </summary>
        public void UpdateAnythingLLMResponse(string response)
        {
            AnythingLLMResponse = response ?? "Empty response from AnythingLLM";
            AnythingLLMResponseLength = response?.Length ?? 0;
            HasAnythingLLMResponse = !string.IsNullOrEmpty(response);

            _logger.LogInformation("[PromptDiagnostics] AnythingLLM response updated: {Length} chars",
                AnythingLLMResponseLength);

            // Auto-compare if both responses are available
            if (HasAnythingLLMResponse && HasExternalResponse)
            {
                CompareResponses();
            }
        }

        /// <summary>
        /// Clears the external LLM response field
        /// </summary>
        [RelayCommand]
        private void ClearExternalResponse()
        {
            ExternalLLMResponse = "Paste external LLM response here for comparison...";
            ExternalResponseLength = 0;
            HasExternalResponse = false;
            ComparisonResults = string.Empty;
        }

        /// <summary>
        /// Copies the prompt to clipboard
        /// </summary>
        [RelayCommand(CanExecute = nameof(HasPrompt))]
        private void CopyPrompt()
        {
            try
            {
                Clipboard.SetText(GeneratedPrompt);
                _logger.LogInformation("[PromptDiagnostics] Prompt copied to clipboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PromptDiagnostics] Error copying prompt to clipboard");
            }
        }

        /// <summary>
        /// Copies the AnythingLLM response to clipboard
        /// </summary>
        [RelayCommand(CanExecute = nameof(HasAnythingLLMResponse))]
        private void CopyAnythingLLMResponse()
        {
            try
            {
                Clipboard.SetText(AnythingLLMResponse);
                _logger.LogInformation("[PromptDiagnostics] AnythingLLM response copied to clipboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PromptDiagnostics] Error copying response to clipboard");
            }
        }

        /// <summary>
        /// Compares the two responses and highlights differences
        /// </summary>
        [RelayCommand]
        private void CompareResponses()
        {
            if (!HasAnythingLLMResponse || string.IsNullOrWhiteSpace(ExternalLLMResponse) ||
                ExternalLLMResponse.StartsWith("Paste external"))
            {
                ComparisonResults = "⚠️ Need both responses to compare.";
                return;
            }

            HasExternalResponse = true;
            ExternalResponseLength = ExternalLLMResponse.Length;

            var comparison = new System.Text.StringBuilder();
            comparison.AppendLine("═══════════════════════════════════════");
            comparison.AppendLine("RESPONSE COMPARISON");
            comparison.AppendLine("═══════════════════════════════════════");
            comparison.AppendLine();

            // Basic metrics
            comparison.AppendLine("--- SIZE COMPARISON ---");
            comparison.AppendLine($"AnythingLLM: {AnythingLLMResponseLength:N0} characters (~{AnythingLLMResponseLength / 4:N0} tokens)");
            comparison.AppendLine($"External LLM: {ExternalResponseLength:N0} characters (~{ExternalResponseLength / 4:N0} tokens)");
            comparison.AppendLine($"Difference: {Math.Abs(AnythingLLMResponseLength - ExternalResponseLength):N0} characters");
            comparison.AppendLine();

            // Try to extract test case count from both
            var anythingLLMTestCases = CountTestCasesInResponse(AnythingLLMResponse);
            var externalTestCases = CountTestCasesInResponse(ExternalLLMResponse);

            comparison.AppendLine("--- TEST CASE COUNT ---");
            comparison.AppendLine($"AnythingLLM: {anythingLLMTestCases} test cases detected");
            comparison.AppendLine($"External LLM: {externalTestCases} test cases detected");
            
            if (anythingLLMTestCases > externalTestCases)
            {
                comparison.AppendLine($"✓ AnythingLLM generated {anythingLLMTestCases - externalTestCases} more test cases");
            }
            else if (externalTestCases > anythingLLMTestCases)
            {
                comparison.AppendLine($"⚠️ External LLM generated {externalTestCases - anythingLLMTestCases} more test cases");
            }
            else
            {
                comparison.AppendLine("✓ Both generated same number of test cases");
            }
            comparison.AppendLine();

            // JSON structure comparison
            comparison.AppendLine("--- STRUCTURE ANALYSIS ---");
            var anythingLLMHasJSON = AnythingLLMResponse.Contains("{") && AnythingLLMResponse.Contains("}");
            var externalHasJSON = ExternalLLMResponse.Contains("{") && ExternalLLMResponse.Contains("}");

            comparison.AppendLine($"AnythingLLM JSON structure: {(anythingLLMHasJSON ? "✓ Present" : "✕ Missing")}");
            comparison.AppendLine($"External LLM JSON structure: {(externalHasJSON ? "✓ Present" : "✕ Missing")}");
            comparison.AppendLine();

            // Similarity check
            comparison.AppendLine("--- CONTENT SIMILARITY ---");
            var similarity = CalculateSimilarity(AnythingLLMResponse, ExternalLLMResponse);
            comparison.AppendLine($"Similarity: {similarity:F1}%");
            
            if (similarity > 80)
            {
                comparison.AppendLine("✓ Responses are very similar");
            }
            else if (similarity > 50)
            {
                comparison.AppendLine("⚠️ Responses have moderate similarity");
            }
            else
            {
                comparison.AppendLine("⚠️ Responses are quite different");
            }

            ComparisonResults = comparison.ToString();

            _logger.LogInformation("[PromptDiagnostics] Responses compared: Similarity={Similarity:F1}%",
                similarity);
        }

        /// <summary>
        /// Clears all diagnostic data
        /// </summary>
        [RelayCommand]
        private void ClearAll()
        {
            GeneratedPrompt = "No prompt generated yet. Generate test cases to see the prompt.";
            AnythingLLMResponse = "No response yet.";
            ExternalLLMResponse = "Paste external LLM response here for comparison...";
            ComparisonResults = string.Empty;
            PromptMetadata = string.Empty;
            HasPrompt = false;
            HasAnythingLLMResponse = false;
            HasExternalResponse = false;
            PromptLength = 0;
            AnythingLLMResponseLength = 0;
            ExternalResponseLength = 0;

            _logger.LogInformation("[PromptDiagnostics] All diagnostics cleared");
        }

        // Helper methods

        private int CountTestCasesInResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return 0;

            // Try multiple heuristics to count test cases
            int count = 0;

            // Look for "id": or "ID": patterns (common in JSON)
            var idMatches = System.Text.RegularExpressions.Regex.Matches(response, @"""[iI][dD]""\s*:\s*""[^""]+""");
            count = Math.Max(count, idMatches.Count);

            // Look for test case markers
            var tcMatches = System.Text.RegularExpressions.Regex.Matches(response, @"TC[-_]?\d+", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            count = Math.Max(count, tcMatches.Count);

            return count;
        }

        private double CalculateSimilarity(string text1, string text2)
        {
            if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
                return 0;

            // Simple word-based similarity
            var words1 = text1.ToLowerInvariant()
                .Split(new[] { ' ', '\n', '\r', '\t', ',', '.', ':', ';' }, 
                    StringSplitOptions.RemoveEmptyEntries);
            var words2 = text2.ToLowerInvariant()
                .Split(new[] { ' ', '\n', '\r', '\t', ',', '.', ':', ';' }, 
                    StringSplitOptions.RemoveEmptyEntries);

            var set1 = new HashSet<string>(words1);
            var set2 = new HashSet<string>(words2);

            var intersection = new HashSet<string>(set1);
            intersection.IntersectWith(set2);

            var union = new HashSet<string>(set1);
            union.UnionWith(set2);

            if (union.Count == 0)
                return 0;

            return (double)intersection.Count / union.Count * 100;
        }
    }
}
