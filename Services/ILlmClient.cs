// Minimal interface for the LLM client used by TestCaseGenerator_QuestionsVM.
// Implement this with your existing Ollama/OpenAI/LmStudio calling code.
namespace TestCaseEditorApp.MVVM.Services
{
    public interface ILlmClient
    {
        // Given a prompt/context and budget, return an array of clarifying question strings.
        // Implement with your actual call (Ollama/OpenAI etc).
        Task<string[]?> QueryForClarifyingQuestionsAsync(string context, int questionBudget, CancellationToken ct = default);
    }
}