using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators;
using TestCaseEditorApp.MVVM.Events;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// ViewModel for the TestCaseGenerator title area with save indicator and app title
    /// </summary>
    public partial class TestCaseGenerator_TitleVM : ObservableObject
    {
        private readonly ITestCaseGenerationMediator _mediator;
        
        // ==================== Observable Properties ====================
        
        [ObservableProperty] 
        private string? workspaceFilePath;

        [ObservableProperty] 
        private bool isDirty;

        [ObservableProperty] 
        private bool canUndoLastSave;

        [ObservableProperty] 
        private string saveStatusText = "No changes";

        [ObservableProperty] 
        private DateTime? lastSavedTimestamp;

        [ObservableProperty]
        private string title = "Test Case Generator";

        // ==================== Computed Properties ====================
        
        /// <summary>
        /// Formatted timestamp for display in window controls
        /// </summary>
        public string LastSaveTimestamp => LastSavedTimestamp.HasValue 
            ? LastSavedTimestamp.Value.ToString("HH:mm:ss")
            : "No saves";

        // ==================== Commands ====================
        
        public IAsyncRelayCommand? SaveWorkspaceCommand { get; set; }
        public IAsyncRelayCommand? UndoLastSaveCommand { get; set; }

        // ==================== Constructor ====================
        
        public TestCaseGenerator_TitleVM(ITestCaseGenerationMediator mediator)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            
            // Subscribe to workflow state changes to update save status
            _mediator.Subscribe<TestCaseGenerationEvents.WorkflowStateChanged>(OnWorkflowStateChanged);
            
            // Subscribe to project title changes
            _mediator.Subscribe<TestCaseGenerationEvents.ProjectTitleChanged>(OnProjectTitleChanged);
            
            // Initialize save state
            SaveStatusText = "No changes";
            LastSavedTimestamp = null;
            
            // Make save indicator visible immediately
            WorkspaceFilePath = "project";
            System.Diagnostics.Debug.WriteLine($"[TitleVM] Constructor: WorkspaceFilePath={WorkspaceFilePath}");
        }

        // ==================== Public Methods ====================
        
        /// <summary>
        /// Update save status from workspace management mediator
        /// </summary>
        public void UpdateSaveStatus(IWorkspaceManagementMediator mediator)
        {
            ArgumentNullException.ThrowIfNull(mediator);
            
            var wasDirty = IsDirty;
            IsDirty = mediator.HasUnsavedChanges();
            CanUndoLastSave = mediator.CanUndoLastSave();

            // Set workspace file path so save button is visible
            WorkspaceFilePath = "project"; // Non-null value to show save button
            
            System.Diagnostics.Debug.WriteLine($"[TitleVM] UpdateSaveStatus: IsDirty={IsDirty}, WorkspaceFilePath={WorkspaceFilePath}, HasUnsavedChanges={mediator.HasUnsavedChanges()}");

            // Update timestamp and status text when transitioning from dirty to clean (save completed)
            if (wasDirty && !IsDirty)
            {
                LastSavedTimestamp = DateTime.Now;
                OnPropertyChanged(nameof(LastSaveTimestamp)); // Notify computed property
                SaveStatusText = $"Saved {LastSavedTimestamp:HH:mm:ss}";
            }
            else if (IsDirty)
            {
                SaveStatusText = "Unsaved changes";
            }
            else
            {
                SaveStatusText = LastSavedTimestamp.HasValue 
                    ? $"Saved {LastSavedTimestamp:HH:mm:ss}"
                    : "No changes";
            }
        }
        
        /// <summary>
        /// Handle workflow state changes from the domain mediator
        /// </summary>
        private void OnWorkflowStateChanged(TestCaseGenerationEvents.WorkflowStateChanged e)
        {
            // Update IsDirty state when the domain's dirty state changes
            if (e.PropertyName == nameof(ITestCaseGenerationMediator.IsDirty) && e.NewValue is bool isDirty)
            {
                var wasDirty = IsDirty;
                IsDirty = isDirty;
                
                // Update status text based on new dirty state
                if (IsDirty)
                {
                    SaveStatusText = "Unsaved changes";
                }
                else if (wasDirty && !IsDirty) 
                {
                    // Transitioning from dirty to clean (save completed)
                    LastSavedTimestamp = DateTime.Now;
                    OnPropertyChanged(nameof(LastSaveTimestamp));
                    SaveStatusText = $"Saved {LastSavedTimestamp:HH:mm:ss}";
                }
                
                System.Diagnostics.Debug.WriteLine($"[TitleVM] WorkflowStateChanged: IsDirty={IsDirty}, SaveStatusText={SaveStatusText}");
            }
        }
        
        /// <summary>
        /// Handle project title changes from the domain mediator
        /// </summary>
        private void OnProjectTitleChanged(TestCaseGenerationEvents.ProjectTitleChanged e)
        {
            if (!string.IsNullOrEmpty(e.ProjectName))
            {
                Title = $"Test Case Generator - {e.ProjectName}";
            }
            else
            {
                Title = "Test Case Generator";
            }
            
            System.Diagnostics.Debug.WriteLine($"[TitleVM] Project title changed to: {Title}");
        }
    }
}