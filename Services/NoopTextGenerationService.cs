using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Simple no-op ITextGenerationService used for development/testing.
    /// Returns a short placeholder string synchronously.
    /// </summary>
    public sealed class NoopTextGenerationService : ITextGenerationService
    {
        private readonly string _response;

        public NoopTextGenerationService(string response = "[LLM not configured] NoopTextGenerationService response.")
        {
            _response = response;
        }

        public Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
        {
            // Return a reproducible placeholder so parsing/flow can be exercised without an LLM.
            return Task.FromResult(_response + " PromptEcho: " + (prompt ?? string.Empty));
        }

        public Task<string> GenerateWithSystemAsync(string systemMessage, string contextMessage, CancellationToken ct = default)
        {
            // Return a reproducible placeholder that shows both system and context were received
            var systemLength = systemMessage?.Length ?? 0;
            var systemSnippet = systemMessage?.Substring(0, Math.Min(50, systemLength)) ?? "";
            return Task.FromResult(_response + " SystemEcho: " + systemSnippet + " ContextEcho: " + (contextMessage ?? string.Empty));
        }
    }
}