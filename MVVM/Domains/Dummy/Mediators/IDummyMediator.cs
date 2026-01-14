using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.Dummy.Mediators
{
    /// <summary>
    /// Interface for the Dummy domain mediator - Reference implementation following AI Guide patterns
    /// Use this as a template for any domain's mediator interface
    /// </summary>
    public interface IDummyMediator
    {
        // ===== CORE MEDIATOR FUNCTIONALITY =====
        
        /// <summary>
        /// Subscribe to Dummy domain events
        /// </summary>
        void Subscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Unsubscribe from Dummy domain events
        /// </summary>
        void Unsubscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Publish events within Dummy domain
        /// </summary>
        void PublishEvent<T>(T eventData) where T : class;
        
        /// <summary>
        /// Mark mediator as registered and ready
        /// </summary>
        void MarkAsRegistered();
        
        // ===== DUMMY DOMAIN SPECIFIC METHODS =====
        
        /// <summary>
        /// Update workspace content for visual testing
        /// </summary>
        void ChangeWorkspace(string workspaceName, string content);
        
        /// <summary>
        /// Update status for testing notifications
        /// </summary>
        void UpdateStatus(string status, string message);
        
        /// <summary>
        /// Request transition to demonstrate cross-domain coordination
        /// </summary>
        Task RequestDomainTransition(string targetDomain, object? transitionData = null);
        
        /// <summary>
        /// Handle broadcast notifications from other domains - required for architectural compliance
        /// </summary>
        void HandleBroadcastNotification<T>(T notification) where T : class;
    }
}