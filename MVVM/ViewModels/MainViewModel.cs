using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;

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

        // === TEMPORARY COMPATIBILITY STUBS ===
        // These properties temporarily satisfy legacy ViewModels during architectural transition
        // TODO: Remove once all ViewModels use proper domain coordination

        public static bool _anythingLLMInitializing { get; set; } = false;
        public static Requirement? CurrentRequirement { get; set; }
        public bool IsDirty { get; set; } = false;
        public bool HasUnsavedChanges { get; set; } = false;
        public bool AutoAnalyzeOnImport { get; set; } = false;
        public bool AutoExportForChatGpt { get; set; } = false;
        public bool IsBatchAnalyzing { get; set; } = false;
        public string? CurrentAnythingLLMWorkspaceSlug { get; set; }
        public string? WorkspacePath { get; set; }
        public object? CurrentWorkspace { get; set; }
        public string? WordFilePath { get; set; }
        public string DisplayName { get; set; } = "Test Case Editor";
        public string SapStatus { get; set; } = string.Empty;
        public string SelectedMenuSection { get; set; } = "Requirements";
        public object? SelectedStep { get; set; }
        public object? CurrentStepViewModel { get; set; }
        
        // Collections
        public System.Collections.ObjectModel.ObservableCollection<object> Requirements { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<object> LooseTables { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<object> LooseParagraphs { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<object> TestCaseGeneratorSteps { get; } = new();

        // Methods
        public void SetTransientStatus(string message, int duration, bool isError = false) 
        { 
            _logger?.LogInformation("SetTransientStatus (stub): {Message}", message);
        }
        
        public void SaveWorkspace() 
        { 
            _logger?.LogInformation("SaveWorkspace (stub): Method called");
        }
        
        public void RequirementsOnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) 
        { 
            _logger?.LogInformation("RequirementsOnCollectionChanged (stub): Method called");
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