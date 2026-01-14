using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.Project.Events;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.Project.Mediators
{
    /// <summary>
    /// Project domain mediator implementation following AI Guide patterns
    /// Handles all Project domain coordination and cross-domain communication
    /// </summary>
    public class ProjectMediator : IProjectMediator
    {
        private readonly ILogger<ProjectMediator> _logger;
        private readonly IPersistenceService? _persistenceService;
        private readonly AnythingLLMService? _llmService;
        private readonly Dictionary<Type, List<Delegate>> _subscriptions;
        private bool _isRegistered = false;
        private ProjectStatus _currentStatus;
        
        public ProjectMediator(
            ILogger<ProjectMediator> logger,
            IPersistenceService? persistenceService = null,
            AnythingLLMService? llmService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _persistenceService = persistenceService;
            _llmService = llmService;
            _subscriptions = new Dictionary<Type, List<Delegate>>();
            _currentStatus = new ProjectStatus();
            
            _logger.LogDebug("[ProjectMediator] Initialized");
        }
        
        // ===== CORE MEDIATOR FUNCTIONALITY =====
        
        public void Subscribe<T>(Action<T> handler) where T : class
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            
            var eventType = typeof(T);
            if (!_subscriptions.ContainsKey(eventType))
            {
                _subscriptions[eventType] = new List<Delegate>();
            }
            
            _subscriptions[eventType].Add(handler);
            _logger.LogDebug("[ProjectMediator] Subscribed to {EventType}", eventType.Name);
        }
        
        public void Unsubscribe<T>(Action<T> handler) where T : class
        {
            if (handler == null) return;
            
            var eventType = typeof(T);
            if (_subscriptions.ContainsKey(eventType))
            {
                _subscriptions[eventType].Remove(handler);
                _logger.LogDebug("[ProjectMediator] Unsubscribed from {EventType}", eventType.Name);
            }
        }
        
        public void PublishEvent<T>(T eventData) where T : class
        {
            if (eventData == null) return;
            
            var eventType = typeof(T);
            _logger.LogDebug("[ProjectMediator] Publishing {EventType}", eventType.Name);
            
            if (_subscriptions.ContainsKey(eventType))
            {
                foreach (var subscription in _subscriptions[eventType])
                {
                    try
                    {
                        ((Action<T>)subscription)(eventData);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[ProjectMediator] Error handling {EventType}: {Error}", 
                            eventType.Name, ex.Message);
                    }
                }
            }
        }
        
        public void MarkAsRegistered()
        {
            _isRegistered = true;
            _logger.LogDebug("[ProjectMediator] Marked as registered");
        }
        
        // ===== PROJECT DOMAIN SPECIFIC METHODS =====
        
        public async Task<bool> OpenProjectAsync(string projectPath)
        {
            try
            {
                _logger.LogInformation("[ProjectMediator] Opening project: {ProjectPath}", projectPath);
                
                if (_persistenceService != null)
                {
                    // Implementation would load workspace from persistence service
                    // For now, create a mock workspace
                    var workspace = new Workspace 
                    { 
                        Name = System.IO.Path.GetFileNameWithoutExtension(projectPath)
                    };
                    
                    _currentStatus.ProjectName = workspace.Name;
                    _currentStatus.ProjectPath = projectPath;
                    _currentStatus.HasProject = true;
                    _currentStatus.HasUnsavedChanges = false;
                    
                    // Publish project opened event
                    PublishEvent(new ProjectEvents.ProjectOpened(workspace.Name, projectPath, workspace));
                    
                    return true;
                }
                
                _logger.LogWarning("[ProjectMediator] Failed to load project from {ProjectPath}", projectPath);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProjectMediator] Error opening project {ProjectPath}: {Error}", 
                    projectPath, ex.Message);
                return false;
            }
        }
        
        public async Task<bool> SaveProjectAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentStatus.ProjectPath))
                {
                    _logger.LogWarning("[ProjectMediator] Cannot save project - no project path");
                    return false;
                }
                
                return await SaveProjectAsAsync(_currentStatus.ProjectPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProjectMediator] Error saving project: {Error}", ex.Message);
                return false;
            }
        }
        
        public async Task<bool> SaveProjectAsAsync(string projectPath)
        {
            try
            {
                _logger.LogInformation("[ProjectMediator] Saving project to: {ProjectPath}", projectPath);
                
                if (_persistenceService != null && _currentStatus.HasProject)
                {
                    // Implementation would save project data
                    // For now, just update status and publish event
                    _currentStatus.ProjectPath = projectPath;
                    _currentStatus.HasUnsavedChanges = false;
                    
                    PublishEvent(new ProjectEvents.ProjectSaved(
                        _currentStatus.ProjectName ?? "Unknown", projectPath, true));
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProjectMediator] Error saving project to {ProjectPath}: {Error}", 
                    projectPath, ex.Message);
                    
                PublishEvent(new ProjectEvents.ProjectSaved(
                    _currentStatus.ProjectName ?? "Unknown", projectPath, false));
                return false;
            }
        }
        
        public async Task<bool> CloseProjectAsync()
        {
            try
            {
                _logger.LogInformation("[ProjectMediator] Closing current project");
                
                var projectName = _currentStatus.ProjectName;
                var hasUnsavedChanges = _currentStatus.HasUnsavedChanges;
                
                // Reset status
                _currentStatus = new ProjectStatus();
                
                // Publish project closed event
                PublishEvent(new ProjectEvents.ProjectClosed(projectName, hasUnsavedChanges));
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProjectMediator] Error closing project: {Error}", ex.Message);
                return false;
            }
        }
        
        public async Task<bool> CreateNewProjectAsync()
        {
            try
            {
                _logger.LogInformation("[ProjectMediator] Creating new project");
                
                // Implementation would create new project
                // For now, just reset status
                _currentStatus = new ProjectStatus
                {
                    ProjectName = "New Project",
                    HasProject = true,
                    HasUnsavedChanges = true
                };
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProjectMediator] Error creating new project: {Error}", ex.Message);
                return false;
            }
        }
        
        public ProjectStatus GetProjectStatus()
        {
            // Update LLM status if service is available
            if (_llmService != null)
            {
                // Implementation would check actual LLM service status
                // For now, just provide a default status
                _currentStatus.IsLLMConnected = false;
                _currentStatus.LLMStatusMessage = "AnythingLLM Status Unknown";
            }
            
            return _currentStatus;
        }
        
        public void UpdateProjectSetting(string settingName, object? value)
        {
            try
            {
                _logger.LogDebug("[ProjectMediator] Updating setting {SettingName} to {Value}", 
                    settingName, value);
                
                // Implementation would update project settings
                // For now, just publish the event
                PublishEvent(new ProjectEvents.ProjectSettingsChanged(settingName, null, value));
                
                _currentStatus.HasUnsavedChanges = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProjectMediator] Error updating setting {SettingName}: {Error}", 
                    settingName, ex.Message);
            }
        }
    }
}