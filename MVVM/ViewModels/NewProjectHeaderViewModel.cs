using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Header ViewModel that displays project creation progress and status.
    /// Uses mediator pattern to stay in sync with workflow state.
    /// </summary>
    public partial class NewProjectHeaderViewModel : ObservableObject
    {
        [ObservableProperty] private string title = "Create New Project";
        [ObservableProperty] private string subtitle = "Setting up your new project workspace";
        
        // Progress tracking properties
        [ObservableProperty] private bool canProceed = false;
        [ObservableProperty] private bool hasWorkspaceName = false;
        [ObservableProperty] private bool isWorkspaceCreated = false;
        [ObservableProperty] private bool hasSelectedDocument = false;
        [ObservableProperty] private bool hasProjectName = false;
        [ObservableProperty] private bool hasProjectSavePath = false;
        
        // AnythingLLM status properties
        [ObservableProperty] private bool isAnythingLLMAvailable;
        [ObservableProperty] private bool isAnythingLLMStarting;
        [ObservableProperty] private string anythingLLMStatusMessage = "Initializing AnythingLLM...";
        
        public NewProjectHeaderViewModel()
        {
            TestCaseEditorApp.Services.Logging.Log.Info("[NewProjectHeaderVM] Constructor called");
            
            // Subscribe to workflow state changes via mediator
            ProjectWorkflowMediator.WorkflowStateChanged += OnWorkflowStateChanged;
            
            // Subscribe to AnythingLLM status updates via mediator
            AnythingLLMMediator.StatusUpdated += OnAnythingLLMStatusUpdated;
            
            // Request current AnythingLLM status since we might have missed initial updates
            TestCaseEditorApp.Services.Logging.Log.Info("[NewProjectHeaderVM] Requesting current AnythingLLM status");
            AnythingLLMMediator.RequestCurrentStatus();
        }
        
        private void OnWorkflowStateChanged(ProjectWorkflowState state)
        {
            CanProceed = state.CanProceed;
            HasWorkspaceName = state.HasWorkspaceName;
            IsWorkspaceCreated = state.IsWorkspaceCreated;
            HasSelectedDocument = state.HasSelectedDocument;
            HasProjectName = state.HasProjectName;
            HasProjectSavePath = state.HasProjectSavePath;
        }
        
        /// <summary>
        /// Handles AnythingLLM status updates from the mediator
        /// </summary>
        private void OnAnythingLLMStatusUpdated(AnythingLLMStatus status)
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[NewProjectHeaderVM] Received status update: Available={status.IsAvailable}, Starting={status.IsStarting}, Message={status.StatusMessage}");
            
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                IsAnythingLLMAvailable = status.IsAvailable;
                IsAnythingLLMStarting = status.IsStarting;
                AnythingLLMStatusMessage = status.StatusMessage;
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[NewProjectHeaderVM] Properties updated: Available={IsAnythingLLMAvailable}, Starting={IsAnythingLLMStarting}, Message={AnythingLLMStatusMessage}");
            });
        }
    }
}