using System;
using System.Collections.ObjectModel;
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
        private DateTime? lastModified;

        [ObservableProperty]
        private ObservableCollection<RecentProject> recentProjects = new();

        [ObservableProperty]
        private bool hasRecentProjects = false;

        [ObservableProperty]
        private bool isMainOpenButtonActive = false;

        [ObservableProperty]
        private string? activeRecentProjectPath;

        partial void OnActiveRecentProjectPathChanged(string? value)
        {
            // Reset main button when recent project is selected
            if (!string.IsNullOrEmpty(value))
            {
                IsMainOpenButtonActive = false;
            }
        }

        partial void OnIsMainOpenButtonActiveChanged(bool value)
        {
            // Reset recent project selection when main button is selected
            // DISABLED DURING DEBUGGING: if (value)
            // {
            //     ActiveRecentProjectPath = null;
            // }
            _logger.LogInformation($"*** IsMainOpenButtonActive changed to: {value} - STACK TRACE: {Environment.StackTrace}");
        }

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
            OpenRecentProjectCommand = new RelayCommand<RecentProject>(OpenRecentProjectSync!);
            
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
                        IsMainOpenButtonActive = true; // Set main button as active
                        SetActiveRecentProject(null); // Clear any active recent projects
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

        private void OpenRecentProjectSync(RecentProject recentProject)
        {
            if (recentProject == null) return;

            _logger.LogInformation($"Recent project button clicked: {recentProject.ProjectName}");
            
            // Set button colors immediately
            SetActiveRecentProject(recentProject);
            
            // Update timestamp silently (no UI collection rebuild)
            UpdateRecentProjectTimestampSilently(recentProject.FilePath, recentProject.ProjectName);
            
            // Add slight delay before opening to see if it prevents flash
            _ = Task.Run(async () => 
            {
                await Task.Delay(100); // Small delay
                try
                {
                    // DON'T use mediator for recent projects - it affects main UI
                    // Just log that project was selected
                    _logger.LogInformation($"Recent project selected: {recentProject.FilePath}");
                    
                    // If you need actual project loading, implement separate mechanism
                    // that doesn't broadcast to other domains or affect main UI
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing recent project selection");
                }
            });
        }

        private async Task OpenRecentProjectAsync(RecentProject recentProject)
        {
            if (recentProject == null) return;

            try
            {
                _logger.LogInformation($"*** OpenRecentProjectAsync called for: {recentProject.ProjectName}");
                
                IsLoadingProject = true;
                ProjectStatus = $"Opening {recentProject.ProjectName}...";
                
                if (!recentProject.FileExists)
                {
                    ProjectStatus = "File not found - removing from recent projects";
                    RemoveFromRecentProjects(recentProject.FilePath);
                    return;
                }

                _logger.LogInformation($"*** About to open project file: {recentProject.ProjectName}");
                
                // Open recent project - this will actually load the project WITHOUT updating main UI
                await OpenRecentProjectFileInternal(recentProject.FilePath, recentProject.ProjectName);
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

        private async Task OpenRecentProjectFileDirectly(string filePath)
        {
            // DO NOT update SelectedProjectPath or IsProjectSelected
            // Keep the file selection UI unchanged when opening recent projects
            
            // Set internal project name for status/logging purposes only
            var projectName = Path.GetFileNameWithoutExtension(filePath);
            if (projectName.EndsWith(".tcex", StringComparison.OrdinalIgnoreCase))
            {
                projectName = Path.GetFileNameWithoutExtension(projectName);
            }
            
            // Get file info for internal tracking
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                // Don't update LastModified property as it's UI-bound
            }

            // Add to recent projects (this is still needed)
            AddToRecentProjects(filePath, projectName);
            
            // Open through mediator (this handles the actual project loading)
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

        private async Task OpenRecentProjectFileInternal(string filePath, string projectName)
        {
            try
            {
                // DO NOT update main UI state (SelectedProjectPath, IsProjectSelected, ProjectStatus, etc.)
                // Recent project opens should not affect the Project File section at all
                
                // Update timestamp in-place without UI collection rebuild
                UpdateRecentProjectTimestampSilently(filePath, projectName);
                
                // DON'T use mediator for recent projects - it causes UI flash due to domain broadcasts
                // Just log the action - the project state is maintained elsewhere
                _logger.LogInformation($"Recent project selected: {filePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error opening recent project file: {filePath}");
            }
        }

        private void UpdateRecentProjectTimestampSilently(string filePath, string projectName)
        {
            try
            {
                var now = DateTime.Now;
                
                // Update the UI collection item directly
                var existingProject = RecentProjects.FirstOrDefault(rp => 
                    rp.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                
                if (existingProject != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Remove and re-add at same position to trigger UI refresh
                        var index = RecentProjects.IndexOf(existingProject);
                        RecentProjects.RemoveAt(index);
                        existingProject.LastOpened = now;
                        RecentProjects.Insert(index, existingProject); // Put back at same position
                    });
                }
                
                // Update persistence storage
                var recent = _persistenceService.Load<List<RecentProject>>(RECENT_PROJECTS_KEY) ?? new List<RecentProject>();
                recent.RemoveAll(rp => string.Equals(rp.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                
                var newProject = new RecentProject
                {
                    FilePath = filePath,
                    ProjectName = projectName,
                    LastOpened = now
                };
                
                PopulateProjectMetadata(newProject);
                recent.Insert(0, newProject);
                
                if (recent.Count > MAX_RECENT_PROJECTS)
                {
                    recent = recent.Take(MAX_RECENT_PROJECTS).ToList();
                }
                
                _persistenceService.Save(RECENT_PROJECTS_KEY, recent);
                
                _logger.LogInformation($"Silently updated timestamp for {projectName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating recent project timestamp silently: {filePath}");
            }
        }

        private void UpdateRecentProjectTimestamp(string filePath, string projectName)
        {
            try
            {
                // Update the existing item in the UI collection in-place
                var existingProject = RecentProjects.FirstOrDefault(rp => 
                    rp.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                
                if (existingProject != null)
                {
                    // Just update the timestamp on the existing UI item
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        existingProject.LastOpened = DateTime.Now;
                    });
                }
                
                // Update persistence storage
                var recent = _persistenceService.Load<List<RecentProject>>(RECENT_PROJECTS_KEY) ?? new List<RecentProject>();
                recent.RemoveAll(rp => string.Equals(rp.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                
                var newProject = new RecentProject
                {
                    FilePath = filePath,
                    ProjectName = projectName,
                    LastOpened = DateTime.Now
                };
                
                PopulateProjectMetadata(newProject);
                recent.Insert(0, newProject);
                
                if (recent.Count > MAX_RECENT_PROJECTS)
                {
                    recent = recent.Take(MAX_RECENT_PROJECTS).ToList();
                }
                
                _persistenceService.Save(RECENT_PROJECTS_KEY, recent);
                
                _logger.LogInformation($"Updated timestamp for {projectName} without rebuilding UI collection");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating recent project timestamp: {filePath}");
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

        private void SetActiveRecentProject(RecentProject? activeProject)
        {
            _logger.LogInformation($"*** SetActiveRecentProject called with: {activeProject?.ProjectName ?? "null"}");
            
            // Set all projects to inactive first
            foreach (var project in RecentProjects)
            {
                project.IsActiveProject = false;
            }
            
            // Set the selected project to active
            if (activeProject != null)
            {
                _logger.LogInformation($"*** Setting {activeProject.ProjectName} to ACTIVE");
                activeProject.IsActiveProject = true;
                ActiveRecentProjectPath = activeProject.FilePath;
                // DON'T modify IsMainOpenButtonActive - keep main UI completely separate
            }
            else
            {
                _logger.LogInformation($"*** Clearing active project path");
                ActiveRecentProjectPath = null;
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
                
                // Update UI on dispatcher thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    RecentProjects.Clear();
                    foreach (var project in validRecent)
                    {
                        // Populate metadata for each project
                        PopulateProjectMetadata(project);
                        RecentProjects.Add(project);
                    }
                    
                    HasRecentProjects = RecentProjects.Count > 0;
                });
                
                _logger.LogInformation($"Loaded {validRecent.Count} recent projects");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading recent projects");
                RecentProjects.Clear();
                HasRecentProjects = false;
            }
        }

        private void PopulateProjectMetadata(RecentProject project)
        {
            try
            {
                if (!System.IO.File.Exists(project.FilePath))
                {
                    project.RequirementsCount = 0;
                    project.AnalyzedCount = 0;
                    project.TestCasesGeneratedCount = 0;
                    return;
                }

                var projectJson = System.IO.File.ReadAllText(project.FilePath);
                var projectData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(projectJson);

                // Count total requirements
                var requirementsCount = 0;
                var analyzedCount = 0;
                var testCasesCount = 0;

                if (projectData.TryGetProperty("requirements", out var requirementsElement) && requirementsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var requirement in requirementsElement.EnumerateArray())
                    {
                        requirementsCount++;

                        // Check if requirement has been analyzed (has assumptions or questions)
                        var hasAnalysis = false;
                        if (requirement.TryGetProperty("assumptions", out var assumptions) && 
                            assumptions.ValueKind == System.Text.Json.JsonValueKind.Array && 
                            assumptions.GetArrayLength() > 0)
                        {
                            hasAnalysis = true;
                        }
                        if (requirement.TryGetProperty("questions", out var questions) && 
                            questions.ValueKind == System.Text.Json.JsonValueKind.Array && 
                            questions.GetArrayLength() > 0)
                        {
                            hasAnalysis = true;
                        }

                        if (hasAnalysis) analyzedCount++;

                        // Check if requirement has test cases
                        if (requirement.TryGetProperty("testCases", out var testCases) && 
                            testCases.ValueKind == System.Text.Json.JsonValueKind.Array && 
                            testCases.GetArrayLength() > 0)
                        {
                            testCasesCount++;
                        }
                    }
                }

                project.RequirementsCount = requirementsCount;
                project.AnalyzedCount = analyzedCount;
                project.TestCasesGeneratedCount = testCasesCount;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading metadata for project: {FilePath}", project.FilePath);
                project.RequirementsCount = 0;
                project.AnalyzedCount = 0;
                project.TestCasesGeneratedCount = 0;
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
                var newProject = new RecentProject
                {
                    FilePath = filePath,
                    ProjectName = projectName,
                    LastOpened = DateTime.Now
                };
                
                // Populate metadata for the new project
                PopulateProjectMetadata(newProject);
                
                recent.Insert(0, newProject);
                
                // Keep only the most recent projects
                if (recent.Count > MAX_RECENT_PROJECTS)
                {
                    recent = recent.Take(MAX_RECENT_PROJECTS).ToList();
                }
                
                _persistenceService.Save(RECENT_PROJECTS_KEY, recent);
                
                // Update UI on dispatcher thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    LoadRecentProjects();
                });
                
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