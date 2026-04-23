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

            _workspaceSlug = workspaceSlug
                ?? Environment.GetEnvironmentVariable("ANYTHINGLLM_WORKSPACE")
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_workspaceSlug))
            {
                TestCaseEditorApp.Services.Logging.Log.Error(
                    "[AnythingLLMTextGen] No workspace slug provided and ANYTHINGLLM_WORKSPACE is not set.");
            }
            else
            {
                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[AnythingLLMTextGen] Initialized with workspace: {_workspaceSlug}");
            }
        }

        /// <summary>
        /// make this the active code once troubleshooting analysis is complete
        /// </summary>
        /// <param name="prompt"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// 
        //public AnythingLLMTextGenerationService(IAnythingLLMService anythingLlmService, string? workspaceSlug = null)
        //{
        //    _anythingLlmService = anythingLlmService ?? throw new ArgumentNullException(nameof(anythingLlmService));

        //    _workspaceSlug = workspaceSlug
        //        ?? Environment.GetEnvironmentVariable("ANYTHINGLLM_WORKSPACE");

        //    if (string.IsNullOrWhiteSpace(_workspaceSlug))
        //    {
        //        throw new InvalidOperationException(
        //            "AnythingLLM workspace slug was not provided and ANYTHINGLLM_WORKSPACE is not set.");
        //    }

        //    TestCaseEditorApp.Services.Logging.Log.Info(
        //        $"[AnythingLLMTextGen] Initialized with workspace: {_workspaceSlug}");
        //}

        public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLMTextGen] Sending prompt to AnythingLLM workspace '{_workspaceSlug}' (prompt length: {prompt.Length})");
                
                // Use 3-minute timeout for document parsing (can be long)
                var response = await _anythingLlmService.SendChatMessageAsync(
                    _workspaceSlug, 
                    prompt, 
                    TimeSpan.FromMinutes(3), 
                    ct);
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLMTextGen] Received response from AnythingLLM (length: {response?.Length ?? 0})");
                
                return response ?? string.Empty;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[AnythingLLMTextGen] Failed to generate via AnythingLLM: {ex.Message}");
                throw;
            }
        }

        public async Task<string> GenerateWithSystemAsync(string systemMessage, string contextMessage, CancellationToken ct = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[AnythingLLMTextGen] Sending context-only prompt to AnythingLLM workspace '{_workspaceSlug}'. " +
                    $"System length={systemMessage?.Length ?? 0}, Context length={contextMessage?.Length ?? 0}");

                var response = await _anythingLlmService.SendChatMessageAsync(
                    _workspaceSlug,
                    contextMessage,
                    TimeSpan.FromMinutes(3),
                    ct);

                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[AnythingLLMTextGen] Received response from AnythingLLM (length: {response?.Length ?? 0})");

                return response ?? string.Empty;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(
                    $"[AnythingLLMTextGen] Failed to generate with system via AnythingLLM: {ex.Message}");
                throw;
            }
        }
    }
}
