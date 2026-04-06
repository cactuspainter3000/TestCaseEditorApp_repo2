using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.Project.Mediators;
using TestCaseEditorApp.MVVM.Domains.Project.Events;

namespace TestCaseEditorApp.MVVM.Domains.Project.ViewModels
{
    /// <summary>
    /// Main workspace ViewModel for Project domain following AI Guide patterns
    /// Handles project management functionality and UI data binding
    /// </summary>
    public partial class Project_MainViewModel : ObservableObject
    {
        private readonly IProjectMediator _mediator;
        private readonly ILogger<Project_MainViewModel> _logger;
        
        // ===== OBSERVABLE PROPERTIES =====
        
        [ObservableProperty]
        private string _projectName = "No Project Loaded";
        
        [ObservableProperty]
        private string _projectPath = string.Empty;
        
        [ObservableProperty]
        private bool _hasProject = false;
        
        [ObservableProperty]
        private bool _hasUnsavedChanges = false;
        
        [ObservableProperty]
        private bool _isAnythingLLMAvailable = false;
        
        [ObservableProperty]
        private bool _isAnythingLLMStarting = false;
        
        [ObservableProperty]
        private string _anythingLLMStatusMessage = "AnythingLLM Status Unknown";
        
        // ===== COMMANDS =====
        
        public ICommand OpenProjectCommand { get; }
        public ICommand SaveProjectCommand { get; }
        public ICommand SaveProjectAsCommand { get; }
        public ICommand CloseProjectCommand { get; }
        public ICommand NewProjectCommand { get; }
        
        // ===== CONSTRUCTOR =====
        
        public Project_MainViewModel(IProjectMediator mediator, ILogger<Project_MainViewModel> logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Initialize commands
            OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
            SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync, CanSaveProject);
            SaveProjectAsCommand = new AsyncRelayCommand(SaveProjectAsAsync, CanSaveProject);
            CloseProjectCommand = new AsyncRelayCommand(CloseProjectAsync, CanCloseProject);
            NewProjectCommand = new AsyncRelayCommand(NewProjectAsync);
            
            // Subscribe to domain events
            _mediator.Subscribe<ProjectEvents.ProjectOpened>(OnProjectOpened);
            _mediator.Subscribe<ProjectEvents.ProjectSaved>(OnProjectSaved);
            _mediator.Subscribe<ProjectEvents.ProjectClosed>(OnProjectClosed);
            _mediator.Subscribe<ProjectEvents.LLMConnectionStatusChanged>(OnLLMStatusChanged);
            
            _logger.LogDebug("[Project_MainViewModel] Initialized");
            
            // Load initial status
            UpdateProjectStatus();
        }
        
        // ===== COMMAND IMPLEMENTATIONS =====
        
        private async Task OpenProjectAsync()
        {
            try
            {
                _logger.LogDebug("[Project_MainViewModel] OpenProject command executed");
                
                // Implementation would show file picker dialog
                // For now, simulate opening a project
                var result = await _mediator.OpenProjectAsync(@"C:\Sample\Project.tcex");
                
                if (!result)
                {
                    _logger.LogWarning("[Project_MainViewModel] Failed to open project");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Project_MainViewModel] Error in OpenProjectAsync: {Error}", ex.Message);
            }
        }
        
        private async Task SaveProjectAsync()
        {
            try
            {
                _logger.LogDebug("[Project_MainViewModel] SaveProject command executed");
                
                var result = await _mediator.SaveProjectAsync();
                
                if (!result)
                {
                    _logger.LogWarning("[Project_MainViewModel] Failed to save project");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Project_MainViewModel] Error in SaveProjectAsync: {Error}", ex.Message);
            }
        }
        
        private async Task SaveProjectAsAsync()
        {
            try
            {
                _logger.LogDebug("[Project_MainViewModel] SaveProjectAs command executed");
                
                // Implementation would show save file dialog
                // For now, simulate save as
                var result = await _mediator.SaveProjectAsAsync(@"C:\Sample\NewProject.tcex");
                
                if (!result)
                {
                    _logger.LogWarning("[Project_MainViewModel] Failed to save project as");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Project_MainViewModel] Error in SaveProjectAsAsync: {Error}", ex.Message);
            }
        }
        
        private async Task CloseProjectAsync()
        {
            try
            {
                _logger.LogDebug("[Project_MainViewModel] CloseProject command executed");
                
                var result = await _mediator.CloseProjectAsync();
                
                if (!result)
                {
                    _logger.LogWarning("[Project_MainViewModel] Failed to close project");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Project_MainViewModel] Error in CloseProjectAsync: {Error}", ex.Message);
            }
        }
        
        private async Task NewProjectAsync()
        {
            try
            {
                _logger.LogDebug("[Project_MainViewModel] NewProject command executed");
                
                var result = await _mediator.CreateNewProjectAsync();
                
                if (!result)
                {
                    _logger.LogWarning("[Project_MainViewModel] Failed to create new project");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Project_MainViewModel] Error in NewProjectAsync: {Error}", ex.Message);
            }
        }
        
        // ===== COMMAND CAN EXECUTE =====
        
        private bool CanSaveProject()
        {
            return HasProject && HasUnsavedChanges;
        }
        
        private bool CanCloseProject()
        {
            return HasProject;
        }
        
        // ===== EVENT HANDLERS =====
        
        private void OnProjectOpened(ProjectEvents.ProjectOpened evt)
        {
            _logger.LogDebug("[Project_MainViewModel] Project opened: {ProjectName}", evt.ProjectName);
            UpdateProjectStatus();
        }
        
        private void OnProjectSaved(ProjectEvents.ProjectSaved evt)
        {
            _logger.LogDebug("[Project_MainViewModel] Project saved: {ProjectName}", evt.ProjectName);
            UpdateProjectStatus();
        }
        
        private void OnProjectClosed(ProjectEvents.ProjectClosed evt)
        {
            _logger.LogDebug("[Project_MainViewModel] Project closed: {ProjectName}", evt.ProjectName ?? "Unknown");
            UpdateProjectStatus();
        }
        
        private void OnLLMStatusChanged(ProjectEvents.LLMConnectionStatusChanged evt)
        {
            _logger.LogDebug("[Project_MainViewModel] LLM status changed: {Status}", evt.StatusMessage);
            
            IsAnythingLLMAvailable = evt.IsConnected;
            IsAnythingLLMStarting = false;
            AnythingLLMStatusMessage = evt.StatusMessage;
        }
        
        // ===== PRIVATE METHODS =====
        
        private void UpdateProjectStatus()
        {
            try
            {
                var status = _mediator.GetProjectStatus();
                
                ProjectName = status.ProjectName ?? "No Project Loaded";
                ProjectPath = status.ProjectPath ?? string.Empty;
                HasProject = status.HasProject;
                HasUnsavedChanges = status.HasUnsavedChanges;
                IsAnythingLLMAvailable = status.IsLLMConnected;
                AnythingLLMStatusMessage = status.LLMStatusMessage;
                
                // Update command can execute states
                ((AsyncRelayCommand)SaveProjectCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)SaveProjectAsCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)CloseProjectCommand).NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Project_MainViewModel] Error updating project status: {Error}", ex.Message);
            }
        }
    }
}