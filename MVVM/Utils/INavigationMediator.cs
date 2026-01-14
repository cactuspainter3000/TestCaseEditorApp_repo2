using System;

namespace TestCaseEditorApp.MVVM.Utils
{
    /// <summary>
    /// Navigation mediator to decouple navigation requests from direct ViewModel management.
    /// Eliminates circular dependencies by using message-based communication.
    /// </summary>
    public interface INavigationMediator
    {
        /// <summary>
        /// Navigate to a specific section with optional context
        /// </summary>
        void NavigateToSection(string sectionName, object? context = null);
        
        /// <summary>
        /// Navigate to a specific step within the current section
        /// </summary>
        void NavigateToStep(string stepId, object? context = null);
        
        /// <summary>
        /// Set the active header ViewModel
        /// </summary>
        void SetActiveHeader(object? headerViewModel);
        
        /// <summary>
        /// Set the main content ViewModel
        /// </summary>
        void SetMainContent(object? contentViewModel);
        
        /// <summary>
        /// Clear all navigation state and return to initial state
        /// </summary>
        void ClearNavigationState();
        
        /// <summary>
        /// Subscribe to navigation events
        /// </summary>
        void Subscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Unsubscribe from navigation events
        /// </summary>
        void Unsubscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Publish a navigation event
        /// </summary>
        void Publish<T>(T navigationEvent) where T : class;
        
        // Current state access
        string? CurrentSection { get; }
        object? CurrentHeader { get; }
        object? CurrentContent { get; }
    }
}