using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class ProjectViewModel : ObservableObject
    {
        public ProjectViewModel()
        {
            Title = "Project Management";
            Description = "Configure your test case generation projects and workspace settings.";
            
            TestCaseEditorApp.Services.Logging.Log.Info("[ProjectViewModel] Constructor called");
            
            // Subscribe to AnythingLLM status updates via mediator
            AnythingLLMMediator.StatusUpdated += OnAnythingLLMStatusUpdated;
            
            // Request current AnythingLLM status since we might have missed initial updates
            TestCaseEditorApp.Services.Logging.Log.Info("[ProjectViewModel] Requesting current AnythingLLM status");
            AnythingLLMMediator.RequestCurrentStatus();
        }
        
        [ObservableProperty]
        private string title = string.Empty;
        
        [ObservableProperty]
        private string description = string.Empty;
        
        // AnythingLLM status properties
        [ObservableProperty] private bool isAnythingLLMAvailable;
        [ObservableProperty] private bool isAnythingLLMStarting;
        [ObservableProperty] private string anythingLLMStatusMessage = "Initializing AnythingLLM...";
        
        /// <summary>
        /// Handles AnythingLLM status updates from the mediator
        /// </summary>
        private void OnAnythingLLMStatusUpdated(AnythingLLMStatus status)
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[ProjectViewModel] Received status update: Available={status.IsAvailable}, Starting={status.IsStarting}, Message={status.StatusMessage}");
            
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                IsAnythingLLMAvailable = status.IsAvailable;
                IsAnythingLLMStarting = status.IsStarting;
                AnythingLLMStatusMessage = status.StatusMessage;
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[ProjectViewModel] Properties updated: Available={IsAnythingLLMAvailable}, Starting={IsAnythingLLMStarting}, Message={AnythingLLMStatusMessage}");
            });
        }
    }
}