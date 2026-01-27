using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.ViewModels;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.Events;

namespace TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels
{
    public partial class NewProjectWorkflowViewModel : BaseDomainViewModel
    {
        // Domain mediator (properly typed)
        private new readonly INewProjectMediator _mediator;
        
        private readonly AnythingLLMService _anythingLLMService;
        
        // Busy state properties for spinner feedback
        [ObservableProperty]
        private bool isCreatingProject = false;
        
        [ObservableProperty]
        private bool isImportingRequirements = false;

        [ObservableProperty]
        private string jamaConnectionStatus = "Ready to connect";

        [ObservableProperty]
        private bool hasConnectionError = false;
        
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
        
        // Jama Connect integration
        [ObservableProperty]
        private bool hasJamaConnection = false;
        
        [ObservableProperty]
        private bool isTestingConnection = false;
        
        [ObservableProperty]
        private bool hasJamaRequirements = false;
        
        // Import mode toggle - true = Jama (default), false = Document
        [ObservableProperty]
        private bool isJamaImportMode = true;
        
        [ObservableProperty]
        private ObservableCollection<JamaProjectItem> availableProjects = new();
        
        [ObservableProperty]
        private JamaProjectItem? selectedProject;
        
        [ObservableProperty]
        private bool isLoadingProjects = false;
        
        [ObservableProperty]
        private bool isLoadingRequirements = false;
        
        [ObservableProperty]
        private int requirementsCount = 0;
        
        [ObservableProperty]
        private bool showProjectSelection = false;
        
        // Computed properties for smart button UX
        public string CreateProjectButtonText
        {
            get
            {
                if (IsCreatingProject)
                    return "🔄 Creating...";
                if (!HasWorkspaceName)
                    return "⚠️ Create AnythingLLM Workspace First";
                if (!IsWorkspaceCreated)
                    return "⚠️ Workspace Not Validated";
                    
                // Check requirements source based on import mode
                if (IsJamaImportMode)
                {
                    if (SelectedProject == null)
                        return "⚠️ Select Jama Project";
                }
                else
                {
                    if (!HasSelectedDocument)
                        return "⚠️ Select Requirements Document";
                }
                
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
                    
                // Check requirements source based on import mode
                if (IsJamaImportMode)
                {
                    if (SelectedProject == null)
                        return "Select a Jama project to import requirements from";
                }
                else
                {
                    if (!HasSelectedDocument)
                        return "Select a Word document containing your requirements";
                }
                
                if (!HasProjectName)
                    return "Enter a name for your new project";
                if (!HasProjectSavePath)
                    return "Choose where to save your project file";
                if (IsProjectCreated)
                    return "Project has been successfully created!";
                return $"All prerequisites met - ready to create project with {(IsJamaImportMode ? "Jama" : "document")} requirements!";
            }
        }

        // Commands
        public ICommand SelectDocumentCommand { get; }
        public ICommand ChooseProjectSaveLocationCommand { get; }
        public ICommand CreateProjectCommand { get; }
        public ICommand ValidateWorkspaceCommand { get; }
        public new ICommand CancelCommand { get; }
        public IAsyncRelayCommand TestJamaConnectionCommand { get; }
        public IAsyncRelayCommand ImportFromJamaCommand { get; }
        public IAsyncRelayCommand LoadJamaProjectsCommand { get; }
        public IAsyncRelayCommand LoadRequirementsCommand { get; }
        public IAsyncRelayCommand<object> ImportSelectedRequirementsCommand { get; }

        // Events
        public event EventHandler<NewProjectCompletedEventArgs>? ProjectCreated;
        public event EventHandler? ProjectCancelled;
        
