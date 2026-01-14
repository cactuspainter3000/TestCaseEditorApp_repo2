using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Utils
{
    /// <summary>
    /// Non-generic base class for static domain coordinator management
    /// </summary>
    public abstract class BaseDomainMediatorBase
    {
        // Domain coordinator for cross-domain communication (optional)
        private static IDomainCoordinator? _domainCoordinator;
        
        /// <summary>
        /// Set the domain coordinator for all mediators (called during app startup)
        /// </summary>
        public static void SetDomainCoordinator(IDomainCoordinator coordinator)
        {
            _domainCoordinator = coordinator;
        }
        
        /// <summary>
        /// Get the current domain coordinator
        /// </summary>
        protected static IDomainCoordinator? GetDomainCoordinator() => _domainCoordinator;
    }

    /// <summary>
    /// Base class for all domain-specific mediators.
    /// Provides common functionality for event publishing/subscription, navigation state management,
    /// and fail-fast validation to prevent architectural violations.
    /// </summary>
    public abstract class BaseDomainMediator<TEvents> : BaseDomainMediatorBase, IDisposable where TEvents : class
    {
        protected readonly Dictionary<Type, List<Delegate>> _subscriptions = new();
        protected readonly ILogger _logger;
        protected readonly IDomainUICoordinator _uiCoordinator;
        protected readonly string _domainName;
        protected readonly PerformanceMonitoringService? _performanceMonitor;
        protected readonly EventReplayService? _eventReplay;
        private bool _isDisposed = false;
        
        // Navigation state common to all mediators
        protected string? _currentStep;
        protected object? _currentViewModel;
        protected readonly Stack<string> _navigationHistory = new();
        
        // Registration state for fail-fast validation
        protected bool _isRegistered = false;
        
        protected BaseDomainMediator(ILogger logger, IDomainUICoordinator uiCoordinator, string domainName, 
            PerformanceMonitoringService? performanceMonitor = null, EventReplayService? eventReplay = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _uiCoordinator = uiCoordinator ?? throw new ArgumentNullException(nameof(uiCoordinator));
            _domainName = !string.IsNullOrWhiteSpace(domainName) ? domainName : throw new ArgumentException("Domain name cannot be null or empty", nameof(domainName));
            _performanceMonitor = performanceMonitor;
            _eventReplay = eventReplay;
            _logger.LogDebug("Created {MediatorType} with performance monitoring: {HasPerf}, event replay: {HasReplay}", 
                GetType().Name, performanceMonitor != null, eventReplay != null);
        }
        
        /// <summary>
        /// Indicates whether this mediator has been properly registered and initialized.
        /// Used for fail-fast validation in ViewModels.
        /// </summary>
        public virtual bool IsRegistered => _isRegistered;
        
        /// <summary>
        /// Current step/view in this domain's workflow
        /// </summary>
        public virtual string? CurrentStep => _currentStep;
        
        /// <summary>
        /// Current ViewModel being displayed for this domain
        /// </summary>
        public virtual object? CurrentViewModel => _currentViewModel;
        
        /// <summary>
        /// Subscribe to domain-specific events with strong typing
        /// </summary>
        public virtual void Subscribe<T>(Action<T> handler) where T : class
        {
            ValidateNotDisposed();
            ValidateEventType<T>();
            
            var eventType = typeof(T);
            if (!_subscriptions.ContainsKey(eventType))
                _subscriptions[eventType] = new List<Delegate>();
                
            _subscriptions[eventType].Add(handler);
            _logger.LogDebug("Subscribed to {EventType} in {MediatorType}", eventType.Name, GetType().Name);
        }
        
        /// <summary>
        /// Unsubscribe from domain-specific events
        /// </summary>
        public virtual void Unsubscribe<T>(Action<T> handler) where T : class
        {
            ValidateNotDisposed();
            
            var eventType = typeof(T);
            if (_subscriptions.ContainsKey(eventType))
            {
                _subscriptions[eventType].Remove(handler);
                _logger.LogDebug("Unsubscribed from {EventType} in {MediatorType}", eventType.Name, GetType().Name);
            }
        }
        
        /// <summary>
        /// Publish events within this domain with error handling and optional replay recording
        /// </summary>
        protected virtual void PublishEvent<T>(T eventData) where T : class
        {
            ValidateNotDisposed();
            ValidateEventType<T>();
            
            // Record event for replay debugging
            _eventReplay?.RecordEvent(eventData, _domainName, GetType().Name);
            
            var eventType = typeof(T);
            if (_subscriptions.ContainsKey(eventType))
            {
                var handlersToNotify = _subscriptions[eventType].ToList(); // Create snapshot
                
                foreach (var handler in handlersToNotify.Cast<Action<T>>())
                {
                    try
                    {
                        handler(eventData);
                        _logger.LogDebug("Published {EventType} to handler in {MediatorType}", eventType.Name, GetType().Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error publishing {EventType} in {MediatorType}", eventType.Name, GetType().Name);
                        // Continue to other handlers - don't let one failure break everything
                    }
                }
            }
            else
            {
                _logger.LogDebug("No subscribers for {EventType} in {MediatorType}", eventType.Name, GetType().Name);
            }
        }
        
        /// <summary>
        /// Navigate to a step within this domain and track history
        /// </summary>
        protected virtual void NavigateToStep(string stepName, object? viewModel = null)
        {
            ValidateNotDisposed();
            
            if (string.IsNullOrWhiteSpace(stepName))
                throw new ArgumentException("Step name cannot be null or empty", nameof(stepName));
            
            // Track navigation history
            if (!string.IsNullOrEmpty(_currentStep))
            {
                _navigationHistory.Push(_currentStep);
            }
            
            var previousStep = _currentStep;
            _currentStep = stepName;
            _currentViewModel = viewModel;
            
            _logger.LogDebug("Navigated from {PreviousStep} to {CurrentStep} in {MediatorType}", 
                previousStep ?? "null", _currentStep, GetType().Name);
        }
        
        /// <summary>
        /// Navigate back to the previous step in this domain
        /// </summary>
        public virtual bool TryNavigateBack()
        {
            ValidateNotDisposed();
            
            if (_navigationHistory.Count > 0)
            {
                var previousStep = _navigationHistory.Pop();
                var currentStep = _currentStep;
                _currentStep = previousStep;
                _currentViewModel = null; // Will be recreated by domain logic
                
                _logger.LogDebug("Navigated back from {CurrentStep} to {PreviousStep} in {MediatorType}", 
                    currentStep, _currentStep, GetType().Name);
                    
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Mark this mediator as registered and ready for use
        /// </summary>
        public virtual void MarkAsRegistered()
        {
            _isRegistered = true;
            _logger.LogDebug("{MediatorType} marked as registered", GetType().Name);
        }
        
        /// <summary>
        /// Show progress for this domain with automatic context
        /// </summary>
        protected virtual bool ShowProgress(string message, double percentage = 0)
        {
            return _uiCoordinator.ShowDomainProgress(_domainName, message, percentage);
        }
        
        /// <summary>
        /// Update progress for this domain
        /// </summary>
        protected virtual bool UpdateProgress(string message, double percentage)
        {
            return _uiCoordinator.UpdateDomainProgress(_domainName, message, percentage);
        }
        
        /// <summary>
        /// Hide progress for this domain
        /// </summary>
        protected virtual bool HideProgress()
        {
            return _uiCoordinator.HideDomainProgress(_domainName);
        }
        
        /// <summary>
        /// Show notification for this domain with automatic context
        /// </summary>
        protected virtual void ShowNotification(string message, DomainNotificationType type = DomainNotificationType.Info, int durationSeconds = 4)
        {
            _uiCoordinator.ShowDomainNotification(_domainName, message, type, durationSeconds);
        }
        
        /// <summary>
        /// Get the domain name for this mediator
        /// </summary>
        public virtual string DomainName => _domainName;
        
        /// <summary>
        /// Request cross-domain communication (handled by DomainCoordinator)
        /// </summary>
        public virtual void RequestCrossDomainAction<T>(T request) where T : class
        {
            ValidateNotDisposed();
            
            _logger.LogDebug("Cross-domain request {RequestType} from {MediatorType}", typeof(T).Name, GetType().Name);
            OnCrossDomainActionRequested(request);
        }
        
        /// <summary>
        /// Broadcast notification to all domains (handled by DomainCoordinator)
        /// </summary>
        public virtual void BroadcastToAllDomains<T>(T notification) where T : class
        {
            ValidateNotDisposed();
            
            _logger.LogDebug("Broadcasting {NotificationType} from {MediatorType}", typeof(T).Name, GetType().Name);
            OnBroadcastRequested(notification);
        }
        
        // Abstract methods that domains must implement
        public abstract void NavigateToInitialStep();
        public abstract void NavigateToFinalStep();
        public abstract bool CanNavigateBack();
        public abstract bool CanNavigateForward();
        
        // Virtual methods for cross-domain communication (override in DomainCoordinator)
        protected virtual void OnCrossDomainActionRequested<T>(T request) where T : class
        {
            var domainCoordinator = GetDomainCoordinator();
            if (domainCoordinator != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await domainCoordinator.HandleCrossDomainRequestAsync<object>(request, _domainName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Cross-domain request failed: {RequestType}", typeof(T).Name);
                    }
                });
            }
            else
            {
                _logger.LogWarning("Cross-domain action {RequestType} not handled - DomainCoordinator not configured", typeof(T).Name);
            }
        }
        
        protected virtual void OnBroadcastRequested<T>(T notification) where T : class
        {
            var domainCoordinator = GetDomainCoordinator();
            if (domainCoordinator != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await domainCoordinator.BroadcastNotificationAsync(notification, _domainName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Broadcast failed: {NotificationType}", typeof(T).Name);
                    }
                });
            }
            else
            {
                _logger.LogWarning("Broadcast {NotificationType} not handled - DomainCoordinator not configured", typeof(T).Name);
            }
        }
        
        // Validation methods
        private void ValidateNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().Name, "Cannot use disposed mediator");
        }
        
        private void ValidateEventType<T>() where T : class
        {
            // Ensure event type belongs to this domain's event namespace
            var eventType = typeof(T);
            var eventsType = typeof(TEvents);
            
            if (!eventType.FullName?.StartsWith(eventsType.Namespace ?? "") == true)
            {
                _logger.LogWarning("Event type {EventType} may not belong to domain {DomainType}", 
                    eventType.Name, eventsType.Name);
            }
        }
        
        /// <summary>
        /// Track performance of an operation with automatic timing
        /// </summary>
        protected virtual IDisposable? TrackPerformance(string operationName)
        {
            return _performanceMonitor?.StartOperation(operationName, _domainName);
        }
        
        public virtual void Dispose()
        {
            if (!_isDisposed)
            {
                _subscriptions.Clear();
                _navigationHistory.Clear();
                _isDisposed = true;
                _logger.LogDebug("Disposed {MediatorType}", GetType().Name);
            }
        }
    }
}