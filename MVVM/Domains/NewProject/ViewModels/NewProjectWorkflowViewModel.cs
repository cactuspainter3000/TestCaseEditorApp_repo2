using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.ViewModels;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;

namespace TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels
{
    public partial class NewProjectWorkflowViewModel : BaseDomainViewModel
    {
        // Domain mediator (properly typed)
        private new readonly INewProjectMediator _mediator;
        
        private readonly AnythingLLMService _anythingLLMService;
        private readonly JamaConnectService _jamaConnectService;
        private readonly ToastNotificationService _toastService;
        private string? _validatedAnythingLLMWorkspaceSlug;
        
        // Event fired when project creation is completed
        public event EventHandler<NewProjectCompletedEventArgs>? ProjectCompleted;
        
        [ObservableProperty]
        private string workspaceName = "";
        
        [ObservableProperty]
        private string workspaceDescription = "";
        
        [ObservableProperty]
        private string selectedDocumentPath = "";
        
        [ObservableProperty]
        private bool autoExportEnabled = false;
        
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
        
        [ObservableProperty]
        private bool isProjectCreated = false;

        // Step tracking
        [ObservableProperty]
        private bool hasWorkspaceName = false;
        
        [ObservableProperty]
        private bool hasSelectedDocument = false;
        
        [ObservableProperty]
        private bool hasProjectSavePath = false;
        
        [ObservableProperty]
        private bool hasProjectName = false;
        
        // Computed properties for smart button UX
        public string CreateProjectButtonText
        {
            get
            {
                if (!HasWorkspaceName)
                    return "⚠️ Create AnythingLLM Workspace First";
                if (!IsWorkspaceCreated)
                    return "⚠️ Workspace Not Validated";
                if (!HasSelectedDocument)
                    return "⚠️ Select Jama Project or Requirements Document";
                if (!HasProjectName)
                    return "⚠️ Enter Project Name";
                if (!HasProjectSavePath)
                    return "⚠️ Choose Save Location";
                if (IsProjectCreated)
                    return "✅ Project Created";
                return "🚀 Create Project";
            }
        }
        
        public string CreateProjectButtonTooltip
        {
            get
            {
                if (!HasWorkspaceName)
                    return "First create an AnythingLLM workspace above";
                if (!IsWorkspaceCreated)
                    return "Click 'Create Workspace' to validate your workspace setup";
                if (!HasSelectedDocument)
                    return "Select a Jama project (preferred) or a Word document containing your requirements";
                if (!HasProjectName)
                    return "Enter a name for your new project";
                if (!HasProjectSavePath)
                    return "Choose where to save your project file";
                if (IsProjectCreated)
                    return "Project has been successfully created!";
                return "All prerequisites met - ready to create project!";
            }
        }

        // Commands
        public ICommand SelectDocumentCommand { get; }
        public ICommand ChooseProjectSaveLocationCommand { get; }
        public ICommand CreateProjectCommand { get; }
        public ICommand ValidateWorkspaceCommand { get; }
        public new ICommand CancelCommand { get; }

        // Events
        public event EventHandler<NewProjectCompletedEventArgs>? ProjectCreated;
        public event EventHandler? ProjectCancelled;
        
        public NewProjectWorkflowViewModel(
            INewProjectMediator newProjectMediator,
            ILogger<NewProjectWorkflowViewModel> logger,
            AnythingLLMService anythingLLMService,
            JamaConnectService jamaConnectService,
            ToastNotificationService toastService)
            : base(newProjectMediator, logger)
        {
            // Store properly typed mediator
            _mediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
            
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            _jamaConnectService = jamaConnectService ?? throw new ArgumentNullException(nameof(jamaConnectService));
            _toastService = toastService ?? throw new ArgumentNullException(nameof(toastService));
            SelectDocumentCommand = new AsyncRelayCommand(SelectDocumentAsync);
            ChooseProjectSaveLocationCommand = new RelayCommand(ChooseProjectSaveLocation);
            CreateProjectCommand = new RelayCommand(CreateProject);
            ValidateWorkspaceCommand = new AsyncRelayCommand(ValidateWorkspaceAsync, CanValidateWorkspace);
            CancelCommand = new RelayCommand(() => Cancel());
            
            // Initialize state
            Initialize();
            
            // Set title for BaseDomainViewModel
            Title = "New Project Workflow";
        }

        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====
        
        protected override async Task SaveAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Saving project...";
                
