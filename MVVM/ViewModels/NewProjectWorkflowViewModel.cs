using System;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class NewProjectWorkflowViewModel : ObservableObject
    {
        private readonly AnythingLLMService _anythingLLMService;
        
        [ObservableProperty]
        private string workspaceName = "";
        
        [ObservableProperty]
        private string workspaceDescription = "";
        
        [ObservableProperty]
        private string selectedDocumentPath = "";
        
        [ObservableProperty]
        private bool autoAnalyzeEnabled = true;
        
        [ObservableProperty]
        private bool autoExportEnabled = true;
        
        [ObservableProperty]
        private string projectSavePath = "";
        
        [ObservableProperty]
        private string projectName = "";
        
        [ObservableProperty]
        private bool canProceed = false;

        // Workspace validation
        [ObservableProperty]
        private bool isValidatingWorkspace = false;
        
        [ObservableProperty]
        private string workspaceValidationMessage = "";
        
        [ObservableProperty]
        private bool workspaceValidationSuccess = false;
        
        [ObservableProperty]
        private bool hasValidationMessage = false;
        
        [ObservableProperty]
        private bool isDuplicateName = false;
        
        [ObservableProperty]
        private bool isWorkspaceCreated = false;

        // Step tracking
        [ObservableProperty]
        private bool hasWorkspaceName = false;
        
        [ObservableProperty]
        private bool hasSelectedDocument = false;
        
        [ObservableProperty]
        private bool hasProjectSavePath = false;
        
        [ObservableProperty]
        private bool hasProjectName = false;

        // Commands
        public ICommand SelectDocumentCommand { get; }
        public ICommand ChooseProjectSaveLocationCommand { get; }
        public ICommand CreateProjectCommand { get; }
        public ICommand ValidateWorkspaceCommand { get; }
        public ICommand CancelCommand { get; }

        // Events
        public event EventHandler<NewProjectCompletedEventArgs>? ProjectCreated;
        public event EventHandler? ProjectCancelled;

        public NewProjectWorkflowViewModel(AnythingLLMService anythingLLMService)
        {
            _anythingLLMService = anythingLLMService;
            SelectDocumentCommand = new RelayCommand(SelectDocument);
            ChooseProjectSaveLocationCommand = new RelayCommand(ChooseProjectSaveLocation);
            CreateProjectCommand = new RelayCommand(CreateProject);
            ValidateWorkspaceCommand = new AsyncRelayCommand(ValidateWorkspaceAsync, () => !string.IsNullOrWhiteSpace(WorkspaceName) && !IsWorkspaceCreated);
            CancelCommand = new RelayCommand(Cancel);
        }

        partial void OnWorkspaceNameChanged(string value)
        {
            HasWorkspaceName = !string.IsNullOrWhiteSpace(value);
            UpdateCanProceed();
            
            // Reset workspace creation status when name changes
            IsWorkspaceCreated = false;
            
            // Notify the command that CanExecute may have changed
            ((AsyncRelayCommand)ValidateWorkspaceCommand).NotifyCanExecuteChanged();
        }

        partial void OnIsWorkspaceCreatedChanged(bool value)
        {
            // Update CanProceed when workspace creation status changes
            UpdateCanProceed();
            
            // Notify the command that CanExecute may have changed
            ((AsyncRelayCommand)ValidateWorkspaceCommand).NotifyCanExecuteChanged();
        }

        partial void OnSelectedDocumentPathChanged(string value)
        {
            HasSelectedDocument = !string.IsNullOrWhiteSpace(value) && File.Exists(value);
            UpdateCanProceed();
        }

        partial void OnProjectSavePathChanged(string value)
        {
            HasProjectSavePath = !string.IsNullOrWhiteSpace(value);
            UpdateCanProceed();
        }

        partial void OnProjectNameChanged(string value)
        {
            HasProjectName = !string.IsNullOrWhiteSpace(value);
            UpdateCanProceed();
        }

        private void UpdateCanProceed()
        {
            CanProceed = HasWorkspaceName && HasSelectedDocument && HasProjectSavePath && HasProjectName && IsWorkspaceCreated;
        }

        private void SelectDocument()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Requirements Document",
                Filter = "Word Documents (*.docx)|*.docx|All Files (*.*)|*.*",
                RestoreDirectory = true
            };

            if (dlg.ShowDialog() == true)
            {
                SelectedDocumentPath = dlg.FileName;
                
                // Auto-suggest project name from document name
                if (string.IsNullOrWhiteSpace(ProjectName))
                {
                    ProjectName = Path.GetFileNameWithoutExtension(dlg.FileName);
                }
            }
        }

        private void ChooseProjectSaveLocation()
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save Project As",
                Filter = "Test Case Editor Project (*.tcex.json)|*.tcex.json|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".tcex.json",
                FileName = string.IsNullOrWhiteSpace(ProjectName) ? "New Project.tcex.json" : $"{ProjectName}.tcex.json"
            };

            if (dlg.ShowDialog() == true)
            {
                ProjectSavePath = dlg.FileName;
            }
        }

        private void CreateProject()
        {
            if (!CanProceed)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn("[PROJECT] CreateProject called but CanProceed is false");
                return;
            }
            
            if (!IsWorkspaceCreated)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn("[PROJECT] CreateProject called but workspace not yet created. Please validate workspace first.");
                WorkspaceValidationMessage = "Please create the workspace first by clicking 'Create Workspace'";
                HasValidationMessage = true;
                return;
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Creating project with workspace '{WorkspaceName}', document '{SelectedDocumentPath}', save path '{ProjectSavePath}'");

            var args = new NewProjectCompletedEventArgs
            {
                WorkspaceName = WorkspaceName,
                WorkspaceDescription = WorkspaceDescription,
                DocumentPath = SelectedDocumentPath,
                AutoAnalyzeEnabled = AutoAnalyzeEnabled,
                AutoExportEnabled = AutoExportEnabled,
                ProjectSavePath = ProjectSavePath,
                ProjectName = ProjectName
            };

            ProjectCreated?.Invoke(this, args);
        }

        private void Cancel()
        {
            ProjectCancelled?.Invoke(this, EventArgs.Empty);
        }

        private async System.Threading.Tasks.Task ValidateWorkspaceAsync()
        {
            if (string.IsNullOrWhiteSpace(WorkspaceName))
            {
                WorkspaceValidationMessage = "Please enter a workspace name.";
                WorkspaceValidationSuccess = false;
                return;
            }

            IsValidatingWorkspace = true;
            WorkspaceValidationMessage = "";
            WorkspaceValidationSuccess = false;
            HasValidationMessage = false;
            IsDuplicateName = false;

            try
            {
                // Basic validation first
                var invalidNames = new[] { "test", "demo", "example", "workspace", "default" };
                var workspaceLower = WorkspaceName.ToLowerInvariant().Trim();
                
                if (invalidNames.Contains(workspaceLower))
                {
                    WorkspaceValidationMessage = $"The name '{WorkspaceName}' is reserved. Please choose a different name.";
                    WorkspaceValidationSuccess = false;
                    HasValidationMessage = true;
                    return;
                }
                
                if (WorkspaceName.Length < 3)
                {
                    WorkspaceValidationMessage = "Workspace name must be at least 3 characters long.";
                    WorkspaceValidationSuccess = false;
                    HasValidationMessage = true;
                    return;
                }
                
                if (WorkspaceName.Length > 50)
                {
                    WorkspaceValidationMessage = "Workspace name must be less than 50 characters.";
                    WorkspaceValidationSuccess = false;
                    HasValidationMessage = true;
                    return;
                }
                
                // Check for duplicate in AnythingLLM
                bool nameExists = false;
                try
                {
                    nameExists = await _anythingLLMService.WorkspaceNameExistsAsync(WorkspaceName);
                }
                catch
                {
                    // If service check fails, continue with local validation only
                }
                
                if (nameExists)
                {
                    WorkspaceValidationMessage = $"A workspace named '{WorkspaceName}' already exists. Please choose a different name.";
                    WorkspaceValidationSuccess = false;
                    HasValidationMessage = true;
                    IsDuplicateName = true;
                }
                else
                {
                    // Create the workspace immediately
                    WorkspaceValidationMessage = $"Creating workspace '{WorkspaceName}'...";
                    WorkspaceValidationSuccess = false; // Set to false during creation
                    HasValidationMessage = true;
                    
                    try
                    {
                        var createdWorkspace = await _anythingLLMService.CreateWorkspaceAsync(WorkspaceName);
                        if (createdWorkspace != null)
                        {
                            WorkspaceValidationMessage = $"✅ Workspace '{WorkspaceName}' created successfully! You can now proceed with the remaining steps.";
                            WorkspaceValidationSuccess = true;
                            HasValidationMessage = true;
                            HasWorkspaceName = true;
                            IsWorkspaceCreated = true;
                        }
                        else
                        {
                            WorkspaceValidationMessage = $"Failed to create workspace '{WorkspaceName}'. Please try again.";
                            WorkspaceValidationSuccess = false;
                            HasValidationMessage = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        WorkspaceValidationMessage = $"Error creating workspace: {ex.Message}";
                        WorkspaceValidationSuccess = false;
                        HasValidationMessage = true;
                    }
                }
            }
            catch (Exception ex)
            {
                WorkspaceValidationMessage = $"Validation failed: {ex.Message}";
                WorkspaceValidationSuccess = false;
                HasValidationMessage = true;
            }
            finally
            {
                IsValidatingWorkspace = false;
            }
        }

        public void Initialize()
        {
            // Reset all fields
            WorkspaceName = "";
            WorkspaceDescription = "";
            SelectedDocumentPath = "";
            ProjectSavePath = "";
            ProjectName = "";
            AutoAnalyzeEnabled = true;
            AutoExportEnabled = true;
        }
    }

    public class NewProjectCompletedEventArgs : EventArgs
    {
        public string WorkspaceName { get; set; } = "";
        public string WorkspaceDescription { get; set; } = "";
        public string DocumentPath { get; set; } = "";
        public bool AutoAnalyzeEnabled { get; set; }
        public bool AutoExportEnabled { get; set; }
        public string ProjectSavePath { get; set; } = "";
        public string ProjectName { get; set; } = "";
    }
}