using System;
using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Content area ViewModel with configuration-based updates and built-in idempotency.
    /// Subscribes to view configuration broadcasts and only updates when needed.
    /// </summary>
    public partial class ConfigurableContentAreaViewModel : ObservableObject
    {
        [ObservableProperty]
        private object? currentContent;

        [ObservableProperty]
        private string contentType = "Default";
        
        [ObservableProperty]
        private string sectionName = "Default";
        
        private readonly INavigationMediator _navigationMediator;
        private ViewConfiguration? _currentConfiguration;

        public ConfigurableContentAreaViewModel(INavigationMediator navigationMediator)
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
            
            System.Diagnostics.Debug.WriteLine($"*** ConfigurableContentAreaViewModel: OnViewConfigurationRequested called for {configuration.SectionName} ***");
            try {
                System.IO.File.AppendAllText("debug_requirements.log", 
                    $"{DateTime.Now}: ConfigurableContentAreaViewModel: OnViewConfigurationRequested({configuration.SectionName})\\n");
            } catch { /* ignore */ }
            
            // IDEMPOTENCY CHECK: Already showing this content?
            if (ReferenceEquals(_currentConfiguration?.ContentViewModel, configuration.ContentViewModel))
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigurableContentAreaViewModel] Already showing content for {configuration.SectionName} - no update needed");
                
                // Publish that we handled it but no change was needed
                _navigationMediator.Publish(new ViewConfigurationEvents.ViewConfigurationApplied(
                    configuration, "Content", wasChanged: false));
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[ConfigurableContentAreaViewModel] Switching content: {_currentConfiguration?.SectionName ?? "none"} → {configuration.SectionName}");
            try {
                System.IO.File.AppendAllText("debug_requirements.log", 
                    $"{DateTime.Now}: ConfigurableContentAreaViewModel: Switching content to {configuration.SectionName}\\n");
            } catch { /* ignore */ }
            
            // Update content
            CurrentContent = configuration.ContentViewModel;
            ContentType = DetermineContentType(configuration.ContentViewModel);
            SectionName = configuration.SectionName;
            _currentConfiguration = configuration;
            
            // Publish that we applied the configuration and it changed
            _navigationMediator.Publish(new ViewConfigurationEvents.ViewConfigurationApplied(
                configuration, "Content", wasChanged: true));
        }

        /// <summary>
        /// Determine content type from the ViewModel type
        /// </summary>
        private static string DetermineContentType(object? contentViewModel)
        {
            if (contentViewModel == null) return "None";
            
            var typeName = contentViewModel.GetType().Name;
            
            // Extract meaningful names from ViewModel types
            return typeName switch
            {
                var name when name.Contains("Project") => "Project",
                var name when name.Contains("Requirements") => "Requirements",
                var name when name.Contains("TestCase") => "TestCaseGenerator",
                var name when name.Contains("Import") => "Import",
                var name when name.Contains("Workflow") => "Workflow",
                var name when name.Contains("Placeholder") => "Placeholder",
                var name when name.Contains("Initial") => "Welcome",
                _ => typeName
            };
        }

        /// <summary>
        /// Clear current configuration (for cleanup)
        /// </summary>
        public void ClearConfiguration()
        {
            CurrentContent = null;
            ContentType = "Default";
            SectionName = "Default";
            _currentConfiguration = null;
            
            System.Diagnostics.Debug.WriteLine("[ConfigurableContentAreaViewModel] Configuration cleared");
        }
    }
}