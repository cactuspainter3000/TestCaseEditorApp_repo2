using System;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for the comprehensive import requirements workflow.
    /// Integrated into main navigation instead of modal approach.
    /// </summary>
    public partial class ImportRequirementsWorkflowViewModel : ObservableObject
    {
        // Events for completion and cancellation
        public event EventHandler<RequirementsImportCompletedEventArgs>? WorkflowCompleted;
        public event EventHandler? WorkflowCancelled;

        // Document selection
        [ObservableProperty] 
        private string? selectedDocumentPath;

        // Import options
        [ObservableProperty] 
        private bool autoAnalyzeEnabled = true;

        [ObservableProperty] 
        private bool autoExportEnabled = false;

        // Workspace configuration
        [ObservableProperty] 
        private string? workspaceName;

        [ObservableProperty] 
        private string? workspaceSavePath;

        // Validation properties
        public bool HasSelectedDocument => !string.IsNullOrWhiteSpace(SelectedDocumentPath) && 
                                          File.Exists(SelectedDocumentPath);

        public bool HasWorkspaceName => !string.IsNullOrWhiteSpace(WorkspaceName);

        public bool HasWorkspaceSavePath => !string.IsNullOrWhiteSpace(WorkspaceSavePath);

        public bool CanProceed => HasSelectedDocument && HasWorkspaceName && HasWorkspaceSavePath;

        // Commands
        [RelayCommand]
        private void SelectDocument()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Requirements Document",
                Filter = "Word Documents (*.docx)|*.docx|All Files (*.*)|*.*",
                DefaultExt = ".docx"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SelectedDocumentPath = openFileDialog.FileName;
                
                // Auto-suggest workspace name based on document name
                if (string.IsNullOrWhiteSpace(WorkspaceName))
                {
                    var documentName = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                    WorkspaceName = $"{documentName} Requirements";
                }
            }
        }

        [RelayCommand]
        private void ChooseWorkspaceLocation()
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Workspace As",
                Filter = "Workspace Files (*.tcex.json)|*.tcex.json|All Files (*.*)|*.*",
                DefaultExt = ".tcex.json",
                FileName = string.IsNullOrWhiteSpace(WorkspaceName) 
                    ? "Requirements Workspace.tcex.json" 
                    : $"{WorkspaceName}.tcex.json"
            };

            // Set default directory to Documents/TestCaseEditorApp/Sessions
            var defaultFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                "TestCaseEditorApp", 
                "Sessions");
            
            try
            {
                Directory.CreateDirectory(defaultFolder);
                saveFileDialog.InitialDirectory = defaultFolder;
            }
            catch
            {
                // Fall back to Documents if we can't create the folder
                saveFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            if (saveFileDialog.ShowDialog() == true)
            {
                WorkspaceSavePath = saveFileDialog.FileName;
                
                // Update workspace name if user changed it in the dialog
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(saveFileDialog.FileName);
                if (!string.IsNullOrWhiteSpace(fileNameWithoutExtension))
                {
                    WorkspaceName = fileNameWithoutExtension;
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanProceed))]
        private void StartImport()
        {
            var args = new RequirementsImportCompletedEventArgs
            {
                DocumentPath = SelectedDocumentPath!,
                AutoAnalyzeEnabled = AutoAnalyzeEnabled,
                AutoExportEnabled = AutoExportEnabled,
                WorkspaceName = WorkspaceName!,
                WorkspaceSavePath = WorkspaceSavePath!
            };

            WorkflowCompleted?.Invoke(this, args);
        }

        [RelayCommand]
        private void Cancel()
        {
            WorkflowCancelled?.Invoke(this, EventArgs.Empty);
        }

        // Property change notifications for validation
        partial void OnSelectedDocumentPathChanged(string? value)
        {
            OnPropertyChanged(nameof(HasSelectedDocument));
            OnPropertyChanged(nameof(CanProceed));
        }

        partial void OnWorkspaceNameChanged(string? value)
        {
            OnPropertyChanged(nameof(HasWorkspaceName));
            OnPropertyChanged(nameof(CanProceed));
        }

        partial void OnWorkspaceSavePathChanged(string? value)
        {
            OnPropertyChanged(nameof(HasWorkspaceSavePath));
            OnPropertyChanged(nameof(CanProceed));
        }

        /// <summary>
        /// Reset the workflow to initial state
        /// </summary>
        public void Reset()
        {
            SelectedDocumentPath = null;
            AutoAnalyzeEnabled = true;
            AutoExportEnabled = false;
            WorkspaceName = null;
            WorkspaceSavePath = null;
        }

        /// <summary>
        /// Initialize with default settings from MainViewModel
        /// </summary>
        public void Initialize(bool defaultAutoAnalyze, bool defaultAutoExport)
        {
            AutoAnalyzeEnabled = defaultAutoAnalyze;
            AutoExportEnabled = defaultAutoExport;
        }
    }

    /// <summary>
    /// Event arguments for completed import workflow
    /// </summary>
    public class RequirementsImportCompletedEventArgs : EventArgs
    {
        public required string DocumentPath { get; set; }
        public required bool AutoAnalyzeEnabled { get; set; }
        public required bool AutoExportEnabled { get; set; }
        public required string WorkspaceName { get; set; }
        public required string WorkspaceSavePath { get; set; }
    }
}