using System;
using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Notification area ViewModel with configuration-based updates and built-in idempotency.
    /// Subscribes to view configuration broadcasts and only updates when needed.
    /// </summary>
    public partial class ConfigurableNotificationAreaViewModel : ObservableObject
    {
        [ObservableProperty]
        private object? currentNotification;

        [ObservableProperty]
        private string notificationType = "Default";
        
        [ObservableProperty]
        private string sectionName = "Default";
        
        private readonly INavigationMediator _navigationMediator;
        private ViewConfiguration? _currentConfiguration;

        public ConfigurableNotificationAreaViewModel(INavigationMediator navigationMediator)
        {
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            
            // Subscribe to configuration broadcasts
            _navigationMediator.Subscribe<ViewConfigurationEvents.ApplyViewConfiguration>(OnViewConfigurationRequested);
        }

        /// <summary>
        /// Handle view configuration broadcast with idempotency check
        /// </summary>
        private void OnViewConfigurationRequested(ViewConfigurationEvents.ApplyViewConfiguration configEvent)
        {
            var configuration = configEvent.Configuration;
            
            // IDEMPOTENCY CHECK: Already showing this notification?
            if (ReferenceEquals(_currentConfiguration?.NotificationViewModel, configuration.NotificationViewModel))
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigurableNotificationAreaViewModel] Already showing notification for {configuration.SectionName} - no update needed");
                
                // Publish that we handled it but no change was needed
                _navigationMediator.Publish(new ViewConfigurationEvents.ViewConfigurationApplied(
                    configuration, "Notification", wasChanged: false));
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[ConfigurableNotificationAreaViewModel] Switching notification: {_currentConfiguration?.SectionName ?? "none"} → {configuration.SectionName}");
            
            // Dispose old notification if disposable
            if (CurrentNotification is IDisposable disposableNotification)
            {
                disposableNotification.Dispose();
            }
            
            // Update notification
            CurrentNotification = configuration.NotificationViewModel;
            NotificationType = DetermineNotificationType(configuration.NotificationViewModel);
            SectionName = configuration.SectionName;
            _currentConfiguration = configuration;
            
            // Publish that we applied the configuration and it changed
            _navigationMediator.Publish(new ViewConfigurationEvents.ViewConfigurationApplied(
                configuration, "Notification", wasChanged: true));
        }

        /// <summary>
        /// Determine notification type from the ViewModel type
        /// </summary>
        private static string DetermineNotificationType(object? notificationViewModel)
        {
            if (notificationViewModel == null) return "None";
            
            var typeName = notificationViewModel.GetType().Name;
            
            return typeName switch
            {
                var name when name.Contains("Default") => "Default",
                var name when name.Contains("TestCase") => "TestCaseGenerator",
                var name when name.Contains("Workspace") => "Workspace",
                var name when name.Contains("Error") => "Error",
                var name when name.Contains("Success") => "Success",
                _ => typeName
            };
        }

        /// <summary>
        /// Clear current configuration (for cleanup)
        /// </summary>
        public void ClearConfiguration()
        {
            if (CurrentNotification is IDisposable disposableNotification)
            {
                disposableNotification.Dispose();
            }
            
            CurrentNotification = null;
            NotificationType = "Default";
            SectionName = "Default";
            _currentConfiguration = null;
            
            System.Diagnostics.Debug.WriteLine("[ConfigurableNotificationAreaViewModel] Configuration cleared");
        }
    }
}