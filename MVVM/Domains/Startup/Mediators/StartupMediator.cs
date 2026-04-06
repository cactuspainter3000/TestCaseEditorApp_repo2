using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.Startup.Events;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.Startup.Mediators
{
    /// <summary>
    /// Mediator for the Startup domain, managing initial application state and transitions
    /// </summary>
    public interface IStartupMediator
    {
        /// <summary>
        /// Initiate startup sequence for all workspace areas
        /// </summary>
        void InitiateStartup();

        /// <summary>
        /// Update startup progress
        /// </summary>
        void UpdateStartupStatus(string status, double progressPercent = 0);

        /// <summary>
        /// Complete startup and prepare for domain transition
        /// </summary>
        void CompleteStartup();

        /// <summary>
        /// Request transition to a specific domain
        /// </summary>
        Task RequestDomainTransition(string targetDomain, object? transitionData = null);
        
        /// <summary>
        /// Check if mediator is properly registered in DI container
        /// </summary>
        bool IsRegistered { get; }
        
        /// <summary>
        /// Mark mediator as registered for fail-fast validation
        /// </summary>
        void MarkAsRegistered();
    }

    public class StartupMediator : BaseDomainMediator<StartupEvents>, IStartupMediator
    {
        private DateTime _startupInitiated;

        public StartupMediator(
            ILogger<StartupMediator> logger,
            IDomainUICoordinator uiCoordinator,
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "Startup", performanceMonitor, eventReplay)
        {
        }

        public void InitiateStartup()
        {
            _startupInitiated = DateTime.Now;
            
            PublishEvent(new StartupEvents.StartupInitiated
            {
                Timestamp = _startupInitiated,
                InitiatedBy = "Application"
            });

            UpdateStartupStatus("Initializing application...", 0);
        }

        public void UpdateStartupStatus(string status, double progressPercent = 0)
        {
            PublishEvent(new StartupEvents.StartupStatusChanged
            {
                Status = status,
                ProgressPercent = progressPercent,
                IsVisible = true
            });
        }

        public void CompleteStartup()
        {
            var startupDuration = DateTime.Now - _startupInitiated;
            
            PublishEvent(new StartupEvents.StartupCompleted
            {
                Timestamp = DateTime.Now,
                StartupDuration = startupDuration
            });
        }

        public async Task RequestDomainTransition(string targetDomain, object? transitionData = null)
        {
            PublishEvent(new StartupEvents.RequestDomainTransition
            {
                TargetDomain = targetDomain,
                TransitionData = transitionData
            });

            // Give UI time to process the transition request
            await Task.Delay(100);
        }

        // Abstract method implementations for startup domain navigation
        public override void NavigateToInitialStep()
        {
            // For startup, this would return to the main startup view
            UpdateStartupStatus("Returning to startup...", 0);
        }

        public override void NavigateToFinalStep()
        {
            // For startup, this completes startup and transitions to main app
            CompleteStartup();
        }

        public override bool CanNavigateBack()
        {
            // Startup typically doesn't have navigation history
            return false;
        }

        public override bool CanNavigateForward()
        {
            // Startup can always proceed forward to complete
            return true;
        }
    }
}