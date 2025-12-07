using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.Services
{
    public static class ClarifyingQuestionService
    {
        public static async Task<(string llmText, List<ClarifyingQuestionVM> questions, List<string> suggestedKeys)> GenerateClarifyingQuestionsAsync(
            Requirement requirement,
            int questionBudget,
            IEnumerable<string> enabledAssumptions,
            ITextGenerationService llm,
            MainViewModel? mainVm,
            IEnumerable<DefaultItem>? suggestedDefaultsCatalog,
            CancellationToken ct,
            string? customInstructions = null)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            if (llm == null) throw new ArgumentNullException(nameof(llm));

            var prompt = ClarifyingParsingHelpers.BuildBudgetedQuestionsPrompt(
                requirement,
                questionBudget,
                enabledAssumptions ?? Enumerable.Empty<string>(),
                Enumerable.Empty<TableDto>(),
                customInstructions);

            var llmText = await llm.GenerateAsync(prompt, ct).ConfigureAwait(false);
            var safe = llmText ?? string.Empty;

            var suggested = ClarifyingParsingHelpers.TryExtractSuggestedChipKeys(safe, suggestedDefaultsCatalog);
            // Note: callers will merge suggestedKeys into their SuggestedDefaults catalog as appropriate.

            var parsed = ClarifyingParsingHelpers.ParseQuestions(safe, mainVm) ?? new List<ClarifyingQuestionVM>();

            return (llmText ?? string.Empty, parsed.ToList(), suggested ?? new List<string>());
        }

        public static async Task<(string llmText, List<ClarifyingQuestionVM> questions)> RequestReplacementQuestionAsync(
            Requirement requirement,
            IEnumerable<string> enabledAssumptions,
            ITextGenerationService llm,
            MainViewModel? mainVm,
            CancellationToken ct,
            string? customInstructions = null)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            if (llm == null) throw new ArgumentNullException(nameof(llm));

            var basePrompt = ClarifyingParsingHelpers.BuildBudgetedQuestionsPrompt(
                requirement,
                1,
                enabledAssumptions ?? Enumerable.Empty<string>(),
                Enumerable.Empty<TableDto>(),
                customInstructions);

            var prompt = basePrompt + "\n\nIMPORTANT: Return EXACTLY ONE question in a JSON array with a single object. Do not return multiple questions. Format: [{\"text\":\"...\",\"category\":\"...\",\"severity\":\"...\",\"rationale\":\"...\"}]";

            var llmText = await llm.GenerateAsync(prompt, ct).ConfigureAwait(false);
            var safe = llmText ?? string.Empty;

            var parsed = ClarifyingParsingHelpers.ParseQuestions(safe, mainVm) ?? new List<ClarifyingQuestionVM>();

            return (llmText ?? string.Empty, parsed.ToList());
        }
    }
}
