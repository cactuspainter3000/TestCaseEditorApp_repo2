using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators
{
    /// <summary>
    /// Interface for the Open Project domain mediator.
    /// Handles project opening operations: file selection, loading, validation.
    /// </summary>
    public interface IOpenProjectMediator
    {
        // ===== CORE MEDIATOR FUNCTIONALITY =====
        
        /// <summary>
        /// Subscribe to OpenProject domain events
        /// </summary>
        void Subscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Unsubscribe from OpenProject domain events
        /// </summary>
        void Unsubscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Broadcast events to all domains
        /// </summary>
        void BroadcastToAllDomains<T>(T eventData) where T : class;
        
        /// <summary>
        /// Mark mediator as registered and ready
        /// </summary>
        void MarkAsRegistered();
        
        // ===== OPEN PROJECT DOMAIN SPECIFIC METHODS =====
        
        /// <summary>
        /// Start open project workflow with file dialog
        /// </summary>
        Task OpenProjectAsync();
        
        /// <summary>
        /// Open specific project file
        /// </summary>
        Task<bool> OpenProjectFileAsync(string filePath);
        
        /// <summary>
        /// Get current workspace information
        /// </summary>
        Workspace? GetCurrentWorkspace();
        
        /// <summary>
        /// Validate project file before opening
        /// </summary>
        Task<bool> ValidateProjectFileAsync(string filePath);
    }
}