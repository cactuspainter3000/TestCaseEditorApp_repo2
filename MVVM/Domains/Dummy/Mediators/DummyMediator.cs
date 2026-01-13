using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.Dummy.Events;
using TestCaseEditorApp.MVVM.Utils;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.Dummy.Mediators
{
    /// <summary>
    /// Dummy domain mediator - Reference implementation following AI Guide patterns
    /// Use this as a template for any domain's mediator implementation
    /// </summary>
    public class DummyMediator : BaseDomainMediator<DummyEvents>, IDummyMediator
    {
        public DummyMediator(
            ILogger<DummyMediator> logger,
            IDomainUICoordinator uiCoordinator,
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "Dummy Domain", performanceMonitor, eventReplay)
        {
            _logger.LogDebug("DummyMediator created for testing workspace coordination");
        }
        
        // ===== CORE MEDIATOR FUNCTIONALITY =====
        
        public override void Subscribe<T>(Action<T> handler) where T : class
        {
            base.Subscribe(handler);
        }
        
        public override void Unsubscribe<T>(Action<T> handler) where T : class
        {
            base.Unsubscribe(handler);
        }
        
        public new void PublishEvent<T>(T eventData) where T : class
        {
            base.PublishEvent(eventData);
        }
        
        public new void MarkAsRegistered()
        {
            base.MarkAsRegistered();
        }
        
        // ===== NAVIGATION METHODS (REQUIRED BY BASE CLASS) =====
        
        public override void NavigateToInitialStep()
        {
            _currentStep = "Initial";
            _logger.LogDebug("Dummy domain: Navigated to initial step");
        }
        
        public override void NavigateToFinalStep()
        {
            _currentStep = "Final";
            _logger.LogDebug("Dummy domain: Navigated to final step");
        }
        
        public override bool CanNavigateBack()
        {
            return !string.IsNullOrEmpty(_currentStep) && _currentStep != "Initial";
        }
        
        public override bool CanNavigateForward()
        {
            return !string.IsNullOrEmpty(_currentStep) && _currentStep != "Final";
        }
        
        // ===== DUMMY DOMAIN SPECIFIC METHODS =====
        
        public void ChangeWorkspace(string workspaceName, string content)
        {
            PublishEvent(new DummyEvents.DummyWorkspaceChanged
            {
                WorkspaceName = workspaceName,
                NewContent = content
            });
            
            _logger.LogDebug("Dummy domain: Changed {WorkspaceName} to {Content}", workspaceName, content);
        }
        
        public void UpdateStatus(string status, string message)
        {
            PublishEvent(new DummyEvents.DummyStatusChanged
            {
                Status = status,
                Message = message
            });
            
            _logger.LogDebug("Dummy domain: Status updated to {Status}: {Message}", status, message);
        }
        
        public async Task RequestDomainTransition(string targetDomain, object? transitionData = null)
        {
            _logger.LogInformation("Dummy domain: Requesting transition to {TargetDomain}", targetDomain);
            
            // This would typically trigger cross-domain coordination
            // For dummy purposes, just log the request
            await Task.Delay(100); // Simulate async operation
            _logger.LogDebug("Dummy domain: Domain transition request processed");
        }
    }
}