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
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using System.Runtime.CompilerServices;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// MainViewModel serves as a simple container that sets up the 5 workspace areas.
    /// Following architectural guidelines: NO coordination logic, just workspace setup.
    /// All domain logic is delegated to appropriate domain mediators and ViewModels.
    /// 
    /// Responsibilities:
    /// 1. Set up 5 workspace areas (MainWorkspace, HeaderWorkspace, NotificationWorkspace, NavigationWorkspace, SideMenuWorkspace)
    /// 2. Initialize ViewAreaCoordinator
    /// 3. Provide property bindings for UI
    /// 4. NO coordination logic - once workspace assigned ? hands-off
    /// 5. Simple dynamic title updates
    /// </summary>
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        // === SIMPLE CONTAINER FIELDS ===
        // MainViewModel should only manage 4 workspace areas, no coordination logic
        private readonly IViewAreaCoordinator _viewAreaCoordinator;
        private readonly IViewModelFactory _viewModelFactory;
        private readonly ILogger<MainViewModel>? _logger;
        private readonly INavigationService _navigationService;
        private string _displayName = "Systems ATE APP";
        
        [ObservableProperty]
        private object? modalViewModel; // Observable property for modal visibility
        
        [ObservableProperty]
        private string modalTitle = string.Empty;
        
        // === 6-WORKSPACE PROPERTIES ===
        // All UI areas are managed by ViewAreaCoordinator
        
        /// <summary>
        /// Navigation mediator access for UI binding
        /// </summary>
        public INavigationMediator NavigationMediator => _viewAreaCoordinator.NavigationMediator;
        
        /// <summary>
        /// Title workspace area
        /// </summary>
        public object? TitleWorkspace => _viewAreaCoordinator?.TitleArea?.ActiveTitle;
        
        /// <summary>
        /// Main content workspace area
        /// </summary>
        public object? MainWorkspace => _viewAreaCoordinator?.WorkspaceContent?.CurrentContent;
        
        /// <summary>
        /// Header workspace area
        /// </summary>
        public object? HeaderWorkspace => _viewAreaCoordinator?.HeaderArea?.ActiveHeader;
        
        /// <summary>
        /// Notification workspace area - status indicators below header
        /// </summary>
        public object? NotificationWorkspace => _viewAreaCoordinator?.NotificationArea?.CurrentNotification;
        
        /// <summary>
        /// Navigation workspace area
        /// </summary>
        public object? NavigationWorkspace => _viewAreaCoordinator.NavigationArea.CurrentContent;
        
        /// <summary>
        /// Side menu workspace area
        /// </summary>
        public object? SideMenuWorkspace => _viewAreaCoordinator.SideMenu;
        
        /// <summary>
        /// Dynamic title for the application window
        /// </summary>
        public string DisplayName 
        { 
            get {
                System.Diagnostics.Debug.WriteLine($"*** MainViewModel[{GetHashCode()}]: DisplayName getter called, returning '{_displayName}' ***");
                return _displayName;
            }
            private set => SetProperty(ref _displayName, value); 
        }

        /// <summary>
        /// Simple container constructor - sets up 5 workspace areas with NO coordination logic.
        /// According to architectural guidelines: MainViewModel should be a simple container that 
        /// sets up 5 workspace areas. Once workspace assigned ? hands-off.
        /// </summary>
        public MainViewModel(IViewModelFactory viewModelFactory, INavigationService navigationService, ILogger<MainViewModel>? logger = null)
        {
            System.Diagnostics.Debug.WriteLine($"*** MainViewModel constructor called! Instance: {GetHashCode()} ***");
            _viewModelFactory = viewModelFactory ?? throw new ArgumentNullException(nameof(viewModelFactory));
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _logger = logger;
            
            // Initialize modal command - ensure modal starts hidden
            CloseModalCommand = new RelayCommand(CloseModal);
            ModalViewModel = null; // Explicitly set to null to hide modal
            ModalTitle = string.Empty;
            
            // Simple title binding - NavigationService handles all title logic
            _navigationService.TitleChanged += (_, title) => {
                System.Diagnostics.Debug.WriteLine($"*** MainViewModel[{GetHashCode()}]: NavigationService title changed to '{title}' ***");
                _logger?.LogInformation("MainViewModel: NavigationService title changed to '{Title}'", title);
                
                // Ensure PropertyChanged is fired on UI thread for proper WPF binding
                System.Windows.Application.Current?.Dispatcher.Invoke(() => {
                    _displayName = title;
                    System.Diagnostics.Debug.WriteLine($"*** MainViewModel[{GetHashCode()}]: _displayName field set to '{_displayName}' on UI thread ***");
                    OnPropertyChanged(nameof(DisplayName));
                    System.Diagnostics.Debug.WriteLine($"*** MainViewModel[{GetHashCode()}]: PropertyChanged fired on UI thread for DisplayName ***");
                });
                _logger?.LogInformation("MainViewModel: DisplayName updated to '{DisplayName}'", DisplayName);
            };
            
            // Initialize unified navigation system - this is the ONLY responsibility
            _viewAreaCoordinator = _viewModelFactory.CreateViewAreaCoordinator();
            
            // Initialize NavigationService with coordinator for proper title management
            _navigationService.Initialize(_viewAreaCoordinator);
            
            // Subscribe to navigation events for UI property binding notifications ONLY
            _viewAreaCoordinator.NavigationMediator.Subscribe<NavigationEvents.HeaderChanged>(
                e => OnPropertyChanged(nameof(HeaderWorkspace)));
            _viewAreaCoordinator.NavigationMediator.Subscribe<NavigationEvents.ContentChanged>(
                e => OnPropertyChanged(nameof(MainWorkspace)));
            _viewAreaCoordinator.NavigationMediator.Subscribe<NavigationEvents.SectionChanged>(e => {
                System.Diagnostics.Debug.WriteLine($"*** MainViewModel: SectionChanged event received! PreviousSection='{e.PreviousSection}', NewSection='{e.NewSection}' ***");
                _logger?.LogInformation("MainViewModel: Section changed from '{PreviousSection}' to '{NewSection}'", e.PreviousSection, e.NewSection);
                OnPropertyChanged(nameof(NavigationWorkspace));
                OnPropertyChanged(nameof(NotificationWorkspace));
            });
            
            // Subscribe to configurable ViewModel property changes
            if (_viewAreaCoordinator.TitleArea != null)
            {
                _viewAreaCoordinator.TitleArea.PropertyChanged += (_, e) => {
                    if (e.PropertyName == nameof(_viewAreaCoordinator.TitleArea.ActiveTitle))
                        OnPropertyChanged(nameof(TitleWorkspace));
                };
            }
            
            if (_viewAreaCoordinator.HeaderArea != null)
            {
                _viewAreaCoordinator.HeaderArea.PropertyChanged += (_, e) => {
                    if (e.PropertyName == nameof(_viewAreaCoordinator.HeaderArea.ActiveHeader))
                        OnPropertyChanged(nameof(HeaderWorkspace));
                };
            }
            if (_viewAreaCoordinator.WorkspaceContent != null)
            {
                _viewAreaCoordinator.WorkspaceContent.PropertyChanged += (_, e) => {
                    if (e.PropertyName == nameof(_viewAreaCoordinator.WorkspaceContent.CurrentContent))
                        OnPropertyChanged(nameof(MainWorkspace));
                };
            }
            if (_viewAreaCoordinator.NotificationArea != null)
            {
                _viewAreaCoordinator.NotificationArea.PropertyChanged += (_, e) => {
                    if (e.PropertyName == nameof(_viewAreaCoordinator.NotificationArea.CurrentNotification))
                        OnPropertyChanged(nameof(NotificationWorkspace));
                };
            }
            
            if (_viewAreaCoordinator.NavigationArea != null)
            {
                _viewAreaCoordinator.NavigationArea.PropertyChanged += (_, e) => {
                    if (e.PropertyName == nameof(_viewAreaCoordinator.NavigationArea.CurrentContent))
                        OnPropertyChanged(nameof(NavigationWorkspace));
                };
            }
            
            _logger?.LogInformation("MainViewModel initialized as simple 5-workspace container");
        }

        /// <summary>
        /// Access to ViewAreaCoordinator for components that need unified navigation
        /// </summary>
        public IViewAreaCoordinator ViewAreaCoordinator => _viewAreaCoordinator;

        // === UI-BOUND PROPERTIES ===
        // Properties needed for UI data binding - kept minimal and focused
        
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
        private System.Collections.ObjectModel.ObservableCollection<object> toastNotifications = new();

        // === LEGACY PROPERTIES (TO BE REMOVED) ===
        // These properties are still actively used and need migration to domain coordination
        // TODO: Migrate these usages to proper domain patterns
        
        public string? CurrentAnythingLLMWorkspaceSlug { get; set; } // Used by LLMServiceManagementViewModel - needs migration
        
        /// <summary>
        /// DEPRECATED: Use domain mediators instead. 
        /// Still used by GeneratedTestCase, ClarifyingQuestionVM, TestCaseGenerator_AssumptionsVM
        /// </summary>
        [Obsolete("Use domain mediators for dirty state management")]
        public bool IsDirty { get; set; } = false;

        // Collections for UI binding
        public System.Collections.ObjectModel.ObservableCollection<object> Requirements { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<object> LooseTables { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<object> LooseParagraphs { get; } = new();
        public System.Collections.ObjectModel.ObservableCollection<object> TestCaseGeneratorSteps { get; } = new();

        // === MODAL COMMAND IMPLEMENTATION ===
        public ICommand CloseModalCommand { get; private set; }
        
        private void CloseModal()
        {
            ModalViewModel = null;
            ModalTitle = string.Empty;
            _logger?.LogInformation("Modal dialog closed");
        }

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
