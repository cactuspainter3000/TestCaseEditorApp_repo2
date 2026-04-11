// Services/AnythingLLMTextGenerationService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Adapter that implements ITextGenerationService by delegating to AnythingLLM service.
    /// This allows using AnythingLLM for document parsing instead of direct Ollama.
    /// </summary>
    public sealed class AnythingLLMTextGenerationService : ITextGenerationService
    {
        private readonly IAnythingLLMService _anythingLlmService;
        private readonly string _workspaceSlug;

        public AnythingLLMTextGenerationService(IAnythingLLMService anythingLlmService, string? workspaceSlug = null)
        {
            _anythingLlmService = anythingLlmService ?? throw new ArgumentNullException(nameof(anythingLlmService));
            
            // Default to a general-purpose workspace if not specified
            // User can override via ANYTHINGLLM_WORKSPACE environment variable
            _workspaceSlug = workspaceSlug 
                ?? Environment.GetEnvironmentVariable("ANYTHINGLLM_WORKSPACE") 
                ?? "test-case-editor-learning"; // fallback to known workspace
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLMTextGen] Initialized with workspace: {_workspaceSlug}");
        }

        public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[AnythingLLMTextGen] Sending prompt to AnythingLLM workspace '{_workspaceSlug}' (prompt length: {prompt.Length})");

                var response = await _anythingLlmService.SendChatMessageAsync(
                    _workspaceSlug,
                    prompt,
                    TimeSpan.FromMinutes(3),
                    ct);

                if (string.IsNullOrWhiteSpace(response))
                {
                    throw new InvalidOperationException(
                        $"AnythingLLM returned an empty response for workspace '{_workspaceSlug}'.");
                }

                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[AnythingLLMTextGen] Received response from AnythingLLM (length: {response.Length})");

                return response;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(
                    $"[AnythingLLMTextGen] Failed to generate via AnythingLLM: {ex.Message}");
                throw;
            }
        }

        public async Task<string> GenerateWithSystemAsync(string systemMessage, string contextMessage, CancellationToken ct = default)
        {
            try
            {
                // AnythingLLM workspaces have system prompts configured in the workspace settings
                // We'll prepend the system message to the context for this call
                var combinedPrompt = $"System Context: {systemMessage}\n\nUser Request: {contextMessage}";
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLMTextGen] Sending prompt with system message to AnythingLLM workspace '{_workspaceSlug}'");
                
                var response = await _anythingLlmService.SendChatMessageAsync(
                    _workspaceSlug, 
                    combinedPrompt, 
                    TimeSpan.FromMinutes(3), 
                    ct);
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLMTextGen] Received response from AnythingLLM (length: {response?.Length ?? 0})");
                
                return response ?? string.Empty;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[AnythingLLMTextGen] Failed to generate with system via AnythingLLM: {ex.Message}");
                throw;
            }
        }
    }
}