        public NewProjectWorkflowViewModel(
            INewProjectMediator newProjectMediator,
            ILogger<NewProjectWorkflowViewModel> logger,
            AnythingLLMService anythingLLMService)
            : base(newProjectMediator, logger)
        {
            // Store properly typed mediator
            _mediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            SelectDocumentCommand = new RelayCommand(SelectDocument);
            ChooseProjectSaveLocationCommand = new RelayCommand(ChooseProjectSaveLocation);
            CreateProjectCommand = new RelayCommand(CreateProject);
            ValidateWorkspaceCommand = new AsyncRelayCommand(ValidateWorkspaceAsync, CanValidateWorkspace);
            CancelCommand = new RelayCommand(() => Cancel());
            TestJamaConnectionCommand = new AsyncRelayCommand(TestJamaConnectionAsync);
            ImportFromJamaCommand = new AsyncRelayCommand(ImportFromJamaAsync);
            LoadJamaProjectsCommand = new AsyncRelayCommand(LoadJamaProjectsAsync);
            LoadRequirementsCommand = new AsyncRelayCommand(LoadRequirementsAsync, () => SelectedProject != null);
            ImportSelectedRequirementsCommand = new AsyncRelayCommand<object>(ImportSelectedRequirementsAsync, (param) => {
                return param != null || SelectedProject != null;
            });
            
            // Initialize import mode to Jama by default
            IsJamaImportMode = true;
            
            // Property change handlers for command state
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SelectedProject))
                {
                    LoadRequirementsCommand.NotifyCanExecuteChanged();
                    ImportSelectedRequirementsCommand.NotifyCanExecuteChanged();
                }
                if (e.PropertyName == nameof(RequirementsCount))
                {
                    ImportSelectedRequirementsCommand.NotifyCanExecuteChanged();
                }
            };
            
            // Initialize state
            Initialize();
            
            // Subscribe to domain events
            _mediator.Subscribe<NewProjectEvents.RequirementsImported>(OnRequirementsImported);
            
            // Auto-load Jama projects if in Jama mode and configured
            _ = Task.Run(async () =>
            {
                if (IsJamaImportMode)
                {
                    try
                    {
                        // Check if Jama is configured before attempting to load
                        var (isConfigured, message) = await _mediator.TestJamaConnectionAsync();
                        if (isConfigured)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info("[NewProject] Auto-loading Jama projects on startup");
                            
                            // Switch back to UI thread for the actual loading which updates UI collections
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                            {
                                await LoadJamaProjectsAsync();
                            });
                        }
                        else
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[NewProject] Jama not configured for auto-load: {message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[NewProject] Failed to auto-load Jama projects: {ex.Message}");
                        // Don't throw - just log and continue
                    }
                }
            });
            
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
            HasSelectedDocument = !string.IsNullOrWhiteSpace(value) && File.Exists(value);
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

        partial void OnSelectedProjectChanged(JamaProjectItem? value)
        {
            // Update CanProceed when Jama project selection changes
            UpdateCanProceed();
            
            // Notify command states
            LoadRequirementsCommand.NotifyCanExecuteChanged();
            ImportSelectedRequirementsCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsJamaImportModeChanged(bool value)
        {
            // Update CanProceed when import mode changes (affects requirements source validation)
            UpdateCanProceed();
            
            // Load projects when switching to Jama mode
            if (value && !IsLoadingProjects)
            {
                _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await LoadJamaProjectsAsync();
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error(ex, "Failed to load projects when switching to Jama mode");
                    }
                });
            }
        }

        private void UpdateCanProceed()
        {
            var oldCanProceed = CanProceed;
            
            // Check if we have requirements source: either a document file (in document mode) or selected Jama project (in Jama mode)
            bool hasRequirementsSource = IsJamaImportMode ? (SelectedProject != null) : HasSelectedDocument;
            
            // All required fields must be filled - allow even if project is open (user will get warning dialog)
            var newCanProceed = HasWorkspaceName && hasRequirementsSource && HasProjectSavePath && HasProjectName && IsWorkspaceCreated;
            
            // Debug logging to help troubleshoot
            TestCaseEditorApp.Services.Logging.Log.Debug($"[UpdateCanProceed] " +
                $"HasWorkspaceName={HasWorkspaceName}, " +
                $"IsJamaImportMode={IsJamaImportMode}, " +
                $"SelectedProject={(SelectedProject != null ? SelectedProject.Name : "null")}, " +
                $"HasSelectedDocument={HasSelectedDocument}, " +
                $"HasRequirementsSource={hasRequirementsSource}, " +
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
                HasSelectedDocument = hasRequirementsSource, // Use requirements source status
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
                
                // Update button text
                OnPropertyChanged(nameof(CreateProjectButtonText));
                OnPropertyChanged(nameof(CreateProjectButtonTooltip));
            }
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

            IsCreatingProject = true;
            OnPropertyChanged(nameof(CreateProjectButtonText));
            
            try
            {
                string documentPathToUse = SelectedDocumentPath;
                
                // Check if we're in Jama import mode with a selected project
                if (IsJamaImportMode && SelectedProject != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Creating project with Jama import from project: {SelectedProject.Name} (ID: {SelectedProject.Id})");
                    
                    try
                    {
                        // Import requirements from Jama and get the temporary file path
                        documentPathToUse = await _mediator.ImportJamaRequirementsAsync(SelectedProject.Id, SelectedProject.Name, SelectedProject.Key);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Jama import successful, temporary file created: {documentPathToUse}");
                    }
                    catch (Exception jamaEx)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error(jamaEx, $"[PROJECT] Failed to import from Jama project {SelectedProject.Name}");
                        _mediator.ShowNotification($"Failed to import requirements from Jama project '{SelectedProject.Name}': {jamaEx.Message}", DomainNotificationType.Error);
                        return;
                    }
                }
                
                // Debug: Log the parameters being passed
                TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Calling CreateNewProjectWithWarningAsync with documentPath: '{documentPathToUse}'");
                
                // Call the workspace management mediator to complete the project creation with proper warning handling
                var creationSuccessful = await _mediator.CreateNewProjectWithWarningAsync(WorkspaceName, ProjectName, ProjectSavePath, documentPathToUse);
                
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
            finally
            {
                IsCreatingProject = false;
                OnPropertyChanged(nameof(CreateProjectButtonText));
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
                            // Clear loading state immediately
                            IsValidatingWorkspace = false;
                            
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
                            IsValidatingWorkspace = false;
                            WorkspaceValidationMessage = $"Failed to create workspace '{WorkspaceName}'. Please try again.";
                            WorkspaceValidationSuccess = false;
                            HasValidationMessage = true;
                        }
                    }
                    catch (Exception ex)
                    {
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
        
        // Jama Connect Integration Methods
        
        private async Task TestJamaConnectionAsync()
        {
            IsTestingConnection = true;
            JamaConnectionStatus = "Testing connection...";
            
            try
            {
                var (success, message) = await _mediator.TestJamaConnectionAsync();
                
                if (success)
                {
                    JamaConnectionStatus = "Connected to Jama Connect";
                    HasJamaConnection = true;
                }
                else
                {
                    JamaConnectionStatus = $"Connection failed: {message}";
                    HasJamaConnection = false;
                }
            }
            catch (Exception ex)
            {
                JamaConnectionStatus = $"Error testing connection: {ex.Message}";
                HasJamaConnection = false;
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "Failed to test Jama connection");
            }
            finally
            {
                IsTestingConnection = false;
            }
        }
        
        private async Task ImportFromJamaAsync()
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[ImportFromJama] Current ShowProjectSelection: {ShowProjectSelection}");
                
                // Toggle project selection section
                ShowProjectSelection = !ShowProjectSelection;
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[ImportFromJama] After toggle ShowProjectSelection: {ShowProjectSelection}");
                
                // Always load projects when showing (not just when empty)
                if (ShowProjectSelection)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[ImportFromJama] Loading projects... Current count: {AvailableProjects.Count}");
                    await LoadJamaProjectsAsync();
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "Failed to toggle Jama import");
            }
        }
        
        private async Task LoadJamaProjectsAsync()
        {
            // Guard against concurrent loading
            if (IsLoadingProjects)
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[LoadJamaProjects] Already loading projects, skipping");
                return;
            }
            
            try
            {
                IsLoadingProjects = true;
                HasConnectionError = false;
                JamaConnectionStatus = "Connecting to Jama...";
                
                TestCaseEditorApp.Services.Logging.Log.Info("[LoadJamaProjects] Starting to load projects...");
                var projects = await _mediator.GetJamaProjectsAsync();
                TestCaseEditorApp.Services.Logging.Log.Info($"[LoadJamaProjects] Received {projects.Count} projects from mediator");
                
                AvailableProjects.Clear();
                foreach (var project in projects)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[LoadJamaProjects] Adding project: {project.Name} (ID: {project.Id}, Key: {project.Key})");
                    var projectItem = new JamaProjectItem
                    {
                        Id = project.Id,
                        Name = project.Name,
                        Key = project.Key,
                        Description = project.Description,
                        CreatedDate = project.CreatedDate,
                        ModifiedDate = project.ModifiedDate
                    };
                    
                    AvailableProjects.Add(projectItem);
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[LoadJamaProjects] Final AvailableProjects count: {AvailableProjects.Count}");
                JamaConnectionStatus = $"Found {AvailableProjects.Count} projects";
                TestCaseEditorApp.Services.Logging.Log.Info($"Loaded {AvailableProjects.Count} Jama projects");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "Failed to load Jama projects");
                HasConnectionError = true;
                // Surface the actual exception message so the UI explains the failure (e.g. not configured)
                JamaConnectionStatus = ex.Message ?? "Connection failed";

                // Use mediator to broadcast simple error notification with details
                await _mediator.NotifyConnectionErrorAsync($"Jama connection failed: {ex.Message}");
            }
            finally
            {
                IsLoadingProjects = false;
            }
        }

        [RelayCommand]
        private void SwitchImportMode()
        {
            IsJamaImportMode = !IsJamaImportMode;
            TestCaseEditorApp.Services.Logging.Log.Info($"Switched import mode to: {(IsJamaImportMode ? "Jama" : "Document")}");
        }
        
        private async Task LoadRequirementsAsync()
        {
            if (SelectedProject == null) return;
            
            try
            {
                IsLoadingRequirements = true;
                
                var requirements = await _mediator.GetJamaRequirementsAsync(SelectedProject.Id);
                RequirementsCount = requirements.Count;
                
                TestCaseEditorApp.Services.Logging.Log.Info($"Found {requirements.Count} requirements in Jama project {SelectedProject.Name}");
            }
            catch (Exception ex)
            {
                RequirementsCount = 0;
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to load requirements from Jama project {SelectedProject?.Name}");
            }
            finally
            {
                IsLoadingRequirements = false;
            }
        }
        
        private async Task ImportSelectedRequirementsAsync(object? parameter)
        {
            // If a parameter was passed (from CommandParameter), use it to set SelectedProject
            if (parameter is JamaProjectItem projectItem)
            {
                SelectedProject = projectItem;
            }
            
            if (SelectedProject == null) return;
            
            try
            {
                IsImportingRequirements = true;
                
                // Import through mediator to create requirements JSON file
                var tempPath = await _mediator.ImportJamaRequirementsAsync(SelectedProject.Id, SelectedProject.Name, SelectedProject.Key);
                
                // Set as selected document (now it's a JSON file)
                SelectedDocumentPath = tempPath;
                HasSelectedDocument = true;
                HasJamaRequirements = true;
                
                // Hide project selection since we're done
                ShowProjectSelection = false;
                
                // Publish requirements imported event via mediator
                _mediator.PublishEvent(new NewProjectEvents.RequirementsImported
                {
                    ProjectName = SelectedProject.Name,
                    RequirementCount = 0, // Will be updated by the import process
                    FilePath = tempPath
                });
                
                TestCaseEditorApp.Services.Logging.Log.Info($"Successfully imported requirements from Jama project {SelectedProject.Name} to {tempPath}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to import requirements from Jama project {SelectedProject?.Name}");
            }
            finally
            {
                IsImportingRequirements = false;
            }
        }
        
        /// <summary>
        /// Handle requirements imported event to update UI status
        /// </summary>
        private void OnRequirementsImported(NewProjectEvents.RequirementsImported evt)
        {
            // Ensure UI updates happen on the UI thread
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                JamaConnectionStatus = $"{evt.ProjectName} requirements imported";
            });
        }
        
        /// <summary>
        /// Cleanup subscriptions when the ViewModel is disposed
        /// </summary>
        public override void Dispose()
        {
            // Unsubscribe from mediator events
            _mediator.Unsubscribe<NewProjectEvents.RequirementsImported>(OnRequirementsImported);
            
            base.Dispose();
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
    
    public partial class JamaProjectItem : ObservableObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Key { get; set; } = "";
        public string Description { get; set; } = "";
        public string CreatedDate { get; set; } = "";
        public string ModifiedDate { get; set; } = "";
        
        public string DisplayText => Name;
            
        public string FormattedCreatedDate 
        {
            get 
            {
                if (DateTime.TryParse(CreatedDate, out var date))
                {
                    return date.ToString("MMM dd, yyyy");
                }
                return "";
            }
        }
    }
}
