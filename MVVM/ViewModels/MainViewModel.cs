using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// MainViewModel serves as a simple container that sets up the 4 workspace areas.
    /// Following architectural guidelines: NO coordination logic, just workspace setup.
    /// All domain logic is delegated to appropriate domain mediators and ViewModels.
    /// 
    /// TEMPORARY: Also implements ITestCaseGenerator_Navigator as bridge during migration.
    /// 
    /// Responsibilities:
    /// 1. Set up 4 workspace areas (MainWorkspace, HeaderWorkspace, NavigationWorkspace, SideMenuWorkspace)
    /// 2. Initialize ViewAreaCoordinator
    /// 3. Provide property bindings for UI
    /// 4. NO coordination logic - once workspace assigned → hands-off
    /// 5. TEMPORARY: Bridge ITestCaseGenerator_Navigator interface until domain migration complete
    /// </summary>
    public partial class MainViewModel : ObservableObject, ITestCaseGenerator_Navigator, IDisposable
    {
        // === SIMPLE CONTAINER FIELDS ===
        // MainViewModel should only manage 4 workspace areas, no coordination logic
        private readonly IViewAreaCoordinator _viewAreaCoordinator;
        private readonly IViewModelFactory _viewModelFactory;
        private readonly ILogger<MainViewModel>? _logger;
        
        // TEMPORARY bridge fields - will be removed after domain migration
        public static bool _anythingLLMInitializing; // Made static for cross-instance coordination
        
        /// <summary>
        /// TEMPORARY: Bridge property for AnythingLLM initialization status.
        /// TODO: Remove after migrating to LLM service mediator.
        /// </summary>
        public bool AnythingLLMInitializing 
        { 
            get => _anythingLLMInitializing;
            set => SetProperty(ref _anythingLLMInitializing, value);
        }
        
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
        public TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_QuestionsVM? QuestionsViewModel 
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
        public TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_AssumptionsVM? AssumptionsViewModel 
        { 
            get 
            {
                _logger?.LogDebug("DEPRECATED: AssumptionsViewModel accessed - should use TestCaseGeneration mediator");
                return null; // Return null to avoid NullReferenceExceptions  
            }
        }
        
        // ============================================================================
        // ITESTECASE_GENERATOR_NAVIGATOR INTERFACE BRIDGE IMPLEMENTATION
        // Temporary implementation during architectural migration to domain mediators.
        // These will be removed once TestCaseGeneration domain is fully implemented.
        // ============================================================================
        
        /// <summary>
        /// TEMPORARY: Bridge collection for Requirements access.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public ObservableCollection<Requirement> Requirements { get; } = new();
        
        private Requirement? _currentRequirement;
        /// <summary>
        /// TEMPORARY: Bridge property for CurrentRequirement access.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public Requirement? CurrentRequirement 
        { 
            get => _currentRequirement;
            set 
            {
                if (SetProperty(ref _currentRequirement, value))
                {
                    OnPropertyChanged(nameof(RequirementPositionDisplay));
                    (_nextRequirementCommand as RelayCommand)?.NotifyCanExecuteChanged();
                    (_previousRequirementCommand as RelayCommand)?.NotifyCanExecuteChanged();
                    (_nextWithoutTestCaseCommand as RelayCommand)?.NotifyCanExecuteChanged();
                }
            }
        }
        
        /// <summary>
        /// TEMPORARY: Bridge property for TestCaseGenerator workflow steps.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public ObservableCollection<TestCaseEditorApp.MVVM.Models.StepDescriptor> TestCaseGeneratorSteps { get; } = new();
        
        // Navigation Commands - TEMPORARY bridge implementation
        private RelayCommand? _nextRequirementCommand;
        private RelayCommand? _previousRequirementCommand;
        private RelayCommand? _nextWithoutTestCaseCommand;
        
        /// <summary>
        /// TEMPORARY: Bridge command for next requirement navigation.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public ICommand? NextRequirementCommand => _nextRequirementCommand ??= new RelayCommand(
            () => {
                _logger?.LogDebug("BRIDGE: NextRequirementCommand executed");
                // TODO: Delegate to TestCaseGeneration mediator
            },
            () => Requirements.Count > 0 && CurrentRequirement != Requirements.LastOrDefault());
        
        /// <summary>
        /// TEMPORARY: Bridge command for previous requirement navigation.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public ICommand? PreviousRequirementCommand => _previousRequirementCommand ??= new RelayCommand(
            () => {
                _logger?.LogDebug("BRIDGE: PreviousRequirementCommand executed");
                // TODO: Delegate to TestCaseGeneration mediator
            },
            () => Requirements.Count > 0 && CurrentRequirement != Requirements.FirstOrDefault());
        
        /// <summary>
        /// TEMPORARY: Bridge command for next without test case navigation.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public ICommand? NextWithoutTestCaseCommand => _nextWithoutTestCaseCommand ??= new RelayCommand(
            () => {
                _logger?.LogDebug("BRIDGE: NextWithoutTestCaseCommand executed");
                // TODO: Delegate to TestCaseGeneration mediator
            },
            () => Requirements.Count > 0);
        
        /// <summary>
        /// TEMPORARY: Bridge property for requirement position display.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public string RequirementPositionDisplay
        {
            get
            {
                if (Requirements.Count == 0) return "0 of 0";
                var index = CurrentRequirement != null ? Requirements.IndexOf(CurrentRequirement) : -1;
                return $"{(index >= 0 ? index + 1 : 0)} of {Requirements.Count}";
            }
        }
        
        private bool _wrapOnNextWithoutTestCase;
        /// <summary>
        /// TEMPORARY: Bridge property for wrap on next without test case.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public bool WrapOnNextWithoutTestCase 
        { 
            get => _wrapOnNextWithoutTestCase;
            set => SetProperty(ref _wrapOnNextWithoutTestCase, value);
        }
        
        private bool _isLlmBusy;
        /// <summary>
        /// TEMPORARY: Bridge property for LLM busy state.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public bool IsLlmBusy 
        { 
            get => _isLlmBusy;
            set => SetProperty(ref _isLlmBusy, value);
        }
        
        private bool _isBatchAnalyzing;
        /// <summary>
        /// TEMPORARY: Bridge property for batch analyzing state.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public bool IsBatchAnalyzing 
        { 
            get => _isBatchAnalyzing;
            set => SetProperty(ref _isBatchAnalyzing, value);
        }
        
        /// <summary>
        /// TEMPORARY: Bridge method for requirement editor.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public void ShowRequirementEditor(Requirement requirement)
        {
            _logger?.LogDebug("BRIDGE: ShowRequirementEditor called for requirement");
            // TODO: Delegate to TestCaseGeneration mediator
        }
        
        private string _workspacePath = string.Empty;
        /// <summary>
        /// TEMPORARY: Bridge property for workspace path access.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public string WorkspacePath 
        { 
            get => _workspacePath;
            set 
            {
                if (SetProperty(ref _workspacePath, value))
                {
                    _logger?.LogDebug("BRIDGE: WorkspacePath changed to {Path}", value);
                    // TODO: Delegate to WorkspaceManagement mediator
                }
            }
        }
        
        private object? _currentWorkspace;
        /// <summary>
        /// TEMPORARY: Bridge property for current workspace access.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public dynamic? CurrentWorkspace 
        { 
            get => _currentWorkspace;
            set 
            {
                if (SetProperty(ref _currentWorkspace, value))
                {
                    _logger?.LogDebug("BRIDGE: CurrentWorkspace changed");
                    // TODO: Delegate to WorkspaceManagement mediator
                }
            }
        }
        
        /// <summary>
        /// TEMPORARY: Bridge method for committing pending edits.
        /// TODO: Remove after migrating to appropriate domain mediator.
        /// </summary>
        public void CommitPendingEdits()
        {
            _logger?.LogDebug("BRIDGE: CommitPendingEdits called");
            // TODO: Delegate to appropriate domain mediator
        }
        
        private object? _currentStepViewModel;
        /// <summary>
        /// TEMPORARY: Bridge property for current step view model access.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public object? CurrentStepViewModel 
        { 
            get => _currentStepViewModel;
            set 
            {
                if (SetProperty(ref _currentStepViewModel, value))
                {
                    _logger?.LogDebug("BRIDGE: CurrentStepViewModel changed");
                    // TODO: Delegate to TestCaseGeneration mediator
                }
            }
        }
        
        private string _wordFilePath = string.Empty;
        /// <summary>
        /// TEMPORARY: Bridge property for Word file path access.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public string WordFilePath 
        { 
            get => _wordFilePath;
            set 
            {
                if (SetProperty(ref _wordFilePath, value))
                {
                    _logger?.LogDebug("BRIDGE: WordFilePath changed to {Path}", value);
                    // TODO: Delegate to WorkspaceManagement mediator
                }
            }
        }
        
        /// <summary>
        /// TEMPORARY: Bridge property for loose tables collection.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public System.Collections.IList? LooseTables { get; } = new System.Collections.Generic.List<object>();
        
        /// <summary>
        /// TEMPORARY: Bridge property for loose paragraphs collection.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public System.Collections.IList? LooseParagraphs { get; } = new System.Collections.Generic.List<object>();
        
        private string _displayName = string.Empty;
        /// <summary>
        /// TEMPORARY: Bridge property for display name.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public string DisplayName 
        { 
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }
        
        private string _sapStatus = string.Empty;
        /// <summary>
        /// TEMPORARY: Bridge property for SAP status.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public string SapStatus 
        { 
            get => _sapStatus;
            set => SetProperty(ref _sapStatus, value);
        }
        
        private object? _activeHeader;
        /// <summary>
        /// TEMPORARY: Bridge property for active header access.
        /// TODO: Remove after migrating to Navigation mediator.
        /// </summary>
        public object? ActiveHeader 
        { 
            get => _activeHeader;
            set 
            {
                if (SetProperty(ref _activeHeader, value))
                {
                    _logger?.LogDebug("BRIDGE: ActiveHeader changed");
                    // TODO: Delegate to Navigation mediator
                }
            }
        }
        
        private object? _testCaseGeneratorHeader;
        /// <summary>
        /// TEMPORARY: Bridge property for test case generator header access.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public object? TestCaseGeneratorHeader 
        { 
            get => _testCaseGeneratorHeader;
            set 
            {
                if (SetProperty(ref _testCaseGeneratorHeader, value))
                {
                    _logger?.LogDebug("BRIDGE: TestCaseGeneratorHeader changed");
                    // TODO: Delegate to TestCaseGeneration mediator
                }
            }
        }
        
        private string _currentAnythingLLMWorkspaceSlug = string.Empty;
        /// <summary>
        /// TEMPORARY: Bridge property for AnythingLLM workspace slug.
        /// TODO: Remove after migrating to LLM domain mediator.
        /// </summary>
        public string CurrentAnythingLLMWorkspaceSlug 
        { 
            get => _currentAnythingLLMWorkspaceSlug;
            set 
            {
                if (SetProperty(ref _currentAnythingLLMWorkspaceSlug, value))
                {
                    _logger?.LogDebug("BRIDGE: CurrentAnythingLLMWorkspaceSlug changed to {Slug}", value);
                    // TODO: Delegate to LLM domain mediator
                }
            }
        }
        
        private object? _newProjectWorkflow;
        /// <summary>
        /// TEMPORARY: Bridge property for new project workflow.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public object? NewProjectWorkflow 
        { 
            get => _newProjectWorkflow;
            set 
            {
                if (SetProperty(ref _newProjectWorkflow, value))
                {
                    _logger?.LogDebug("BRIDGE: NewProjectWorkflow changed");
                    // TODO: Delegate to WorkspaceManagement mediator
                }
            }
        }
        
        /// <summary>
        /// TEMPORARY: Bridge method for getting test case generator instance.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public object? GetTestCaseGeneratorInstance()
        {
            _logger?.LogDebug("BRIDGE: GetTestCaseGeneratorInstance called");
            // TODO: Delegate to TestCaseGeneration mediator
            return null;
        }
        
        /// <summary>
        /// TEMPORARY: Bridge method for workspace selection handling.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public void OnWorkspaceSelected(object sender, object e)
        {
            _logger?.LogDebug("BRIDGE: OnWorkspaceSelected called");
            // TODO: Delegate to WorkspaceManagement mediator
        }
        
        /// <summary>
        /// TEMPORARY: Bridge method for import workflow completion.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public void OnImportWorkflowCompleted(object sender, object e)
        {
            _logger?.LogDebug("BRIDGE: OnImportWorkflowCompleted called");
            // TODO: Delegate to WorkspaceManagement mediator
        }
        
        /// <summary>
        /// TEMPORARY: Bridge method for import workflow cancellation.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public void OnImportWorkflowCancelled(object sender, object e)
        {
            _logger?.LogDebug("BRIDGE: OnImportWorkflowCancelled called");
            // TODO: Delegate to WorkspaceManagement mediator
        }
        
        /// <summary>
        /// TEMPORARY: Bridge method for requirement editing.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public void OnRequirementEdited(object sender, object e)
        {
            _logger?.LogDebug("BRIDGE: OnRequirementEdited called");
            // TODO: Delegate to TestCaseGeneration mediator
        }
        
        /// <summary>
        /// TEMPORARY: Bridge method for requirement analysis request.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public void OnRequirementAnalysisRequested(object sender, object e)
        {
            _logger?.LogDebug("BRIDGE: OnRequirementAnalysisRequested called");
            // TODO: Delegate to TestCaseGeneration mediator
        }
        
        /// <summary>
        /// TEMPORARY: Bridge method for workspace selection modal.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public void ShowWorkspaceSelectionModalForOpen()
        {
            _logger?.LogDebug("BRIDGE: ShowWorkspaceSelectionModalForOpen called");
            // TODO: Delegate to WorkspaceManagement mediator
        }
        
        /// <summary>
        /// TEMPORARY: Bridge method for saving workspace.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public void SaveWorkspace()
        {
            _logger?.LogDebug("BRIDGE: SaveWorkspace called");
            // TODO: Delegate to WorkspaceManagement mediator
        }
        
        private object? _selectedMenuSection;
        /// <summary>
        /// TEMPORARY: Bridge property for selected menu section.
        /// TODO: Remove after migrating to navigation mediator.
        /// </summary>
        public object? SelectedMenuSection 
        { 
            get => _selectedMenuSection;
            set => SetProperty(ref _selectedMenuSection, value);
        }
        
        private object? _selectedStep;
        /// <summary>
        /// TEMPORARY: Bridge property for selected step.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public object? SelectedStep 
        { 
            get => _selectedStep;
            set => SetProperty(ref _selectedStep, value);
        }
        
        private bool _hasUnsavedChanges;
        /// <summary>
        /// TEMPORARY: Bridge property for unsaved changes tracking.
        /// TODO: Remove after migrating to WorkspaceManagement mediator.
        /// </summary>
        public bool HasUnsavedChanges 
        { 
            get => _hasUnsavedChanges;
            set => SetProperty(ref _hasUnsavedChanges, value);
        }
        
        /// <summary>
        /// TEMPORARY: Bridge method for requirements collection changed event.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public void RequirementsOnCollectionChanged(object sender, object e)
        {
            _logger?.LogDebug("BRIDGE: RequirementsOnCollectionChanged called");
            // TODO: Delegate to TestCaseGeneration mediator
        }
        
        private bool _autoAnalyzeOnImport;
        /// <summary>
        /// TEMPORARY: Bridge property for auto analyze on import setting.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public bool AutoAnalyzeOnImport 
        { 
            get => _autoAnalyzeOnImport;
            set => SetProperty(ref _autoAnalyzeOnImport, value);
        }
        
        private bool _autoExportForChatGpt;
        /// <summary>
        /// TEMPORARY: Bridge property for auto export for ChatGPT setting.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public bool AutoExportForChatGpt 
        { 
            get => _autoExportForChatGpt;
            set => SetProperty(ref _autoExportForChatGpt, value);
        }
        
        /// <summary>
        /// TEMPORARY: Bridge property for requirements navigator.
        /// TODO: Remove after migrating to TestCaseGeneration mediator.
        /// </summary>
        public dynamic? RequirementsNavigator => this; // Return self since MainViewModel implements ITestCaseGenerator_Navigator
        
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