                // Save current project state (if applicable)
                await Task.CompletedTask; // No specific save operation for workflow
                
                StatusMessage = "Project saved";
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[NewProject] Error saving project");
                ErrorMessage = $"Error saving project: {ex.Message}";
                HasErrors = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        protected override void Cancel()
        {
            // Reset form to initial state
            Initialize(forceReset: true);
            StatusMessage = "Project creation cancelled";
            ProjectCancelled?.Invoke(this, EventArgs.Empty);
        }

        protected override async Task RefreshAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Refreshing...";
                
                // Refresh workspace validation if workspace name exists
                if (!string.IsNullOrEmpty(WorkspaceName))
                {
                    await ValidateWorkspaceAsync();
                }
                
                StatusMessage = "Refreshed";
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[NewProject] Error refreshing");
                ErrorMessage = $"Error refreshing: {ex.Message}";
                HasErrors = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        protected override bool CanSave()
        {
            return !IsBusy && !string.IsNullOrEmpty(ProjectName) && !string.IsNullOrEmpty(ProjectSavePath);
        }

        protected override bool CanCancel()
        {
            return !IsBusy;
        }

        protected override bool CanRefresh()
        {
            return !IsBusy;
        }

        partial void OnWorkspaceNameChanged(string value)
        {
            HasWorkspaceName = !string.IsNullOrWhiteSpace(value);
            UpdateCanProceed();
            
            // Reset workspace creation status when name changes
            IsWorkspaceCreated = false;
            _validatedAnythingLLMWorkspaceSlug = null;
            
            // Notify the command that CanExecute may have changed
            ((AsyncRelayCommand)ValidateWorkspaceCommand).NotifyCanExecuteChanged();
        }
        
        private bool CanValidateWorkspace()
        {
            var canValidate = !string.IsNullOrWhiteSpace(WorkspaceName) && !IsWorkspaceCreated && !IsValidatingWorkspace;
            TestCaseEditorApp.Services.Logging.Log.Debug($"[WORKSPACE] CanValidateWorkspace: WorkspaceName='{WorkspaceName}', IsWorkspaceCreated={IsWorkspaceCreated}, IsValidatingWorkspace={IsValidatingWorkspace}, Result={canValidate}");
            return canValidate;
        }

        partial void OnIsWorkspaceCreatedChanged(bool value)
        {
            // Update CanProceed when workspace creation status changes
            UpdateCanProceed();
            
            // Notify the command that CanExecute may have changed
            ((AsyncRelayCommand)ValidateWorkspaceCommand).NotifyCanExecuteChanged();
        }
        
        partial void OnIsValidatingWorkspaceChanged(bool value)
        {
            // Notify the command that CanExecute may have changed during validation
            ((AsyncRelayCommand)ValidateWorkspaceCommand).NotifyCanExecuteChanged();
        }

        partial void OnSelectedDocumentPathChanged(string value)
        {
            HasSelectedDocument = !string.IsNullOrWhiteSpace(value) &&
                                  (File.Exists(value) || value.StartsWith("jama://project/", StringComparison.OrdinalIgnoreCase));
            UpdateCanProceed();
            OnPropertyChanged(nameof(CreateProjectButtonText));
            OnPropertyChanged(nameof(CreateProjectButtonTooltip));
            
            // Save to mediator for form persistence
            SaveFormDataToMediator();
        }

        partial void OnProjectSavePathChanged(string value)
        {
            HasProjectSavePath = !string.IsNullOrWhiteSpace(value);
            UpdateCanProceed();
            OnPropertyChanged(nameof(CreateProjectButtonText));
            OnPropertyChanged(nameof(CreateProjectButtonTooltip));
            
            // Save to mediator for form persistence
            SaveFormDataToMediator();
        }

        partial void OnProjectNameChanged(string value)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[NewProjectWorkflowViewModel] ProjectName changed: new='{value}'");
            HasProjectName = !string.IsNullOrWhiteSpace(value);
            UpdateCanProceed();
            OnPropertyChanged(nameof(CreateProjectButtonText));
            OnPropertyChanged(nameof(CreateProjectButtonTooltip));
            
            // Save to mediator for form persistence
            SaveFormDataToMediator();
        }

        partial void OnIsProjectCreatedChanged(bool value)
        {
            OnPropertyChanged(nameof(CreateProjectButtonText));
            OnPropertyChanged(nameof(CreateProjectButtonTooltip));
        }

