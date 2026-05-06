using System;
using System.IO;
using System.Linq;
using System.Text.Json;
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
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Events;
using System.Collections.Generic;

namespace TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels
{
    public partial class OpenProjectWorkflowViewModel : BaseDomainViewModel
    {
        // Domain mediator (properly typed)
        private new readonly IOpenProjectMediator _mediator;
        private readonly IPersistenceService _persistenceService;
        
        [ObservableProperty]
        private string selectedProjectPath = "";

        [ObservableProperty]
        private int requirementCount;

        [ObservableProperty]
        private int analyzedCount;

        [ObservableProperty]
        private int testCasesGeneratedCount;

        public int AnalyzedPercentage => RequirementCount > 0 ? (int)Math.Round((double)AnalyzedCount / RequirementCount * 100) : 0;
        public int TestCasesPercentage => RequirementCount > 0 ? (int)Math.Round((double)TestCasesGeneratedCount / RequirementCount * 100) : 0;
        
        [ObservableProperty]
        private bool isProjectSelected = false;
        
        partial void OnIsProjectSelectedChanged(bool value)
        {
            _logger.LogInformation($"*** OnIsProjectSelectedChanged: value={value}");
            OnPropertyChanged(nameof(SelectButtonText));
            OnPropertyChanged(nameof(OpenButtonText));
            _logger.LogInformation($"*** About to call NotifyCanExecuteChanged from OnIsProjectSelectedChanged");
            ((AsyncRelayCommand)OpenSelectedProjectCommand).NotifyCanExecuteChanged();
            _logger.LogInformation($"*** NotifyCanExecuteChanged called from OnIsProjectSelectedChanged");
        }
        
        [ObservableProperty]
        private bool isLoadingProject = false;
        
        partial void OnIsLoadingProjectChanged(bool value)
        {
            OnPropertyChanged(nameof(SelectButtonText));
            OnPropertyChanged(nameof(OpenButtonText));
        }
        
        [ObservableProperty]
        private string projectName = "";
        
        partial void OnProjectNameChanged(string value)
        {
            OnPropertyChanged(nameof(OpenButtonText));
        }
        
        [ObservableProperty]
        private string projectStatus = "No project selected";
        
        [ObservableProperty]
        private DateTime? lastModified;

        [ObservableProperty]
        private bool isMainOpenButtonActive = false;

        partial void OnIsMainOpenButtonActiveChanged(bool value)
        {
            _logger.LogInformation($"*** IsMainOpenButtonActive changed to: {value} - STACK TRACE: {Environment.StackTrace}");
        }

        // Commands
        public ICommand SelectProjectFileCommand { get; }
        public ICommand OpenSelectedProjectCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand OpenFileDirectlyCommand { get; }

        public OpenProjectWorkflowViewModel(IOpenProjectMediator mediator, IPersistenceService persistenceService, ILogger<OpenProjectWorkflowViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
            
            // Initialize commands
            SelectProjectFileCommand = new RelayCommand(SelectProjectFile);
            OpenSelectedProjectCommand = new AsyncRelayCommand(OpenSelectedProjectAsync, CanOpenSelectedProject);
            ClearSelectionCommand = new RelayCommand(ClearSelection);
            OpenFileDirectlyCommand = new AsyncRelayCommand(OpenFileDirectlyAsync);
            
            // Subscribe to domain events
            _mediator.Subscribe<OpenProjectEvents.ProjectFileSelected>(OnProjectFileSelected);
            _mediator.Subscribe<OpenProjectEvents.ProjectOpened>(OnProjectOpened);
            _mediator.Subscribe<OpenProjectEvents.ProjectOpenFailed>(OnProjectOpenFailed);
            _mediator.Subscribe<OpenProjectEvents.WorkspaceLoaded>(OnWorkspaceLoaded);
        }

