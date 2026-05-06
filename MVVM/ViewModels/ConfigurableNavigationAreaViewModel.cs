using System;
using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Navigation area ViewModel with configuration-based updates and built-in idempotency.
    /// Subscribes to view configuration broadcasts and only updates when needed.
    /// </summary>
    public partial class ConfigurableNavigationAreaViewModel : ObservableObject
    {
        [ObservableProperty]
        private object? currentContent;

        [ObservableProperty]
        private string contentType = "Default";
        
        [ObservableProperty]
        private string sectionName = "Default";
        
        private readonly INavigationMediator _navigationMediator;
        private ViewConfiguration? _currentConfiguration;

        public ConfigurableNavigationAreaViewModel(INavigationMediator navigationMediator)
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
            
            System.Diagnostics.Debug.WriteLine($"[ConfigurableNavigationAreaViewModel] *** Navigation configuration requested for: {configuration.SectionName} ***");
            System.Diagnostics.Debug.WriteLine($"[ConfigurableNavigationAreaViewModel] *** Navigation content type: {configuration.NavigationViewModel?.GetType().Name} ***");
            
            // FORCE REFRESH: Clear content first to ensure clean binding state
            if (CurrentContent != null && configuration.SectionName == "Requirements")
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigurableNavigationAreaViewModel] *** REQUIREMENTS DOMAIN: Force clearing old content to prevent binding conflicts ***");
                CurrentContent = null;
                ContentType = "Clearing";
                
                // Force UI update on dispatcher
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new System.Action(() => {
                    SetRequirementsContent(configuration);
                }), System.Windows.Threading.DispatcherPriority.Normal);
                return;
            }
            
            // IDEMPOTENCY CHECK: Already showing this content?
            if (ReferenceEquals(_currentConfiguration?.NavigationViewModel, configuration.NavigationViewModel))
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigurableNavigationAreaViewModel] Already showing content for {configuration.SectionName} - no update needed");
                
                // Publish that we handled it but no change was needed
                _navigationMediator.Publish(new ViewConfigurationEvents.ViewConfigurationApplied(
                    configuration, "Navigation", wasChanged: false));
                return;
            }

            // UPDATE NEEDED: New content or different configuration
            SetRequirementsContent(configuration);
        }
        
        private void SetRequirementsContent(ViewConfiguration configuration)
        {
            System.Diagnostics.Debug.WriteLine($"[ConfigurableNavigationAreaViewModel] Switching to {configuration.SectionName} with navigation content: {configuration.NavigationViewModel?.GetType().Name ?? "null"}");

            // Store the new configuration for future idempotency checks
            _currentConfiguration = configuration;

            // Apply the new configuration
            CurrentContent = configuration.NavigationViewModel;
            ContentType = configuration.NavigationViewModel?.GetType().Name ?? "Empty";
            SectionName = configuration.SectionName;

            System.Diagnostics.Debug.WriteLine($"[ConfigurableNavigationAreaViewModel] *** Navigation content updated to: {ContentType} ***");
            System.Diagnostics.Debug.WriteLine($"[ConfigurableNavigationAreaViewModel] *** CurrentContent is now: {CurrentContent?.GetType().Name} ***");

            // Publish that we completed the update
            _navigationMediator.Publish(new ViewConfigurationEvents.ViewConfigurationApplied(
                configuration, "Navigation", wasChanged: true));
        }

        /// <summary>
        /// Manual configuration apply (alternative to event-based)
        /// </summary>
        public bool ApplyViewConfiguration(ViewConfiguration configuration)
        {
            if (configuration == null) return false;

            // Same idempotency logic as event handler
            if (ReferenceEquals(_currentConfiguration?.NavigationViewModel, configuration.NavigationViewModel))
            {
                return false; // No change needed
            }

            _currentConfiguration = configuration;
            CurrentContent = configuration.NavigationViewModel;
            ContentType = configuration.NavigationViewModel?.GetType().Name ?? "Empty";
            SectionName = configuration.SectionName;

            return true; // Change was applied
        }

        /// <summary>
        /// For diagnostics - check what configuration is currently active
        /// </summary>
        public ViewConfiguration? GetCurrentConfiguration() => _currentConfiguration;
        
        /// <summary>
        /// For diagnostics - manual content clearing
        /// </summary>
        public void ClearContent()
        {
            _currentConfiguration = null;
            CurrentContent = null;
            ContentType = "Empty";
            SectionName = "Default";
        }
    }
}