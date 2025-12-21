using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// MainViewModel serves as a simple container that sets up the 4 workspace areas.
    /// Following architectural guidelines: NO coordination logic, just workspace setup.
    /// All domain logic is delegated to appropriate domain mediators and ViewModels.
    /// 
    /// Responsibilities:
    /// 1. Set up 4 workspace areas (MainWorkspace, HeaderWorkspace, NavigationWorkspace, SideMenuWorkspace)
    /// 2. Initialize ViewAreaCoordinator
    /// 3. Provide property bindings for UI
    /// 4. NO coordination logic - once workspace assigned → hands-off
    /// </summary>
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        // === SIMPLE CONTAINER FIELDS ===
        // MainViewModel should only manage 4 workspace areas, no coordination logic
        private readonly IViewAreaCoordinator _viewAreaCoordinator;
        private readonly IViewModelFactory _viewModelFactory;
        private readonly ILogger<MainViewModel>? _logger;
        
        // === 4-WORKSPACE PROPERTIES ===
        // All UI areas are managed by ViewAreaCoordinator
        
        /// <summary>
        /// Navigation mediator access for UI binding
        /// </summary>
        public INavigationMediator NavigationMediator => _viewAreaCoordinator.NavigationMediator;
        
        /// <summary>
        /// Main content workspace area
        /// </summary>
        public object? MainWorkspace => _viewAreaCoordinator.WorkspaceContent.CurrentContent;
        
        /// <summary>
        /// Header workspace area
        /// </summary>
        public object? HeaderWorkspace => _viewAreaCoordinator.HeaderArea.ActiveHeader;
        
        /// <summary>
        /// Navigation workspace area
        /// </summary>
        public object? NavigationWorkspace => _viewAreaCoordinator.NavigationMediator.CurrentContent;
        
        /// <summary>
        /// Side menu workspace area
        /// </summary>
        public object? SideMenuWorkspace => _viewAreaCoordinator.SideMenu;

        /// <summary>
        /// Simple container constructor - sets up 4 workspace areas with NO coordination logic.
        /// According to architectural guidelines: MainViewModel should be a simple container that 
        /// sets up 4 workspace areas. Once workspace assigned → hands-off.
        /// </summary>
        public MainViewModel(IViewModelFactory viewModelFactory, ILogger<MainViewModel>? logger = null)
        {
            _viewModelFactory = viewModelFactory ?? throw new ArgumentNullException(nameof(viewModelFactory));
            _logger = logger;
            
            // Initialize unified navigation system - this is the ONLY responsibility
            _viewAreaCoordinator = _viewModelFactory.CreateViewAreaCoordinator();
            
            // Subscribe to navigation events for UI property binding notifications
            _viewAreaCoordinator.NavigationMediator.Subscribe<NavigationEvents.HeaderChanged>(
                e => OnPropertyChanged(nameof(HeaderWorkspace)));
            _viewAreaCoordinator.NavigationMediator.Subscribe<NavigationEvents.ContentChanged>(
                e => OnPropertyChanged(nameof(MainWorkspace)));
            _viewAreaCoordinator.NavigationMediator.Subscribe<NavigationEvents.SectionChanged>(
                e => OnPropertyChanged(nameof(NavigationWorkspace)));
            
            _logger?.LogInformation("MainViewModel initialized as simple 4-workspace container");
        }

        /// <summary>
        /// Access to ViewAreaCoordinator for components that need unified navigation
        /// </summary>
        public IViewAreaCoordinator ViewAreaCoordinator => _viewAreaCoordinator;

        // ============================================================================
        // TEMPORARY BRIDGE METHODS - TO BE REMOVED AFTER DOMAIN MIGRATION
        // These methods provide backwards compatibility while legacy ViewModels are
        // migrated to use proper domain mediators for UI feedback.
        // ============================================================================
        
        /// <summary>
        /// TEMPORARY: Bridge property for legacy ViewModels that need workspace dirty tracking.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public bool IsDirty
        {
            get
            {
                // For now, return false as a safe default.
                // In the future, this should be handled by WorkspaceManagement mediator.
                return false;
            }
            set
            {
                // Log the dirty state change but don't store it in MainViewModel
                _logger?.LogDebug("Workspace dirty state: {IsDirty}", value);
                // TODO: Delegate to WorkspaceManagement mediator
            }
        }
        
        /// <summary>
        /// TEMPORARY: Bridge properties for legacy Views that access TestCaseGeneration ViewModels.
        /// TODO: Remove after migrating to TestCaseGeneration mediator events.
        /// </summary>
        public object? QuestionsViewModel 
        { 
            get 
            {
                _logger?.LogDebug("DEPRECATED: QuestionsViewModel accessed - should use TestCaseGeneration mediator");
                return null; // Return null to avoid NullReferenceExceptions
            }
        }
        
        /// <summary>
        /// TEMPORARY: Bridge properties for legacy Views that access TestCaseGeneration ViewModels.
        /// TODO: Remove after migrating to TestCaseGeneration mediator events.
        /// </summary>
        public object? AssumptionsViewModel 
        { 
            get 
            {
                _logger?.LogDebug("DEPRECATED: AssumptionsViewModel accessed - should use TestCaseGeneration mediator");
                return null; // Return null to avoid NullReferenceExceptions  
            }
        }
        
        /// <summary>
        /// TEMPORARY: Bridge collection for legacy ViewModels that access Requirements.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public ObservableCollection<object> Requirements { get; } = new();
        
        /// <summary>
        /// TEMPORARY: Bridge property for TestCaseGenerator workflow steps.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public ObservableCollection<object> TestCaseGeneratorSteps { get; } = new();
        
        /// <summary>
        /// TEMPORARY: Bridge method for legacy ViewModels that need UI feedback.
        /// Delegates to logging until proper domain mediator migration is complete.
        /// TODO: Remove after migrating legacy ViewModels to domain mediators.
        /// </summary>
        public void SetTransientStatus(string message, int durationSeconds = 3, bool blockingError = false)
        {
            // For now, just log the status message since accessing domain mediators 
            // from MainViewModel violates architectural principles.
            // Legacy ViewModels should be migrated to use domain mediators directly.
            
            var level = blockingError ? LogLevel.Error : LogLevel.Information;
            _logger?.Log(level, "UI Status ({Duration}s): {Message}", durationSeconds, message);
            
            // TODO: Once legacy ViewModels are migrated to domain mediators,
            // remove this bridge method entirely.
        }

        /// <summary>
        /// Dispose pattern implementation
        /// </summary>
        public void Dispose()
        {
            // Simple container has minimal cleanup
            _logger?.LogInformation("MainViewModel disposing");
            GC.SuppressFinalize(this);
        }
    }
}