        private void SelectProjectFile()
        {
            try
            {
                IsLoadingProject = true;
                ProjectStatus = "Selecting project file...";
                
                _logger.LogInformation("Selecting project file");
                
                // Use file dialog service through mediator
                var fileDialog = new OpenFileDialog
                {
                    Title = "Open Test Case Editor Project",
                    Filter = "Test Case Editor Session|*.tcex.json|JSON Files|*.json|All Files|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (fileDialog.ShowDialog() == true)
                {
                    var selectedPath = fileDialog.FileName;
                    
                    if (!string.IsNullOrWhiteSpace(selectedPath))
                    {
                        SelectedProjectPath = selectedPath;
                        ProjectName = Path.GetFileNameWithoutExtension(selectedPath);
                        if (ProjectName.EndsWith(".tcex", StringComparison.OrdinalIgnoreCase))
                        {
                            ProjectName = Path.GetFileNameWithoutExtension(ProjectName);
                        }
                        
                        IsProjectSelected = true;
                        IsMainOpenButtonActive = true; // Set main button as active

                        ProjectStatus = $"Selected: {ProjectName}";
                        
                        // Get file info
                        if (File.Exists(selectedPath))
                        {
                            var fileInfo = new FileInfo(selectedPath);
                            LastModified = fileInfo.LastWriteTime;
                        }
                        
                        _logger.LogInformation($"Project file selected: {selectedPath}");
                    }
                }
                else
                {
                    ProjectStatus = "No project selected";
                    _logger.LogInformation("Project file selection cancelled");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error selecting project file");
                ProjectStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoadingProject = false;
                // Update command states after IsLoadingProject is reset
                _logger.LogInformation($"*** Calling NotifyCanExecuteChanged for OpenSelectedProjectCommand");
                ((AsyncRelayCommand)OpenSelectedProjectCommand).NotifyCanExecuteChanged();
            }
        }

        private async Task OpenSelectedProjectAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedProjectPath))
            {
                return;
            }

            try
            {
                IsLoadingProject = true;
                ProjectStatus = "Opening project...";
                
                _logger.LogInformation($"Opening selected project: {SelectedProjectPath}");
                
                // Use mediator to open the project file
                var success = await _mediator.OpenProjectFileAsync(SelectedProjectPath);
                
                if (success)
                {
                    ProjectStatus = "Project opened successfully";
                    _logger.LogInformation($"Project opened successfully: {SelectedProjectPath}");
                }
                else
                {
                    ProjectStatus = "Failed to open project";
                    _logger.LogWarning($"Failed to open project: {SelectedProjectPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error opening project: {SelectedProjectPath}");
                ProjectStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoadingProject = false;
            }
        }

        private bool CanOpenSelectedProject()
        {
            var canOpen = IsProjectSelected && !IsLoadingProject && !string.IsNullOrWhiteSpace(SelectedProjectPath);
            _logger.LogInformation($"*** CanOpenSelectedProject check: IsProjectSelected={IsProjectSelected}, IsLoadingProject={IsLoadingProject}, HasPath={!string.IsNullOrWhiteSpace(SelectedProjectPath)}, Result={canOpen}");
            return canOpen;
        }

        private void ClearSelection()
        {
            SelectedProjectPath = "";
            IsProjectSelected = false;
            ProjectName = "";
            ProjectStatus = "No project selected";
            RequirementCount = 0;
            LastModified = null;
            
            ((AsyncRelayCommand)OpenSelectedProjectCommand).NotifyCanExecuteChanged();
            
            _logger.LogInformation("Project selection cleared");
        }

        private async Task OpenFileDirectlyAsync()
        {
            try
            {
                IsLoadingProject = true;
                ProjectStatus = "Opening file dialog...";
                
                var fileDialog = new OpenFileDialog
                {
                    Title = "Open Test Case Editor Project",
                    Filter = "Test Case Editor Session|*.tcex.json|JSON Files|*.json|All Files|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (fileDialog.ShowDialog() == true)
                {
                    var selectedPath = fileDialog.FileName;
                    await OpenProjectFile(selectedPath);
                }
                else
                {
                    ProjectStatus = "No project selected";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in direct file open");
                ProjectStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoadingProject = false;
            }
        }

        private async Task OpenProjectFile(string filePath)
        {
            // Update UI state
            SelectedProjectPath = filePath;
            ProjectName = Path.GetFileNameWithoutExtension(filePath);
            if (ProjectName.EndsWith(".tcex", StringComparison.OrdinalIgnoreCase))
            {
                ProjectName = Path.GetFileNameWithoutExtension(ProjectName);
            }
            
            IsProjectSelected = true;
            
            // Get file info
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                LastModified = fileInfo.LastWriteTime;
                
                // Populate metadata for main project display
                PopulateMainProjectMetadata(filePath);
            }
            else
            {
                // Clear metadata if file doesn't exist
                RequirementCount = 0;
                AnalyzedCount = 0;
                TestCasesGeneratedCount = 0;
            }

            // Add to recent projects

            
            // Open through mediator
            var success = await _mediator.OpenProjectFileAsync(filePath);
            
            if (success)
            {
                ProjectStatus = "Project opened successfully";
                _logger.LogInformation($"Project opened successfully: {filePath}");
            }
            else
            {
                ProjectStatus = "Failed to open project";
                _logger.LogWarning($"Failed to open project: {filePath}");
            }
        }

        private void PopulateMainProjectMetadata(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    RequirementCount = 0;
                    AnalyzedCount = 0;
                    TestCasesGeneratedCount = 0;
                    return;
                }

                var jsonContent = File.ReadAllText(filePath);
                using var document = JsonDocument.Parse(jsonContent);
                var root = document.RootElement;

                // Count requirements
                if (root.TryGetProperty("Requirements", out var reqsElement) && reqsElement.ValueKind == JsonValueKind.Array)
                {
                    RequirementCount = reqsElement.GetArrayLength();
                    
                    // Count analyzed and test cases
                    AnalyzedCount = 0;
                    TestCasesGeneratedCount = 0;
                    
                    foreach (var req in reqsElement.EnumerateArray())
                    {
                        if (req.TryGetProperty("IsAnalyzed", out var analyzed) && analyzed.GetBoolean())
                        {
                            AnalyzedCount++;
                        }
                        
                        if (req.TryGetProperty("TestCases", out var testCases) && testCases.ValueKind == JsonValueKind.Array)
                        {
                            TestCasesGeneratedCount += testCases.GetArrayLength();
                        }
                    }
                }
                else
                {
                    RequirementCount = 0;
                    AnalyzedCount = 0;
                    TestCasesGeneratedCount = 0;
                }

                // Notify UI of percentage changes
                OnPropertyChanged(nameof(AnalyzedPercentage));
                OnPropertyChanged(nameof(TestCasesPercentage));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading metadata for main project: {filePath}");
                RequirementCount = 0;
                AnalyzedCount = 0;
                TestCasesGeneratedCount = 0;
            }
        }


