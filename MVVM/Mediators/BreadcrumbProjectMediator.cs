using System;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Events;

namespace TestCaseEditorApp.MVVM.Mediators
{
    /// <summary>
    /// Mediator for project-level breadcrumb (project name)
    /// Handles the second level of navigation context.
    /// </summary>
    public class BreadcrumbProjectMediator
    {
        private readonly ILogger<BreadcrumbProjectMediator>? _logger;
        private string? _currentProject;

        public BreadcrumbProjectMediator(ILogger<BreadcrumbProjectMediator>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Event fired when project changes
        /// </summary>
        public event Action<BreadcrumbEvents.ProjectChanged>? ProjectChanged;

        /// <summary>
        /// Current project name
        /// </summary>
        public string? CurrentProject => _currentProject;

        /// <summary>
        /// Set the current project and notify subscribers
        /// </summary>
        public void SetProject(string? project)
        {
            var displayName = NormalizeProject(project);
            
            if (_currentProject != displayName)
            {
                _currentProject = displayName;
                var projectChangedEvent = new BreadcrumbEvents.ProjectChanged 
                { 
                    Project = displayName 
                };
                ProjectChanged?.Invoke(projectChangedEvent);
                
                _logger?.LogDebug("Breadcrumb project changed to: {Project}", displayName ?? "<none>");
            }
        }

        /// <summary>
        /// Clear the current project
        /// </summary>
        public void ClearProject()
        {
            SetProject(null);
        }

        /// <summary>
        /// Normalize project names for display
        /// </summary>
        private static string? NormalizeProject(string? project)
        {
            if (string.IsNullOrWhiteSpace(project))
                return null;

            // Extract filename from full path if needed
            var displayName = System.IO.Path.GetFileNameWithoutExtension(project.Trim());
            
            return string.IsNullOrEmpty(displayName) ? null : displayName;
        }
    }
}