        private void UpdateCanProceed()
        {
            var oldCanProceed = CanProceed;
            
            // All required fields must be filled - allow even if project is open (user will get warning dialog)
            var newCanProceed = HasWorkspaceName && HasSelectedDocument && HasProjectSavePath && HasProjectName && IsWorkspaceCreated;
            
            // Debug logging to help troubleshoot
            TestCaseEditorApp.Services.Logging.Log.Debug($"[UpdateCanProceed] " +
                $"HasWorkspaceName={HasWorkspaceName}, " +
                $"HasSelectedDocument={HasSelectedDocument}, " +
                $"HasProjectSavePath={HasProjectSavePath}, " +
                $"HasProjectName={HasProjectName}, " +
                $"IsWorkspaceCreated={IsWorkspaceCreated}, " +
                $"CanProceed={newCanProceed}");
            
            // Force property change notification
            CanProceed = newCanProceed;
            OnPropertyChanged(nameof(CanProceed));
            OnPropertyChanged(nameof(CreateProjectButtonText));
            OnPropertyChanged(nameof(CreateProjectButtonTooltip));
            
            // Notify other ViewModels via mediator
            var workflowState = new ProjectWorkflowState
            {
                CanProceed = CanProceed,
                HasWorkspaceName = HasWorkspaceName,
                IsWorkspaceCreated = IsWorkspaceCreated,
                HasSelectedDocument = HasSelectedDocument,
                HasProjectName = HasProjectName,
                HasProjectSavePath = HasProjectSavePath
            };
            ProjectWorkflowMediator.NotifyWorkflowStateChanged(workflowState);
                
            if (oldCanProceed != CanProceed)
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[UpdateCanProceed] CanProceed changed from {oldCanProceed} to {CanProceed}");
                // Removed redundant "Ready to create project!" toast - button state is sufficient feedback
            }
        }

        private async Task SelectDocumentAsync()
        {
            var sourceChoice = System.Windows.MessageBox.Show(
                "Choose requirements source:\n\n" +
                "Yes = Pull from Jama project\n" +
                "No = Select Word document\n" +
                "Cancel = Do nothing",
                "Requirements Source",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Question);

            if (sourceChoice == System.Windows.MessageBoxResult.Cancel)
            {
                return;
            }

            if (sourceChoice == System.Windows.MessageBoxResult.No)
            {
                // Explicit Word-document path
                var docDialog = new OpenFileDialog
                {
                    Title = "Select Requirements Document",
                    Filter = "Word Documents (*.docx)|*.docx|All Files (*.*)|*.*",
                    RestoreDirectory = true
                };

                if (docDialog.ShowDialog() == true)
                {
                    SelectedDocumentPath = docDialog.FileName;

                    if (string.IsNullOrWhiteSpace(ProjectName))
                    {
                        ProjectName = Path.GetFileNameWithoutExtension(docDialog.FileName);
                    }

                    var fileName = System.IO.Path.GetFileName(docDialog.FileName);
                    _toastService.ShowToast($"Requirements document selected: {fileName}", durationSeconds: 3, type: ToastType.Success);

                    OnPropertyChanged(nameof(CreateProjectButtonText));
                    OnPropertyChanged(nameof(CreateProjectButtonTooltip));
                }

                return;
            }

            // Preferred path: select Jama project directly for import
            if (_jamaConnectService.IsConfigured)
            {
                try
                {
                        var projects = await _jamaConnectService.GetProjectsAsync();
                        if (projects.Count > 0)
                        {
                            var projectList = string.Join(Environment.NewLine, projects.Select(p => $"{p.Id}: {p.Name}"));

                            // Show the available IDs explicitly before asking for input.
                            var previewList = string.Join(Environment.NewLine, projects.Take(20).Select(p => $"{p.Id}: {p.Name}"));
                            var suffix = projects.Count > 20 ? Environment.NewLine + "..." : string.Empty;
                            System.Windows.MessageBox.Show(
                                $"Found {projects.Count} Jama project(s)." + Environment.NewLine + Environment.NewLine +
                                previewList + suffix + Environment.NewLine + Environment.NewLine +
                                "Click OK, then enter one of the Project IDs.",
                                "Available Jama Projects",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Information);

                            var selectedIdText = Interaction.InputBox(
                                "Enter Jama Project ID to import requirements from:" + Environment.NewLine + Environment.NewLine +
                                projectList,
                                "Select Jama Project",
                                projects[0].Id.ToString());

                            if (!string.IsNullOrWhiteSpace(selectedIdText) && int.TryParse(selectedIdText, out var selectedProjectId))
                            {
                                var selectedProject = projects.FirstOrDefault(p => p.Id == selectedProjectId);
                                if (selectedProject != null)
                                {
                                    SelectedDocumentPath = $"jama://project/{selectedProject.Id}|{Uri.EscapeDataString(selectedProject.Name)}";

                                    if (string.IsNullOrWhiteSpace(ProjectName))
                                    {
                                        ProjectName = selectedProject.Name;
                                    }

                                    _toastService.ShowToast($"Jama project selected: {selectedProject.Name} (ID: {selectedProject.Id})", durationSeconds: 3, type: ToastType.Success);
                                    OnPropertyChanged(nameof(CreateProjectButtonText));
                                    OnPropertyChanged(nameof(CreateProjectButtonTooltip));
                                    return;
                                }

                                _toastService.ShowToast($"Project ID {selectedProjectId} was not found in the available Jama projects list.", durationSeconds: 4, type: ToastType.Warning);
                                return;
                            }

                            // User cancelled selection; do not force fallback dialog.
                            return;
                        }

                        _toastService.ShowToast("No Jama projects found for this account.", durationSeconds: 4, type: ToastType.Warning);
                        System.Windows.MessageBox.Show(
                            "Connection succeeded, but no Jama projects were returned for this account." + Environment.NewLine + Environment.NewLine +
                            "Verify project permissions/scopes for this user in Jama.",
                            "No Jama Projects Found",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                        return;
                }
                catch (Exception ex)
                {
                    if (TrySelectJamaProjectByManualId(ex.Message))
                    {
                        return;
                    }

                    TestCaseEditorApp.Services.Logging.Log.Warn($"[NewProject] Jama project selection failed: {ex.Message}");
                    _toastService.ShowToast($"Jama project selection failed: {ex.Message}", durationSeconds: 5, type: ToastType.Warning);
                    System.Windows.MessageBox.Show(
                        "Jama project retrieval failed:" + Environment.NewLine + ex.Message,
                        "Jama Project Retrieval Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }
            }

            // Jama chosen but not configured
            _toastService.ShowToast(
                "Jama is not configured on this machine. Set JAMA_BASE_URL and one of: JAMA_API_TOKEN, JAMA_USERNAME/JAMA_PASSWORD, or JAMA_CLIENT_ID/JAMA_CLIENT_SECRET.",
                durationSeconds: 6,
                type: ToastType.Warning);
        }

        private bool TrySelectJamaProjectByManualId(string? errorMessage)
        {
            if (!IsKnownJamaProjectsListingServerError(errorMessage))
            {
                return false;
            }

            var selectedIdText = Interaction.InputBox(
                "Jama project listing failed due to a Jama server error (ArrayIndexOutOfBoundsException)." + Environment.NewLine + Environment.NewLine +
                "You can continue by entering a Jama Project ID manually." + Environment.NewLine +
                "Leave blank to cancel.",
                "Manual Jama Project Selection",
                "");

            if (string.IsNullOrWhiteSpace(selectedIdText))
            {
                return true;
            }

            if (!int.TryParse(selectedIdText, out var selectedProjectId))
            {
                _toastService.ShowToast("Invalid Jama Project ID. Please enter a numeric ID.", durationSeconds: 4, type: ToastType.Warning);
                return true;
            }

            var selectedNameText = Interaction.InputBox(
                "Optional: Enter project name (for display/project name defaults).",
                "Manual Jama Project Name",
                $"Jama Project {selectedProjectId}");

            var projectName = string.IsNullOrWhiteSpace(selectedNameText)
                ? $"Jama Project {selectedProjectId}"
                : selectedNameText.Trim();

            SelectedDocumentPath = $"jama://project/{selectedProjectId}|{Uri.EscapeDataString(projectName)}";

            if (string.IsNullOrWhiteSpace(ProjectName))
            {
                ProjectName = projectName;
            }

            _toastService.ShowToast($"Jama project selected by ID: {selectedProjectId}", durationSeconds: 4, type: ToastType.Success);
            OnPropertyChanged(nameof(CreateProjectButtonText));
            OnPropertyChanged(nameof(CreateProjectButtonTooltip));
            return true;
        }

        private static bool IsKnownJamaProjectsListingServerError(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.Contains("ArrayIndexOutOfBoundsException", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("Index 1 out of bounds for length 1", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("IndexOutOfBounds", StringComparison.OrdinalIgnoreCase);
        }

        private void ChooseProjectSaveLocation()
        {
            var result = _mediator.ShowSaveProjectDialog(ProjectName);
            
            if (result.Success)
            {
                ProjectSavePath = result.FilePath;
                
                // Update project name if user changed it via filename
                if (!string.IsNullOrWhiteSpace(result.ProjectName))
                {
                    ProjectName = result.ProjectName;
                }
                
                // Provide user feedback
                var fileName = Path.GetFileName(result.FilePath);
                _toastService.ShowToast($"Project save location selected: {fileName}", durationSeconds: 3, type: ToastType.Success);
            }
        }

        private async void CreateProject()
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

            try
            {
                // Debug: Log the parameters being passed
                TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Calling CreateNewProjectWithWarningAsync with documentPath: '{SelectedDocumentPath}'");
                
                // Call the workspace management mediator to complete the project creation with proper warning handling
                var workspaceSlugForProject = _validatedAnythingLLMWorkspaceSlug ?? WorkspaceName;
                TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Using AnythingLLM workspace slug '{workspaceSlugForProject}' for workspace '{WorkspaceName}'");
                var creationSuccessful = await _mediator.CreateNewProjectWithWarningAsync(
                    workspaceSlugForProject,
                    ProjectName,
                    ProjectSavePath,
                    SelectedDocumentPath,
                    WorkspaceName);
                
                // Always mark project as created if method completed without exception
                // Even if requirements import failed, the project file was still created successfully
                IsProjectCreated = true;
                
                // Fire completion event to clear cached workflow instance
                var completedArgs = new NewProjectCompletedEventArgs
                {
                    WorkspaceName = WorkspaceName,
                    WorkspaceDescription = WorkspaceDescription,
                    DocumentPath = SelectedDocumentPath,
                    AutoExportEnabled = AutoExportEnabled,
                    ProjectSavePath = ProjectSavePath,
                    ProjectName = ProjectName
                };
                ProjectCompleted?.Invoke(this, completedArgs);
                
                // Log the outcome for debugging
                if (creationSuccessful)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[PROJECT] Project creation completed successfully with requirements import");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn("[PROJECT] Project creation completed but requirements import unsuccessful");
                }
                
                // Fire the event for any remaining legacy listeners
                var args = new NewProjectCompletedEventArgs
                {
                    WorkspaceName = WorkspaceName,
                    WorkspaceDescription = WorkspaceDescription,
                    DocumentPath = SelectedDocumentPath,
                    AutoExportEnabled = AutoExportEnabled,
                    ProjectSavePath = ProjectSavePath,
                    ProjectName = ProjectName
                };
                ProjectCreated?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[PROJECT] Error creating project");
                _mediator.ShowNotification(
                    $"Error creating project: {ex.Message}", 
                    DomainNotificationType.Error);
                _mediator.HideProgress();
            }
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
                    // Create the workspace with optimal configuration immediately
                    IsValidatingWorkspace = true;
                    
                    try
                    {
                        // Use full configuration method to apply optimal settings during project creation
                        var (createdWorkspace, configurationSuccessful) = await _anythingLLMService.CreateAndConfigureWorkspaceAsync(
                            WorkspaceName,
                            preserveOriginalName: true, // Preserve user's chosen name
                            onProgress: (message) => {
                                // Could add progress updates to UI here if needed
                                TestCaseEditorApp.Services.Logging.Log.Info($"[NewProject] {message}");
                            });
                            
                        if (createdWorkspace != null)
                        {
                            _validatedAnythingLLMWorkspaceSlug = createdWorkspace.Slug;
                            TestCaseEditorApp.Services.Logging.Log.Info($"[NewProject] Created AnythingLLM workspace '{createdWorkspace.Name}' with slug '{createdWorkspace.Slug}'");

                            // Clear loading state immediately
                            IsValidatingWorkspace = false;
                            
                            // Show success toast with configuration status
                            var statusMessage = configurationSuccessful 
                                ? $"Project workspace '{WorkspaceName}' created with optimized settings!"
                                : $"Project workspace '{WorkspaceName}' created (settings will be applied during first analysis)";
                            _toastService.ShowToast(statusMessage, durationSeconds: 5, type: ToastType.Success);
                            
                            // Clear validation message UI and update status
                            WorkspaceValidationMessage = "";
                            HasValidationMessage = false;
                            HasWorkspaceName = true;
                            IsWorkspaceCreated = true;
                            
                            // Update CanProceed status after workspace creation
                            UpdateCanProceed();
                            OnPropertyChanged(nameof(CreateProjectButtonText));
                            OnPropertyChanged(nameof(CreateProjectButtonTooltip));
                        }
                        else
                        {
                            _validatedAnythingLLMWorkspaceSlug = null;
                            IsValidatingWorkspace = false;
                            WorkspaceValidationMessage = $"Failed to create workspace '{WorkspaceName}'. Please try again.";
                            WorkspaceValidationSuccess = false;
                            HasValidationMessage = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _validatedAnythingLLMWorkspaceSlug = null;
                        IsValidatingWorkspace = false;
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

        public void Initialize(bool forceReset = false)
        {
            // Only reset if explicitly requested or if project was already completed
            if (forceReset || IsProjectCreated)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[NewProject] Resetting form data");
                WorkspaceName = "";
                WorkspaceDescription = "";
                SelectedDocumentPath = "";
                ProjectSavePath = "";
                ProjectName = "";
                AutoExportEnabled = true;
                
                // Reset workflow state
                IsWorkspaceCreated = false;
                IsProjectCreated = false;
                _validatedAnythingLLMWorkspaceSlug = null;
                WorkspaceValidationMessage = "";
                HasValidationMessage = false;
                IsDuplicateName = false;
                
                // Clear mediator persistence when resetting
                ((INewProjectMediator)_mediator).ClearDraftProjectInfo();
                
                UpdateCanProceed();
            }
            else
            {
                // Load persisted form data for architectural compliance
                var (draftProjectName, draftProjectPath, draftRequirementsPath) = 
                    ((INewProjectMediator)_mediator).GetDraftProjectInfo();
                
                if (!string.IsNullOrWhiteSpace(draftProjectName) || 
                    !string.IsNullOrWhiteSpace(draftProjectPath) || 
                    !string.IsNullOrWhiteSpace(draftRequirementsPath))
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug("[NewProject] Loading persisted form data");
                    
                    if (!string.IsNullOrWhiteSpace(draftProjectName))
                        ProjectName = draftProjectName;
                    if (!string.IsNullOrWhiteSpace(draftProjectPath))
                        ProjectSavePath = draftProjectPath;
                    if (!string.IsNullOrWhiteSpace(draftRequirementsPath))
                        SelectedDocumentPath = draftRequirementsPath;
                        
                    UpdateCanProceed();
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug("[NewProject] No persisted form data found");
                }
            }
        }

        /// <summary>
        /// Debug method to test project name binding
        /// </summary>
        public void TestProjectNameBinding()
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[DEBUG] Testing ProjectName binding...");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[DEBUG] Current ProjectName: '{ProjectName}'");
            
            // Test programmatic change
            ProjectName = "Test_Project_" + DateTime.Now.Ticks;
            TestCaseEditorApp.Services.Logging.Log.Debug($"[DEBUG] After programmatic change: '{ProjectName}'");
            
            // Verify property change notification
            TestCaseEditorApp.Services.Logging.Log.Debug($"[DEBUG] HasProjectName: {HasProjectName}");
        }
        
        /// <summary>
        /// Saves current form data to mediator for architectural-compliant persistence.
        /// Maintains user experience without violating fail-fast validation.
        /// </summary>
        private void SaveFormDataToMediator()
        {
            try
            {
                // Only save if we have meaningful data and project isn't completed
                if (!IsProjectCreated)
                {
                    ((INewProjectMediator)_mediator).SaveDraftProjectInfo(
                        ProjectName,
                        ProjectSavePath, 
                        SelectedDocumentPath);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[NewProject] Error saving form data to mediator");
                // Don't interrupt user workflow for persistence errors
            }
        }
    }

    public class NewProjectCompletedEventArgs : EventArgs
    {
        public string WorkspaceName { get; set; } = "";
        public string WorkspaceDescription { get; set; } = "";
        public string DocumentPath { get; set; } = "";
        public bool AutoExportEnabled { get; set; }
        public string ProjectSavePath { get; set; } = "";
        public string ProjectName { get; set; } = "";
    }
}
