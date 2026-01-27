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
        private readonly RecentFilesService _recentFilesService;
        
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

        // Recent Projects
        public IReadOnlyList<RecentProjectInfo> RecentProjects => GetRecentProjectsInfo();

        // Commands
        public ICommand OpenProjectCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand OpenRecentProjectCommand { get; }

        public OpenProjectWorkflowViewModel(IOpenProjectMediator mediator, IPersistenceService persistenceService, RecentFilesService recentFilesService, ILogger<OpenProjectWorkflowViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
            _recentFilesService = recentFilesService ?? throw new ArgumentNullException(nameof(recentFilesService));
            
            // Initialize commands
            OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
            ClearSelectionCommand = new RelayCommand(ClearSelection);
            OpenRecentProjectCommand = new AsyncRelayCommand<string>(OpenRecentProjectAsync);
            
            // Subscribe to domain events
            _mediator.Subscribe<OpenProjectEvents.ProjectFileSelected>(OnProjectFileSelected);
            _mediator.Subscribe<OpenProjectEvents.ProjectOpened>(OnProjectOpened);
            _mediator.Subscribe<OpenProjectEvents.ProjectOpenFailed>(OnProjectOpenFailed);
            _mediator.Subscribe<OpenProjectEvents.WorkspaceLoaded>(OnWorkspaceLoaded);
        }

        private async Task OpenProjectAsync()
        {
            try
            {
                IsLoadingProject = true;
                ProjectStatus = "Selecting project file...";
                
                _logger.LogInformation("Opening project file dialog");
                
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
                        
                        // Get file info
                        if (File.Exists(selectedPath))
                        {
                            var fileInfo = new FileInfo(selectedPath);
                            LastModified = fileInfo.LastWriteTime;
                        }
                        
                        _logger.LogInformation($"Project file selected: {selectedPath}");
                        
                        // Open immediately
                        ProjectStatus = "Opening project...";
                        var success = await _mediator.OpenProjectFileAsync(SelectedProjectPath);
                        
                        if (success)
                        {
                            _recentFilesService.AddRecentFile(SelectedProjectPath);
                            OnPropertyChanged(nameof(RecentProjects));
                            ProjectStatus = "Project opened successfully";
                            _logger.LogInformation($"Project opened successfully: {SelectedProjectPath}");
                        }
                        else
                        {
                            ProjectStatus = "Failed to open project";
                            _logger.LogWarning($"Failed to open project: {SelectedProjectPath}");
                        }
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
                _logger.LogError(ex, "Error opening project file");
                ProjectStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoadingProject = false;
            }
        }

        private void ClearSelection()
        {
            SelectedProjectPath = "";
            IsProjectSelected = false;
            ProjectName = "";
            ProjectStatus = "No project selected";
            RequirementCount = 0;
            LastModified = null;
            
            _logger.LogInformation("Project selection cleared");
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

        /// <summary>
        /// Gets recent projects with metadata for display
        /// </summary>
        private IReadOnlyList<RecentProjectInfo> GetRecentProjectsInfo()
        {
            var recentFiles = _recentFilesService.GetRecentFiles();
            var result = new List<RecentProjectInfo>();
            
            foreach (var filePath in recentFiles)
            {
                try
                {
                    if (!File.Exists(filePath))
                        continue;
                        
                    var fileInfo = new FileInfo(filePath);
                    var projectName = Path.GetFileNameWithoutExtension(filePath);
                    
                    // Try to get requirement count from file
                    int reqCount = 0;
                    try
                    {
                        var jsonContent = File.ReadAllText(filePath);
                        using var document = JsonDocument.Parse(jsonContent);
                        if (document.RootElement.TryGetProperty("Requirements", out var reqsElement) 
                            && reqsElement.ValueKind == JsonValueKind.Array)
                        {
                            reqCount = reqsElement.GetArrayLength();
                        }
                    }
                    catch { /* Ignore parsing errors */ }
                    
                    result.Add(new RecentProjectInfo
                    {
                        FilePath = filePath,
                        ProjectName = projectName,
                        LastModified = fileInfo.LastWriteTime,
                        RequirementCount = reqCount
                    });
                }
                catch
                {
                    // Skip files that can't be read
                }
            }
            
            return result.AsReadOnly();
        }

        /// <summary>
        /// Opens a recent project by file path
        /// </summary>
        private async Task OpenRecentProjectAsync(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;
                
            _logger.LogInformation($"Opening recent project: {filePath}");
            
            try
            {
                IsLoadingProject = true;
                ProjectStatus = "Opening project...";
                
                // Set path and populate metadata
                SelectedProjectPath = filePath;
                ProjectName = Path.GetFileNameWithoutExtension(filePath);
                if (ProjectName.EndsWith(".tcex", StringComparison.OrdinalIgnoreCase))
                {
                    ProjectName = Path.GetFileNameWithoutExtension(ProjectName);
                }
                IsProjectSelected = true;
                PopulateMainProjectMetadata(filePath);
                
                // Open through mediator
                var success = await _mediator.OpenProjectFileAsync(filePath);
                
                if (success)
                {
                    _recentFilesService.AddRecentFile(filePath);
                    OnPropertyChanged(nameof(RecentProjects));
                    ProjectStatus = "Project opened successfully";
                    _logger.LogInformation($"Project opened successfully: {filePath}");
                }
                else
                {
                    ProjectStatus = "Failed to open project";
                    _logger.LogWarning($"Failed to open project: {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error opening project: {filePath}");
                ProjectStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoadingProject = false;
            }
        }
    }

    /// <summary>
    /// Model for displaying recent project information
    /// </summary>
    public class RecentProjectInfo
    {
        public string FilePath { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public DateTime LastModified { get; set; }
        public int RequirementCount { get; set; }
        
        public string DisplayText => $"{ProjectName} ({RequirementCount} reqs)";
        public string LastModifiedText => LastModified.ToString("MMM d, yyyy h:mm tt");
    }
}