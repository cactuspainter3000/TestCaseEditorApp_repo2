using System;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Implementation of domain UI coordination that manages progress and notifications
    /// with domain context and conflict resolution.
    /// </summary>
    public class DomainUICoordinator : IDomainUICoordinator
    {
        private readonly NotificationService _notificationService;
        private readonly ILogger<DomainUICoordinator> _logger;
        
        // Progress state management
        private string? _currentProgressDomain;
        private string? _currentProgressMessage;
        private double _currentProgressPercentage;
        private readonly object _progressLock = new object();

        public DomainUICoordinator(NotificationService notificationService, ILogger<DomainUICoordinator> logger)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string? CurrentProgressDomain 
        { 
            get 
            { 
                lock (_progressLock) 
                { 
                    return _currentProgressDomain; 
                } 
            } 
        }

        /// <inheritdoc />
        public string? CurrentProgressMessage 
        { 
            get 
            { 
                lock (_progressLock) 
                { 
                    return _currentProgressMessage; 
                } 
            } 
        }

        /// <inheritdoc />
        public double CurrentProgressPercentage 
        { 
            get 
            { 
                lock (_progressLock) 
                { 
                    return _currentProgressPercentage; 
                } 
            } 
        }

        /// <inheritdoc />
        public bool IsProgressActive 
        { 
            get 
            { 
                lock (_progressLock) 
                { 
                    return !string.IsNullOrEmpty(_currentProgressDomain); 
                } 
            } 
        }

        /// <inheritdoc />
        public event EventHandler<DomainProgressChangedEventArgs>? DomainProgressChanged;

        /// <inheritdoc />
        public event EventHandler<DomainNotificationEventArgs>? DomainNotificationShown;

        /// <inheritdoc />
        public bool ShowDomainProgress(string domainName, string message, double percentage)
        {
            if (string.IsNullOrWhiteSpace(domainName))
                throw new ArgumentException("Domain name cannot be null or empty", nameof(domainName));
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Message cannot be null or empty", nameof(message));

            percentage = Math.Clamp(percentage, 0, 100);

            lock (_progressLock)
            {
                // Check if another domain is already showing progress
                if (!string.IsNullOrEmpty(_currentProgressDomain) && _currentProgressDomain != domainName)
                {
                    _logger.LogWarning("Domain {RequestingDomain} tried to show progress but {CurrentDomain} is already active", 
                        domainName, _currentProgressDomain);
                    return false;
                }

                _currentProgressDomain = domainName;
                _currentProgressMessage = message;
                _currentProgressPercentage = percentage;

                _logger.LogDebug("Domain progress started: {Domain} - {Message} ({Percentage}%)", 
                    domainName, message, percentage);
            }

            // Raise event outside lock to prevent deadlocks
            DomainProgressChanged?.Invoke(this, new DomainProgressChangedEventArgs
            {
                DomainName = domainName,
                Message = message,
                Percentage = percentage,
                IsActive = true
            });

            return true;
        }

        /// <inheritdoc />
        public bool UpdateDomainProgress(string domainName, string message, double percentage)
        {
            if (string.IsNullOrWhiteSpace(domainName))
                throw new ArgumentException("Domain name cannot be null or empty", nameof(domainName));
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Message cannot be null or empty", nameof(message));

            percentage = Math.Clamp(percentage, 0, 100);

            lock (_progressLock)
            {
                // Only allow updates from the domain that owns current progress
                if (_currentProgressDomain != domainName)
                {
                    _logger.LogWarning("Domain {RequestingDomain} tried to update progress but {CurrentDomain} owns it", 
                        domainName, _currentProgressDomain ?? "none");
                    return false;
                }

                _currentProgressMessage = message;
                _currentProgressPercentage = percentage;

                _logger.LogDebug("Domain progress updated: {Domain} - {Message} ({Percentage}%)", 
                    domainName, message, percentage);
            }

            // Raise event outside lock
            DomainProgressChanged?.Invoke(this, new DomainProgressChangedEventArgs
            {
                DomainName = domainName,
                Message = message,
                Percentage = percentage,
                IsActive = true
            });

            return true;
        }

        /// <inheritdoc />
        public bool HideDomainProgress(string domainName)
        {
            if (string.IsNullOrWhiteSpace(domainName))
                throw new ArgumentException("Domain name cannot be null or empty", nameof(domainName));

            lock (_progressLock)
            {
                // Only allow hiding from the domain that owns current progress
                if (_currentProgressDomain != domainName)
                {
                    _logger.LogWarning("Domain {RequestingDomain} tried to hide progress but {CurrentDomain} owns it", 
                        domainName, _currentProgressDomain ?? "none");
                    return false;
                }

                _logger.LogDebug("Domain progress hidden: {Domain}", domainName);

                _currentProgressDomain = null;
                _currentProgressMessage = null;
                _currentProgressPercentage = 0;
            }

            // Raise event outside lock
            DomainProgressChanged?.Invoke(this, new DomainProgressChangedEventArgs
            {
                DomainName = null,
                Message = null,
                Percentage = 0,
                IsActive = false
            });

            return true;
        }

        /// <inheritdoc />
        public void ShowDomainNotification(string domainName, string message, DomainNotificationType type, int durationSeconds = 4)
        {
            if (string.IsNullOrWhiteSpace(domainName))
                throw new ArgumentException("Domain name cannot be null or empty", nameof(domainName));
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Message cannot be null or empty", nameof(message));

            // Format message with domain context
            var contextualMessage = $"{domainName}: {message}";

            // Convert domain notification type to toast type
            var toastType = type switch
            {
                DomainNotificationType.Info => ToastType.Info,
                DomainNotificationType.Success => ToastType.Success,
                DomainNotificationType.Warning => ToastType.Warning,
                DomainNotificationType.Error => ToastType.Error,
                _ => ToastType.Info
            };

            // Show notification through existing service
            switch (type)
            {
                case DomainNotificationType.Info:
                    _notificationService.ShowInfo(contextualMessage, durationSeconds);
                    break;
                case DomainNotificationType.Success:
                    _notificationService.ShowSuccess(contextualMessage, durationSeconds);
                    break;
                case DomainNotificationType.Warning:
                    _notificationService.ShowWarning(contextualMessage, durationSeconds);
                    break;
                case DomainNotificationType.Error:
                    _notificationService.ShowError(contextualMessage, durationSeconds);
                    break;
            }

            _logger.LogDebug("Domain notification shown: {Domain} - {Type} - {Message}", 
                domainName, type, message);

            // Raise event for listeners
            DomainNotificationShown?.Invoke(this, new DomainNotificationEventArgs
            {
                DomainName = domainName,
                Message = message,
                Type = type,
                DurationSeconds = durationSeconds
            });
        }
    }
}