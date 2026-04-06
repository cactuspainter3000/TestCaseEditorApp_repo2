using System;
using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Title area ViewModel with configuration-based updates and built-in idempotency.
    /// Subscribes to view configuration broadcasts and only updates when needed.
    /// </summary>
    public partial class ConfigurableTitleAreaViewModel : ObservableObject
    {
        [ObservableProperty]
        private object? activeTitle;

        [ObservableProperty]
        private string currentContext = "Default";
        
        [ObservableProperty]
        private string sectionName = "Default";
        
        private readonly INavigationMediator _navigationMediator;
        private ViewConfiguration? _currentConfiguration;

        public ConfigurableTitleAreaViewModel(INavigationMediator navigationMediator)
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
            
            // IDEMPOTENCY CHECK: Already showing this title?
            if (ReferenceEquals(_currentConfiguration?.TitleViewModel, configuration.TitleViewModel))
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigurableTitleAreaViewModel] Already showing title for {configuration.SectionName} - no update needed");
                
                // Publish that we handled it but no change was needed
                _navigationMediator.Publish(new ViewConfigurationEvents.ViewConfigurationApplied(
                    configuration, "Title", wasChanged: false));
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[ConfigurableTitleAreaViewModel] Switching title: {_currentConfiguration?.SectionName ?? "none"} → {configuration.SectionName}");

            _currentConfiguration = configuration;
            ActiveTitle = configuration.TitleViewModel;
            CurrentContext = configuration.Context?.ToString() ?? "Default";
            SectionName = configuration.SectionName;

            System.Diagnostics.Debug.WriteLine($"[ConfigurableTitleAreaViewModel] Title updated: ActiveTitle={ActiveTitle?.GetType().Name ?? "null"}, Context={CurrentContext}");

            // Publish that we applied the configuration  
            _navigationMediator.Publish(new ViewConfigurationEvents.ViewConfigurationApplied(
                configuration, "Title", wasChanged: true));
        }

        /// <summary>
        /// Clear the current configuration (for shutdown/cleanup)
        /// </summary>
        public void ClearConfiguration()
        {
            _currentConfiguration = null;
            ActiveTitle = null;
            CurrentContext = "Default";
            SectionName = "Default";
            
            System.Diagnostics.Debug.WriteLine("[ConfigurableTitleAreaViewModel] Configuration cleared");
        }
    }
}