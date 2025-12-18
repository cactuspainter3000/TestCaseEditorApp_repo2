using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Base class for all domain ViewModels that enforces mediator injection and provides
    /// common functionality like commands, validation, and communication patterns.
    /// Makes architectural violations impossible by requiring mediator at construction time.
    /// </summary>
    public abstract partial class BaseDomainViewModel : ObservableObject, IDisposable
    {
        protected readonly object _mediator; // Generic to allow different mediator types
        protected readonly ILogger _logger;
        private bool _isDisposed = false;
        
        // Common properties all ViewModels need
        [ObservableProperty] protected bool _isBusy;
        [ObservableProperty] protected string _title = string.Empty;
        [ObservableProperty] protected string _statusMessage = string.Empty;
        [ObservableProperty] protected bool _hasErrors;
        [ObservableProperty] protected string _errorMessage = string.Empty;
        [ObservableProperty] protected double _progressValue;
        [ObservableProperty] protected bool _isProgressVisible;
        
        // Validation state
        protected readonly Dictionary<string, List<string>> _validationErrors = new();
        
        /// <summary>
        /// Constructor that REQUIRES mediator injection - no way to forget it!
        /// </summary>
        /// <param name="mediator">Domain-specific mediator (ITestCaseGenerationMediator, ITestFlowMediator, etc.)</param>
        /// <param name="logger">Logger for this ViewModel</param>
        /// <exception cref="ArgumentNullException">Thrown if mediator is null</exception>
        /// <exception cref="InvalidOperationException">Thrown if mediator is not properly registered</exception>
        protected BaseDomainViewModel(object mediator, ILogger logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator), 
                "🚨 MEDIATOR REQUIRED: Every domain ViewModel must have a mediator for communication! " +
                "Use ViewModelFactory to create ViewModels with proper mediator injection.");
            
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Fail immediately if mediator not properly configured
            if (mediator.GetType().GetProperty("IsRegistered")?.GetValue(mediator) is bool isRegistered && !isRegistered)
            {
                throw new InvalidOperationException(
                    $"🚨 MEDIATOR NOT REGISTERED: {mediator.GetType().Name} must be registered in DI container before creating ViewModels! " +
                    "Check ViewModelFactory registration.");
            }
            
            _logger.LogDebug("Created {ViewModelType} with mediator {MediatorType}", GetType().Name, mediator.GetType().Name);
            
            InitializeCommands();
            InitializeValidation();
        }
        
        // Common commands all ViewModels get automatically
        public IAsyncRelayCommand SaveCommand { get; protected set; } = null!;
        public IRelayCommand CancelCommand { get; protected set; } = null!;
        public IAsyncRelayCommand RefreshCommand { get; protected set; } = null!;
        public IRelayCommand ClearErrorsCommand { get; protected set; } = null!;
        
        /// <summary>
        /// Initialize common commands - override to add domain-specific commands
        /// </summary>
        protected virtual void InitializeCommands()
        {
            SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
            CancelCommand = new RelayCommand(Cancel, CanCancel);
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
            ClearErrorsCommand = new RelayCommand(ClearErrors, () => HasErrors);
        }
        
        /// <summary>
        /// Initialize validation rules - override to add domain-specific validation
        /// </summary>
        protected virtual void InitializeValidation()
        {
            // Subscribe to property changes for validation
            PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName != null && !string.IsNullOrEmpty(e.PropertyName))
                {
                    ValidateProperty(e.PropertyName, GetType().GetProperty(e.PropertyName)?.GetValue(this));
                }
            };
        }
        
        // Abstract methods that domains must implement
        protected abstract Task SaveAsync();
        protected abstract void Cancel();
        protected abstract Task RefreshAsync();
        protected abstract bool CanSave();
        protected abstract bool CanCancel();
        protected abstract bool CanRefresh();
        
        /// <summary>
        /// Easy method for sharing data with other domains
        /// </summary>
        protected void ShareWithOtherDomain<T>(T data) where T : class
        {
            ValidateNotDisposed();
            
            try
            {
                var method = _mediator.GetType().GetMethod("RequestCrossDomainAction");
                var genericMethod = method?.MakeGenericMethod(typeof(T));
                genericMethod?.Invoke(_mediator, new object[] { data });
                
                _logger.LogDebug("Shared {DataType} with other domains from {ViewModelType}", typeof(T).Name, GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to share data {DataType} with other domains", typeof(T).Name);
                SetError($"Failed to communicate with other components: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Request data from other domains with callback
        /// </summary>
        protected void RequestDataFromOtherDomain<TRequest, TResponse>(TRequest request, Action<TResponse> callback) 
            where TRequest : class where TResponse : class
        {
            ValidateNotDisposed();
            
            try
            {
                // This would be handled by DomainCoordinator
                var crossDomainRequest = new CrossDomainDataRequest<TRequest, TResponse>
                {
                    Request = request,
                    Callback = callback
                };
                
                ShareWithOtherDomain(crossDomainRequest);
                
                _logger.LogDebug("Requested {RequestType} data from other domains", typeof(TRequest).Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to request {RequestType} from other domains", typeof(TRequest).Name);
                SetError($"Failed to request data: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Notify all other domains about something
        /// </summary>
        protected void NotifyOtherDomains<T>(T notification) where T : class
        {
            ValidateNotDisposed();
            
            try
            {
                var method = _mediator.GetType().GetMethod("BroadcastToAllDomains");
                var genericMethod = method?.MakeGenericMethod(typeof(T));
                genericMethod?.Invoke(_mediator, new object[] { notification });
                
                _logger.LogDebug("Notified other domains of {NotificationType} from {ViewModelType}", typeof(T).Name, GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify other domains of {NotificationType}", typeof(T).Name);
                SetError($"Failed to send notification: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Publish events within this domain
        /// </summary>
        protected void PublishEvent<T>(T eventData) where T : class
        {
            ValidateNotDisposed();
            
            try
            {
                var method = _mediator.GetType().GetMethod("PublishEvent", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var genericMethod = method?.MakeGenericMethod(typeof(T));
                genericMethod?.Invoke(_mediator, new object[] { eventData });
                
                _logger.LogDebug("Published {EventType} within domain from {ViewModelType}", typeof(T).Name, GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish {EventType} within domain", typeof(T).Name);
                SetError($"Failed to publish event: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Subscribe to events within this domain
        /// </summary>
        protected void Subscribe<T>(Action<T> handler) where T : class
        {
            ValidateNotDisposed();
            
            try
            {
                var method = _mediator.GetType().GetMethod("Subscribe");
                var genericMethod = method?.MakeGenericMethod(typeof(T));
                genericMethod?.Invoke(_mediator, new object[] { handler });
                
                _logger.LogDebug("Subscribed to {EventType} from {ViewModelType}", typeof(T).Name, GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to subscribe to {EventType}", typeof(T).Name);
            }
        }
        
        // Common validation logic
        protected virtual void ValidateProperty(string propertyName, object? value)
        {
            _validationErrors.Remove(propertyName);
            
            var errors = ValidatePropertyValue(propertyName, value);
            if (errors.Any())
            {
                _validationErrors[propertyName] = errors;
            }
            
            HasErrors = _validationErrors.Any();
            ErrorMessage = HasErrors ? string.Join("; ", _validationErrors.SelectMany(e => e.Value)) : string.Empty;
            
            OnPropertyChanged(nameof(HasErrors));
            OnPropertyChanged(nameof(ErrorMessage));
        }
        
        /// <summary>
        /// Override to provide domain-specific validation rules
        /// </summary>
        protected virtual List<string> ValidatePropertyValue(string propertyName, object? value)
        {
            var errors = new List<string>();
            
            // Basic validation using data annotations
            var property = GetType().GetProperty(propertyName);
            if (property != null)
            {
                var validationAttributes = property.GetCustomAttributes(typeof(ValidationAttribute), true)
                    .Cast<ValidationAttribute>();
                
                foreach (var attribute in validationAttributes)
                {
                    if (!attribute.IsValid(value))
                    {
                        errors.Add(attribute.ErrorMessage ?? $"{propertyName} is invalid");
                    }
                }
            }
            
            return errors;
        }
        
        // Common error handling
        protected void SetError(string error)
        {
            HasErrors = true;
            ErrorMessage = error;
            _logger.LogWarning("Error set in {ViewModelType}: {Error}", GetType().Name, error);
        }
        
        protected void ClearErrors()
        {
            HasErrors = false;
            ErrorMessage = string.Empty;
            _validationErrors.Clear();
            _logger.LogDebug("Errors cleared in {ViewModelType}", GetType().Name);
        }
        
        // Common progress handling
        protected void SetProgress(double value, string? message = null)
        {
            ProgressValue = Math.Clamp(value, 0, 100);
            IsProgressVisible = value > 0 && value < 100;
            
            if (!string.IsNullOrEmpty(message))
                StatusMessage = message;
                
            _logger.LogDebug("Progress set to {Progress}% in {ViewModelType}: {Message}", 
                ProgressValue, GetType().Name, message);
        }
        
        // Validation helpers
        private void ValidateNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().Name, "Cannot use disposed ViewModel");
        }
        
        // Disposal
        public virtual void Dispose()
        {
            if (!_isDisposed)
            {
                _validationErrors.Clear();
                
                if (_mediator is IDisposable disposableMediator)
                {
                    disposableMediator.Dispose();
                }
                
                _isDisposed = true;
                _logger.LogDebug("Disposed {ViewModelType}", GetType().Name);
            }
        }
    }
    
    /// <summary>
    /// Helper class for cross-domain data requests
    /// </summary>
    public class CrossDomainDataRequest<TRequest, TResponse> 
        where TRequest : class 
        where TResponse : class
    {
        public TRequest Request { get; set; } = default!;
        public Action<TResponse> Callback { get; set; } = default!;
    }
}