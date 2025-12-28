using System;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Standalone service for AnythingLLM operations specific to test case generation.
    /// </summary>
    public class TestCaseAnythingLLMService
    {
        private readonly AnythingLLMService _anythingLLMService;
        private readonly NotificationService _notificationService;

        public TestCaseAnythingLLMService(
            AnythingLLMService anythingLLMService,
            NotificationService notificationService)
        {
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        }

        /// <summary>
        /// Connect to AnythingLLM with auto-launch and notifications
        /// </summary>
        public async Task ConnectAsync()
        {
            try
            {
                _notificationService.ShowInfo("Connecting to AnythingLLM...");
                await _anythingLLMService.EnsureServiceRunningAsync();
                _notificationService.ShowSuccess("✅ Connected to AnythingLLM", 5);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[TestCaseAnythingLLMService] Failed to connect");
                _notificationService.ShowError("❌ Failed to connect to AnythingLLM", 5);
            }
        }
    }
}