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
        
        public NewProjectHeaderViewModel()
        {
            // Subscribe to workflow state changes via mediator
            ProjectWorkflowMediator.WorkflowStateChanged += OnWorkflowStateChanged;
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
    }
}