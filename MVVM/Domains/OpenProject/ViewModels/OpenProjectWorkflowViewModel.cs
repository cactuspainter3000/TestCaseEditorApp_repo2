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
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Events;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Models;
using System.Collections.Generic;

namespace TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels
{
    public partial class OpenProjectWorkflowViewModel : BaseDomainViewModel
    {
        // Domain mediator (properly typed)
        private new readonly IOpenProjectMediator _mediator;
        private readonly IPersistenceService _persistenceService;
        private const string RECENT_PROJECTS_KEY = "RecentProjects";
        private const int MAX_RECENT_PROJECTS = 10;
        
        [ObservableProperty]
        private string selectedProjectPath = "";
        
        [ObservableProperty]
        private bool isProjectSelected = false;
        
        partial void OnIsProjectSelectedChanged(bool value)
        {
            OnPropertyChanged(nameof(SelectButtonText));
            OnPropertyChanged(nameof(OpenButtonText));
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
        private int requirementCount = 0;
        
        [ObservableProperty]
        private DateTime? lastModified;

        [ObservableProperty]
        private ObservableCollection<RecentProject> recentProjects = new();

        [ObservableProperty]
        private bool hasRecentProjects = false;

        // Commands
        public ICommand SelectProjectFileCommand { get; }
        public ICommand OpenSelectedProjectCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand OpenFileDirectlyCommand { get; }
        public ICommand OpenRecentProjectCommand { get; }

        public OpenProjectWorkflowViewModel(IOpenProjectMediator mediator, IPersistenceService persistenceService, ILogger<OpenProjectWorkflowViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
            
            // Initialize commands
            SelectProjectFileCommand = new AsyncRelayCommand(SelectProjectFileAsync);
            OpenSelectedProjectCommand = new AsyncRelayCommand(OpenSelectedProjectAsync, CanOpenSelectedProject);
            ClearSelectionCommand = new RelayCommand(ClearSelection);
            OpenFileDirectlyCommand = new AsyncRelayCommand(OpenFileDirectlyAsync);
            OpenRecentProjectCommand = new AsyncRelayCommand<RecentProject>(OpenRecentProjectAsync!);
            
            // Load recent projects
            LoadRecentProjects();
            
            // Subscribe to domain events
            _mediator.Subscribe<OpenProjectEvents.ProjectFileSelected>(OnProjectFileSelected);
            _mediator.Subscribe<OpenProjectEvents.ProjectOpened>(OnProjectOpened);
            _mediator.Subscribe<OpenProjectEvents.ProjectOpenFailed>(OnProjectOpenFailed);
            _mediator.Subscribe<OpenProjectEvents.WorkspaceLoaded>(OnWorkspaceLoaded);
        }

        private async Task SelectProjectFileAsync()
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
                        ProjectStatus = $"Selected: {ProjectName}";
                        
                        // Get file info
                        if (File.Exists(selectedPath))
                        {
                            var fileInfo = new FileInfo(selectedPath);
                            LastModified = fileInfo.LastWriteTime;
                        }
                        
                        _logger.LogInformation($"Project file selected: {selectedPath}");
                        
                        // Update command states
                        ((AsyncRelayCommand)OpenSelectedProjectCommand).NotifyCanExecuteChanged();
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
            return IsProjectSelected && !IsLoadingProject && !string.IsNullOrWhiteSpace(SelectedProjectPath);
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

        private async Task OpenRecentProjectAsync(RecentProject recentProject)
        {
            if (recentProject == null) return;

            try
            {
                IsLoadingProject = true;
                ProjectStatus = $"Opening {recentProject.ProjectName}...";
                
                if (!recentProject.FileExists)
                {
                    ProjectStatus = "File not found - removing from recent projects";
                    RemoveFromRecentProjects(recentProject.FilePath);
                    return;
                }

                await OpenProjectFile(recentProject.FilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error opening recent project: {recentProject.FilePath}");
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
            }

            // Add to recent projects
            AddToRecentProjects(filePath, ProjectName);
            
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

        private void LoadRecentProjects()
        {
            try
            {
                var recent = _persistenceService.Load<List<RecentProject>>(RECENT_PROJECTS_KEY) ?? new List<RecentProject>();
                
                // Filter out non-existent files and sort by last opened
                var validRecent = recent
                    .Where(rp => !string.IsNullOrWhiteSpace(rp.FilePath))
                    .OrderByDescending(rp => rp.LastOpened)
                    .Take(MAX_RECENT_PROJECTS)
                    .ToList();
                
                RecentProjects.Clear();
                foreach (var project in validRecent)
                {
                    RecentProjects.Add(project);
                }
                
                HasRecentProjects = RecentProjects.Count > 0;
                
                _logger.LogInformation($"Loaded {RecentProjects.Count} recent projects");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading recent projects");
                RecentProjects.Clear();
                HasRecentProjects = false;
            }
        }

        private void AddToRecentProjects(string filePath, string projectName)
        {
            try
            {
                var recent = _persistenceService.Load<List<RecentProject>>(RECENT_PROJECTS_KEY) ?? new List<RecentProject>();
                
                // Remove existing entry if it exists
                recent.RemoveAll(rp => string.Equals(rp.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                
                // Add new entry at the beginning
                recent.Insert(0, new RecentProject
                {
                    FilePath = filePath,
                    ProjectName = projectName,
                    LastOpened = DateTime.Now
                });
                
                // Keep only the most recent projects
                if (recent.Count > MAX_RECENT_PROJECTS)
                {
                    recent = recent.Take(MAX_RECENT_PROJECTS).ToList();
                }
                
                _persistenceService.Save(RECENT_PROJECTS_KEY, recent);
                
                // Update UI
                LoadRecentProjects();
                
                _logger.LogInformation($"Added {projectName} to recent projects");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding project to recent projects: {filePath}");
            }
        }

        private void RemoveFromRecentProjects(string filePath)
        {
            try
            {
                var recent = _persistenceService.Load<List<RecentProject>>(RECENT_PROJECTS_KEY) ?? new List<RecentProject>();
                recent.RemoveAll(rp => string.Equals(rp.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                _persistenceService.Save(RECENT_PROJECTS_KEY, recent);
                
                // Update UI
                LoadRecentProjects();
                
                _logger.LogInformation($"Removed {filePath} from recent projects");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing project from recent projects: {filePath}");
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