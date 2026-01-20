using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.NewProject.Events;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Events;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Navigation service that manages application title with breadcrumb navigation
    /// Handles project events internally to maintain proper title updates
    /// </summary>
    public interface INavigationService
    {
        /// <summary>
        /// Current application title with full breadcrumb trail
        /// </summary>
        string Title { get; }
        
        /// <summary>
        /// Event fired when title changes
        /// </summary>
        event EventHandler<string>? TitleChanged;
        
        /// <summary>
        /// Navigate to a section and update breadcrumb trail
        /// </summary>
        void NavigateToSection(string section, string? context = null);
        
        /// <summary>
        /// Add a context level to current section (e.g., project name, requirement name)
        /// </summary>
        void AddContext(string contextName);
        
        /// <summary>
        /// Clear navigation and reset to base application title
        /// </summary>
        void ClearNavigation();
        
        /// <summary>
        /// Update the application title based on current section/context (legacy method)
        /// </summary>
        void UpdateTitle(string section, string? context = null);
        
        /// <summary>
        /// Initialize service with workspace coordinator for project event handling
        /// </summary>
        void Initialize(IViewAreaCoordinator? coordinator = null);
    }

    public class NavigationService : INavigationService
    {
        private readonly ILogger<NavigationService>? _logger;
        private string _title = "Systems ATE APP";
        private string _currentSection = "TestCase";
        private string? _currentProject = null;
        private IViewAreaCoordinator? _coordinator;
        
        public NavigationService(ILogger<NavigationService>? logger = null)
        {
            _logger = logger;
            _logger?.LogInformation("NavigationService created with initial title: '{Title}'", _title);
            
            // Fire initial title change event to ensure UI bindings are set
            Task.Run(() => 
            {
                Task.Delay(100);
                TitleChanged?.Invoke(this, _title);
                _logger?.LogInformation("NavigationService initial TitleChanged event fired with '{Title}'", _title);
            });
        }
        
        public string Title => _title;
        
        public event EventHandler<string>? TitleChanged;
        
        public void Initialize(IViewAreaCoordinator? coordinator = null)
        {
            _coordinator = coordinator;
            
            // Set initial startup configuration through SectionChangeRequested 
            // This will trigger both navigation AND view configuration properly
            if (_coordinator?.NavigationMediator is INavigationMediator navMediator)
            {
                navMediator.Publish(new NavigationEvents.SectionChangeRequested("startup", null));
            }
            
            // Subscribe to project events to automatically update titles
            if (_coordinator?.WorkspaceManagement != null)
            {
                _coordinator.WorkspaceManagement.Subscribe<NewProjectEvents.ProjectOpened>(e => {
                    _logger?.LogInformation("NavigationService: NewProject opened: {ProjectName}", e.WorkspaceName);
                    _currentProject = e.WorkspaceName;
                    UpdateCurrentTitle();
                });
                
                _coordinator.WorkspaceManagement.Subscribe<OpenProjectEvents.ProjectOpened>(e => {
                    _logger?.LogInformation("NavigationService: OpenProject opened: {ProjectName}", e.WorkspaceName);
                    _currentProject = e.WorkspaceName;
                    UpdateCurrentTitle();
                });
                
                _coordinator.WorkspaceManagement.Subscribe<NewProjectEvents.ProjectClosed>(e => {
                    _logger?.LogInformation("NavigationService: Project closed");
                    _currentProject = null;
                    UpdateCurrentTitle();
                });
            }
            
            // Subscribe to section navigation events to update title when user navigates
            if (_coordinator?.NavigationMediator != null)
            {
                _coordinator.NavigationMediator.Subscribe<TestCaseEditorApp.MVVM.Utils.NavigationEvents.SectionChanged>(e => {
                    _logger?.LogInformation("NavigationService: Section changed to: {Section}", e.NewSection);
                    _currentSection = e.NewSection ?? "TestCase";
                    
                    // Always ensure we have the current project name when sections change
                    if (string.IsNullOrWhiteSpace(_currentProject))
                    {
                        var workspaceInfo = _coordinator.WorkspaceManagement?.GetCurrentWorkspaceInfo();
                        if (workspaceInfo != null && !string.IsNullOrWhiteSpace(workspaceInfo.Name))
                        {
                            _currentProject = workspaceInfo.Name;
                            _logger?.LogDebug("NavigationService: Restored project name on section change: {ProjectName}", _currentProject);
                        }
                    }
                    
                    UpdateCurrentTitle();
                });
            }
        }
        
        private void UpdateCurrentTitle()
        {
            var previousTitle = _title;
            
            // Map section codes to proper display names
            string sectionName = _currentSection.ToLower() switch
            {
                "project" => "Test Case Generator",
                "testcase" => "Test Case Generator", 
                "requirements" => "Test Case Generator",
                "newproject" => "Test Case Generator",
                "testflow" => "Test Case Generator",
                "import" => "Test Case Generator",
                "startup" => "Systems ATE APP",
                _ => "Systems ATE APP"  // Default to startup instead of Test Case Generator
            };
            
            // Simple format: Section - ProjectName (if available)
            if (!string.IsNullOrEmpty(_currentProject))
            {
                _title = $"{sectionName} - {_currentProject}";
            }
            else
            {
                _title = sectionName;
            }
            
            _logger?.LogInformation("NavigationService: Title updated '{PreviousTitle}' → '{NewTitle}'", previousTitle, _title);
            TitleChanged?.Invoke(this, _title);
        }
        
        public void NavigateToSection(string section, string? context = null)
        {
            _currentSection = section;
            if (context != null)
            {
                _currentProject = context;
            }
            UpdateCurrentTitle();
        }
        
        public void AddContext(string contextName)
        {
            // For simplified navigation, just treat this like a section change
            var previousTitle = _title;
            _title = $"{_title} - {contextName}";
            _logger?.LogInformation("Context added: '{PreviousTitle}' → '{NewTitle}'", previousTitle, _title);
            TitleChanged?.Invoke(this, _title);
        }
        
        public void ClearNavigation()
        {
            var previousTitle = _title;
            _title = "Systems App";
            _currentSection = "TestCase";
            _currentProject = null;
            _logger?.LogInformation("Navigation cleared: '{PreviousTitle}' → '{NewTitle}'", previousTitle, _title);
            TitleChanged?.Invoke(this, _title);
        }
        
        // Legacy method for backward compatibility
        public void UpdateTitle(string section, string? context = null)
        {
            NavigateToSection(section, context);
        }
    }
}
