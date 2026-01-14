using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.Project.Events;

namespace TestCaseEditorApp.MVVM.Domains.Project.Mediators
{
    /// <summary>
    /// Interface for Project domain mediator following AI Guide patterns
    /// </summary>
    public interface IProjectMediator
    {
        // ===== CORE MEDIATOR FUNCTIONALITY =====
        
        /// <summary>
        /// Subscribe to Project domain events
        /// </summary>
        void Subscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Unsubscribe from Project domain events
        /// </summary>
        void Unsubscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Publish events within Project domain
        /// </summary>
        void PublishEvent<T>(T eventData) where T : class;
        
        /// <summary>
        /// Mark mediator as registered and ready
        /// </summary>
        void MarkAsRegistered();
        
        // ===== PROJECT DOMAIN SPECIFIC METHODS =====
        /// <summary>
        /// Open a project from file path
        /// </summary>
        Task<bool> OpenProjectAsync(string projectPath);
        
        /// <summary>
        /// Save the current project
        /// </summary>
        Task<bool> SaveProjectAsync();
        
        /// <summary>
        /// Save project to a specific path
        /// </summary>
        Task<bool> SaveProjectAsAsync(string projectPath);
        
        /// <summary>
        /// Close the current project
        /// </summary>
        Task<bool> CloseProjectAsync();
        
        /// <summary>
        /// Create a new project
        /// </summary>
        Task<bool> CreateNewProjectAsync();
        
        /// <summary>
        /// Get current project status information
        /// </summary>
        ProjectStatus GetProjectStatus();
        
        /// <summary>
        /// Update project settings
        /// </summary>
        void UpdateProjectSetting(string settingName, object? value);
    }
    
    /// <summary>
    /// Project status information
    /// </summary>
    public class ProjectStatus
    {
        public string? ProjectName { get; set; }
        public string? ProjectPath { get; set; }
        public bool HasProject { get; set; }
        public bool HasUnsavedChanges { get; set; }
        public bool IsLLMConnected { get; set; }
        public string LLMStatusMessage { get; set; } = string.Empty;
    }
}