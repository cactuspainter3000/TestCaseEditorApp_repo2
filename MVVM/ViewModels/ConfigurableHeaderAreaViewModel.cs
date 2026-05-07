using System;
using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Header area ViewModel with configuration-based updates and built-in idempotency.
    /// Subscribes to view configuration broadcasts and only updates when needed.
    /// </summary>
    public partial class ConfigurableHeaderAreaViewModel : ObservableObject
    {
        [ObservableProperty]
        private object? activeHeader;

        [ObservableProperty]
        private string currentContext = "Default";
        
        [ObservableProperty]
        private string sectionName = "Default";
        
        private readonly INavigationMediator _navigationMediator;
        private ViewConfiguration? _currentConfiguration;

        public ConfigurableHeaderAreaViewModel(INavigationMediator navigationMediator)
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
            
            // IDEMPOTENCY CHECK: Already showing this header?
            if (ReferenceEquals(_currentConfiguration?.HeaderViewModel, configuration.HeaderViewModel))
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigurableHeaderAreaViewModel] Already showing header for {configuration.SectionName} - no update needed");
                
                // Publish that we handled it but no change was needed
                _navigationMediator.Publish(new ViewConfigurationEvents.ViewConfigurationApplied(
                    configuration, "Header", wasChanged: false));
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[ConfigurableHeaderAreaViewModel] Switching header: {_currentConfiguration?.SectionName ?? "none"} → {configuration.SectionName}");
            
            // Update header
            ActiveHeader = configuration.HeaderViewModel;
            CurrentContext = DetermineFriendlyName(configuration.SectionName);
            SectionName = configuration.SectionName;
            _currentConfiguration = configuration;
            
            // ✅ Trigger refresh on the header ViewModel if it supports it
            if (configuration.HeaderViewModel is BaseDomainViewModel baseDomainVM)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigurableHeaderAreaViewModel] Refreshing header ViewModel: {baseDomainVM.GetType().Name}");
                baseDomainVM.RefreshCommand?.Execute(null);
            }
            
            // Publish that we applied the configuration and it changed
            _navigationMediator.Publish(new ViewConfigurationEvents.ViewConfigurationApplied(
                configuration, "Header", wasChanged: true));
        }

        /// <summary>
        /// Convert section names to user-friendly display names
        /// </summary>
        private static string DetermineFriendlyName(string sectionName)
        {
            return sectionName?.ToLowerInvariant() switch
            {
                "testcase" or "test case creator" => "Test Case Generator",
                "requirements" => "Requirements",
                "project" => "Project",
                "testflow" => "Test Flow",
                "import" => "Import",
                "newproject" => "New Project",
                _ => sectionName ?? "Default"
            };
        }

        /// <summary>
        /// Clear current configuration (for cleanup)
        /// </summary>
        public void ClearConfiguration()
        {
            ActiveHeader = null;
            CurrentContext = "Default";
            SectionName = "Default";
            _currentConfiguration = null;
            
            System.Diagnostics.Debug.WriteLine("[ConfigurableHeaderAreaViewModel] Configuration cleared");
        }
    }
}