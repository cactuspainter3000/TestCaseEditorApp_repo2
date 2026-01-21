using System;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Mediators;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Clean coordinator using configuration pattern - delegates to ViewConfigurationService.
    /// Single responsibility: Respond to navigation requests by applying configurations.
    /// </summary>
    public class ViewAreaCoordinator : IViewAreaCoordinator
    {
        private readonly INavigationMediator _navigationMediator;
        private IViewConfigurationService? _viewConfigurationService;
        private readonly INewProjectMediator _workspaceManagementMediator;

        public SideMenuViewModel SideMenu { get; }
        public ConfigurableTitleAreaViewModel TitleArea { get; }
        public ConfigurableHeaderAreaViewModel HeaderArea { get; }
        public ConfigurableContentAreaViewModel WorkspaceContent { get; }
        public ConfigurableNavigationAreaViewModel NavigationArea { get; }
        public ConfigurableNotificationAreaViewModel NotificationArea { get; }
        public INavigationMediator NavigationMediator => _navigationMediator;
        public INewProjectMediator WorkspaceManagement => _workspaceManagementMediator;

        public ViewAreaCoordinator(
            IViewModelFactory viewModelFactory, 
            INavigationMediator navigationMediator,
            INewProjectMediator workspaceManagementMediator,
            ITestCaseGenerationMediator testCaseGenerationMediator,
            IViewConfigurationService? viewConfigurationService,
            SideMenuViewModel sideMenuViewModel)
        {
            System.Diagnostics.Debug.WriteLine("*** ViewAreaCoordinator: Constructor called! ***");
            
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            _viewConfigurationService = viewConfigurationService; // Allow null initially
            _workspaceManagementMediator = workspaceManagementMediator ?? throw new ArgumentNullException(nameof(workspaceManagementMediator));
            
            System.Diagnostics.Debug.WriteLine("*** ViewAreaCoordinator: Creating workspace ViewModels ***");
            
            // Use dependency-injected SideMenuViewModel
            SideMenu = sideMenuViewModel ?? throw new ArgumentNullException(nameof(sideMenuViewModel));
            TitleArea = new ConfigurableTitleAreaViewModel(navigationMediator);
            HeaderArea = new ConfigurableHeaderAreaViewModel(navigationMediator);
            WorkspaceContent = new ConfigurableContentAreaViewModel(navigationMediator);
            NavigationArea = new ConfigurableNavigationAreaViewModel(navigationMediator);
            NotificationArea = new ConfigurableNotificationAreaViewModel(navigationMediator);

            // System.Diagnostics.Debug.WriteLine("*** ViewAreaCoordinator: About to subscribe to SectionChangeRequested ***");
            
            // Subscribe to navigation requests
            _navigationMediator.Subscribe<NavigationEvents.SectionChangeRequested>(OnSectionChangeRequested);
            
            // System.Diagnostics.Debug.WriteLine("*** ViewAreaCoordinator: Successfully subscribed to SectionChangeRequested ***");
            
            // NOTE: Removed SideMenu.SectionChanged subscription to prevent circular calls
            // The flow should be: SideMenu → NavigateToSection → SectionChangeRequested → OnSectionChangeRequested
            // NOT: SideMenu → SelectedSection → SectionChanged → NavigateToSection (circular!)
            
            // Note: Initial configuration is now handled by NavigationService.Initialize() 
            // which explicitly sets "startup" configuration. No auto-default needed.
        }

        private void OnSectionChangeRequested(NavigationEvents.SectionChangeRequested request)
        {
            // Console.WriteLine($"*** ViewAreaCoordinator: OnSectionChangeRequested called with '{request.SectionName}' ***");
            
            // Write to log file for easier debugging
            // System.IO.File.AppendAllText(@"c:\temp\navigation-debug.log", 
            //     $"[{DateTime.Now:HH:mm:ss}] ViewAreaCoordinator: OnSectionChangeRequested('{request.SectionName}')\n");
                
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewAreaCoordinator] Section change requested: '{request.SectionName}' - delegating to configuration service");
            
            try
            {
                // Lazy-load ViewConfigurationService to avoid circular dependency
                if (_viewConfigurationService == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ViewAreaCoordinator] ViewConfigurationService is null, attempting to resolve from DI...");
                    _viewConfigurationService = App.ServiceProvider?.GetService<IViewConfigurationService>();
                    if (_viewConfigurationService == null)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug("[ViewAreaCoordinator] FAILED to resolve ViewConfigurationService from DI!");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug("[ViewAreaCoordinator] ViewConfigurationService resolved successfully from DI");
                    }
                }
                
                if (_viewConfigurationService != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewAreaCoordinator] Calling ApplyConfiguration for: {request.SectionName}");
                    // Delegate to the configuration service to create configuration
                    _viewConfigurationService.ApplyConfiguration(request.SectionName, request.Context);
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewAreaCoordinator] ApplyConfiguration completed for: {request.SectionName}");
                    
                    // Now publish the configuration using our navigation mediator
                    var configuration = _viewConfigurationService.CurrentConfiguration;
                    if (configuration != null)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewAreaCoordinator] Publishing configuration for: {configuration.SectionName} with content: {configuration.ContentViewModel?.GetType().Name}");
                        _navigationMediator.Publish(new ViewConfigurationEvents.ApplyViewConfiguration(configuration));
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewAreaCoordinator] Configuration applied and published for: {request.SectionName}");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewAreaCoordinator] WARNING: CurrentConfiguration is NULL after ApplyConfiguration for: {request.SectionName}");
                    }
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[ViewAreaCoordinator] ViewConfigurationService not available for: {request.SectionName}");
                }
                
                // Update side menu selection
                SideMenu.SelectedSection = request.SectionName;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ViewAreaCoordinator] Error applying configuration for {request.SectionName}: {ex.Message}");
                // Fallback: at least update side menu
                SideMenu.SelectedSection = request.SectionName;
            }
        }
    }
}
