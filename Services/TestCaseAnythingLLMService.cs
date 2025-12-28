using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Standalone service for AnythingLLM operations specific to test case generation.
    /// Reports status changes via AnythingLLMMediator for decoupled view updates.
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
                // Notify via mediator that we're starting
                AnythingLLMMediator.NotifyStatusUpdated(new AnythingLLMStatus 
                { 
                    IsAvailable = false, 
                    IsStarting = true, 
                    StatusMessage = "Connecting to AnythingLLM..." 
                });
                
                _notificationService.ShowInfo("Connecting to AnythingLLM...");
                
                await _anythingLLMService.EnsureServiceRunningAsync();
                
                // Notify via mediator that we're connected
                AnythingLLMMediator.NotifyStatusUpdated(new AnythingLLMStatus 
                { 
                    IsAvailable = true, 
                    IsStarting = false, 
                    StatusMessage = "✅ Connected to AnythingLLM" 
                });
                
                _notificationService.ShowSuccess("✅ Connected to AnythingLLM", 5);
            }
            catch (Exception ex)
            {
                // Notify via mediator that there was an error
                AnythingLLMMediator.NotifyStatusUpdated(new AnythingLLMStatus 
                { 
                    IsAvailable = false, 
                    IsStarting = false, 
                    StatusMessage = "❌ Failed to connect to AnythingLLM" 
                });
                
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[TestCaseAnythingLLMService] Failed to connect");
                _notificationService.ShowError("❌ Failed to connect to AnythingLLM", 5);
            }
        }
    }
}