        // Required abstract method implementations
        protected override async Task SaveAsync()
        {
            // Open project workflow doesn't have save functionality
            await Task.CompletedTask;
        }

        protected override bool CanSave() => false; // No save functionality
        
        protected override bool CanCancel() => true; // Can always cancel/go back
        
        protected override void Cancel()
        {
            ClearSelection();
        }
        
        protected override bool CanRefresh() => true; // Can refresh file info
        
        protected override async Task RefreshAsync()
        {
            if (IsProjectSelected && File.Exists(SelectedProjectPath))
            {
                var fileInfo = new FileInfo(SelectedProjectPath);
                LastModified = fileInfo.LastWriteTime;
            }
            await Task.CompletedTask;
        }

        // Event handlers
        private void OnProjectFileSelected(OpenProjectEvents.ProjectFileSelected eventData)
        {
            SelectedProjectPath = eventData.FilePath;
            IsProjectSelected = true;
            ProjectName = Path.GetFileNameWithoutExtension(eventData.FilePath);
            if (ProjectName.EndsWith(".tcex", StringComparison.OrdinalIgnoreCase))
            {
                ProjectName = Path.GetFileNameWithoutExtension(ProjectName);
            }
            ProjectStatus = $"Selected: {ProjectName}";
            
            ((AsyncRelayCommand)OpenSelectedProjectCommand).NotifyCanExecuteChanged();
        }

        private void OnProjectOpened(OpenProjectEvents.ProjectOpened eventData)
        {
            ProjectStatus = "Project opened successfully";
            ProjectName = eventData.WorkspaceName;
        }

        private void OnProjectOpenFailed(OpenProjectEvents.ProjectOpenFailed eventData)
        {
            ProjectStatus = $"Failed to open project: {eventData.ErrorMessage}";
        }

        private void OnWorkspaceLoaded(OpenProjectEvents.WorkspaceLoaded eventData)
        {
            RequirementCount = eventData.RequirementCount;
            ProjectStatus = $"Loaded {eventData.RequirementCount} requirements";
        }

        /// <summary>
        /// Property for button text based on current state
        /// </summary>
        public string SelectButtonText
        {
            get
            {
                if (IsLoadingProject) return "Loading...";
                if (IsProjectSelected) return "📁 Change Project";
                return "📁 Select Project File";
            }
        }

        /// <summary>
        /// Property for open button text based on current state
        /// </summary>
        public string OpenButtonText
        {
            get
            {
                if (IsLoadingProject) return "Opening...";
                if (IsProjectSelected) return $"✅ Open {ProjectName}";
                return "Open Project";
            }
        }

        public void DisposeSubscriptions()
        {
            _mediator.Unsubscribe<OpenProjectEvents.ProjectFileSelected>(OnProjectFileSelected);
            _mediator.Unsubscribe<OpenProjectEvents.ProjectOpened>(OnProjectOpened);
            _mediator.Unsubscribe<OpenProjectEvents.ProjectOpenFailed>(OnProjectOpenFailed);
            _mediator.Unsubscribe<OpenProjectEvents.WorkspaceLoaded>(OnWorkspaceLoaded);
        }
    }
}