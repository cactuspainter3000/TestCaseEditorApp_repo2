using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Models.DataDrivenMenu;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators;
using System.Runtime.CompilerServices;

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
        /// Exposes the data-driven Test Case Generator menu section for UI binding
        /// </summary>
        public MenuSection? TestCaseGeneratorMenuSection => 
            (_viewAreaCoordinator.SideMenu as ViewModels.SideMenuViewModel)?.TestCaseGeneratorMenuSection;

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
            
            // Initialize the requirements navigator for UI binding
            RequirementsNavigator = _viewModelFactory.CreateRequirementsNavigationViewModel();
            
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

        // === UI-BOUND PROPERTIES ===
        // Properties needed for UI data binding - kept minimal and focused
        
        public string DisplayName { get; set; } = "Test Case Editor";
        public string SelectedMenuSection { get; set; } = "Requirements";
        
        // === MISSING PROPERTIES FOR UI BINDING ===
        // These properties are bound to in XAML but were missing from the ViewModel
        
        [ObservableProperty]
        private string? workspacePath;
        
        [ObservableProperty]
        private object? activeHeader;
        
        [ObservableProperty]
        private bool isLlmBusy;
        
        [ObservableProperty]
        private object? requirementsNavigator;
        
        [ObservableProperty]
        private System.Collections.ObjectModel.ObservableCollection<object> toastNotifications = new();

        // === LEGACY PROPERTIES (TO BE REMOVED) ===
        // These properties are still actively used and need migration to domain coordination
        // TODO: Migrate these usages to proper domain patterns
        
        public static Requirement? CurrentRequirement { get; set; } // Used by TestCaseGenerator_QuestionsVM
        public bool IsDirty { get; set; } = false; // Used by multiple Generator ViewModels
        public bool IsBatchAnalyzing { get; set; } = false; // Used by TestCaseGenerator_QuestionsVM
        public string? CurrentAnythingLLMWorkspaceSlug { get; set; } // Used by LLMServiceManagementViewModel
        public object? SelectedStep { get; set; } // Used by TestCaseGenerator_QuestionsVM
        public object? CurrentStepViewModel { get; set; } // Used by TestCaseGenerator_QuestionsVM

        // Collections for UI binding
        public System.Collections.ObjectModel.ObservableCollection<object> Requirements { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<object> LooseTables { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<object> LooseParagraphs { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<object> TestCaseGeneratorSteps { get; } = new();

        // Modal properties - ensure no modal is shown on startup
        public object? ModalViewModel => null; // Always null to hide modal
        public string ModalTitle => string.Empty; // Empty to avoid fallback text
        public System.Windows.Input.ICommand? CloseModalCommand => null; // No command needed since modal never shows

        // === SIMPLE CONTAINER METHODS ===
        // MainViewModel keeps only essential functionality as a workspace container
        
        /// <summary>
        /// Dispose pattern implementation
        /// </summary>
        public void Dispose()
        {
            // Simple container has minimal cleanup
            _logger?.LogInformation("MainViewModel disposing");
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Simple container pattern - workspace functionality handled by domain ViewModels
        /// </summary>
    }
}