using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators;
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

        // ==================== Commands ====================
        
        public IAsyncRelayCommand? SaveWorkspaceCommand { get; set; }
        public IAsyncRelayCommand? UndoLastSaveCommand { get; set; }

        // ==================== Constructor ====================
        
        public TestCaseGenerator_TitleVM(ITestCaseGenerationMediator mediator)
        {
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
    }
}