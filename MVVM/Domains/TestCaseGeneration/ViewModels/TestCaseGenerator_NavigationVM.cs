using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Models.DataDrivenMenu;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// Navigation ViewModel for TestCaseGeneration domain - refactored to use domain mediator.
    /// Provides requirement navigation functionality without bridge dependencies.
    /// </summary>
    public class TestCaseGenerator_NavigationVM : BaseDomainViewModel, IDisposable
    {
        private new readonly ITestCaseGenerationMediator _mediator;
        private int _selectedRequirementIndex = -1;
        private Requirement? _selectedRequirement;
        private string? _searchQuery;
        private bool _wrapOnNextWithoutTestCase = false;
        private MenuAction? _requirementsDropdown;
        
        // Commands
        private RelayCommand? _previousCommand;
        private RelayCommand? _nextCommand;
        private RelayCommand? _nextWithoutTestCaseCommand;
        private ICommand? _searchCommand;
        
        // Local collections to avoid bridge dependencies
        private readonly ObservableCollection<Requirement> _requirements = new();

        /// <summary>
        /// Constructor for domain mediator injection
        /// </summary>
        public TestCaseGenerator_NavigationVM(ITestCaseGenerationMediator mediator, ILogger<TestCaseGenerator_NavigationVM> logger) 
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            
            // Subscribe to domain events for requirement selection and collection changes
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
            
            // Initialize the dropdown
            InitializeDropdown();
            
            Title = "Requirements Navigation";
            _logger.LogDebug("TestCaseGenerator_NavigationVM created with mediator");
        }

        // ===== PROPERTIES =====
        
        /// <summary>
        /// Requirements dropdown for data-driven menu system
        /// </summary>
        public MenuAction? RequirementsDropdown
        {
            get => _requirementsDropdown;
            private set => SetProperty(ref _requirementsDropdown, value);
        }
        
        /// <summary>
        /// Requirements collection (local copy to avoid bridge dependencies)
        /// </summary>
        public ObservableCollection<Requirement> Requirements => _requirements;

        /// <summary>
        /// Requirements view for UI binding (ComboBox binds to this property)
        /// </summary>
        public ObservableCollection<Requirement> RequirementsView => _mediator.Requirements;

        /// <summary>
        /// Currently selected requirement
        /// </summary>
        public Requirement? SelectedRequirement
        {
            get => _selectedRequirement;
            set
            {
                if (SetProperty(ref _selectedRequirement, value))
                {
                    // Update index using mediator's requirements collection
                    _selectedRequirementIndex = value != null ? _mediator.Requirements.IndexOf(value) : -1;
                    
                    // Notify mediator of selection
                    if (value != null)
                    {                        
                        _mediator.SelectRequirement(value);
                        _logger.LogDebug("Requirement selected: {RequirementId}", value.GlobalId);
                    }
                    
                    // Update UI
                    OnPropertyChanged(nameof(SelectedRequirementIndex));
                    OnPropertyChanged(nameof(RequirementPositionDisplay));
                    RefreshNavCommands();
                }
            }
        }

        /// <summary>
        /// Selected requirement index for UI binding
        /// </summary>
        public int SelectedRequirementIndex
        {
            get => _selectedRequirementIndex;
            set
            {
                if (SetProperty(ref _selectedRequirementIndex, value))
                {
                    // Update selected requirement using mediator's requirements collection
                    if (value >= 0 && value < _mediator.Requirements.Count)
                    {
                        SelectedRequirement = _mediator.Requirements[value];
                    }
                    else
                    {
                        SelectedRequirement = null;
                    }
                }
            }
        }

        /// <summary>
        /// Display text for current position (e.g. "1 / 5")
        /// </summary>
        public string RequirementPositionDisplay
        {
            get
            {
                var total = _mediator.Requirements.Count;
                var pos = (_selectedRequirementIndex >= 0 && total > 0) ? (_selectedRequirementIndex + 1).ToString() : "—";
                return $"{pos} / {total}";
            }
        }

        /// <summary>
        /// Whether to wrap around when finding next requirement without test case
        /// </summary>
        public bool WrapOnNextWithoutTestCase
        {
            get => _wrapOnNextWithoutTestCase;
            set => SetProperty(ref _wrapOnNextWithoutTestCase, value);
        }

        /// <summary>
        /// Search query for requirement filtering
        /// </summary>
        public string? SearchQuery 
        { 
            get => _searchQuery; 
            set => SetProperty(ref _searchQuery, value); 
        }

        // ===== DROPDOWN INITIALIZATION =====

        private void InitializeDropdown()
        {
            RequirementsDropdown = new MenuAction
            {
                Id = "requirements-nav",
                Text = "No requirements loaded",
                Icon = "📋",
                IsDropdown = true,
                IsExpanded = false,
                Children = new ObservableCollection<MenuContentItem>()
            };
            
            UpdateDropdownFromRequirements();
        }

        private void UpdateDropdownFromRequirements()
        {
            if (RequirementsDropdown?.Children == null) return;

            RequirementsDropdown.Children.Clear();

            var requirements = _mediator.Requirements;
            if (requirements?.Any() == true)
            {
                foreach (var req in requirements)
                {
                    RequirementsDropdown.Children.Add(new MenuAction
                    {
                        Id = $"req-{req.GlobalId}",
                        Text = $"{req.Item} — {req.Name}",
                        Icon = "📄",
                        Command = new RelayCommand(() => SelectRequirement(req)),
                        Level = 1
                    });
                }
                
                // Show selected requirement name, or first requirement if none selected
                var selectedReq = _selectedRequirement ?? requirements.FirstOrDefault();
                RequirementsDropdown.Text = selectedReq != null 
                    ? $"{selectedReq.Item} — {selectedReq.Name}"
                    : "No requirement selected";
            }
            else
            {
                RequirementsDropdown.Text = "No requirements loaded";
            }
        }

        private void SelectRequirement(Requirement requirement)
        {
            SelectedRequirement = requirement;
            if (RequirementsDropdown != null)
            {
                RequirementsDropdown.IsExpanded = false;
                // Update dropdown text to show selected requirement
                RequirementsDropdown.Text = $"{requirement.Item} — {requirement.Name}";
            }
        }

        // ===== NAVIGATION COMMANDS =====

        /// <summary>
        /// Command to navigate to previous requirement
        /// </summary>
        public ICommand PreviousRequirementCommand => _previousCommand ??= new RelayCommand(
            () =>
            {
                if (_mediator.Requirements.Count == 0 || _selectedRequirementIndex <= 0) return;
                
                var newIndex = _selectedRequirementIndex - 1;
                SelectedRequirement = _mediator.Requirements[newIndex];
            },
            () => _mediator.Requirements.Count > 0 && _selectedRequirementIndex > 0);

        /// <summary>
        /// Command to navigate to next requirement
        /// </summary>
        public ICommand NextRequirementCommand => _nextCommand ??= new RelayCommand(
            () =>
            {
                if (_mediator.Requirements.Count == 0 || _selectedRequirementIndex >= _mediator.Requirements.Count - 1) return;
                
                var newIndex = _selectedRequirementIndex + 1;
                SelectedRequirement = _mediator.Requirements[newIndex];
            },
            () => _mediator.Requirements.Count > 0 && _selectedRequirementIndex < _mediator.Requirements.Count - 1);

        /// <summary>
        /// Command to navigate to next requirement without test cases
        /// </summary>
        public ICommand NextWithoutTestCaseCommand => _nextWithoutTestCaseCommand ??= new RelayCommand(
            () =>
            {
                if (_mediator.Requirements.Count == 0) return;
                
                var startIndex = _selectedRequirementIndex >= 0 ? _selectedRequirementIndex + 1 : 0;
                
                // Find next requirement without test cases
                for (int i = 0; i < _mediator.Requirements.Count; i++)
                {
                    var index = (startIndex + i) % _mediator.Requirements.Count;
                    var requirement = _mediator.Requirements[index];
                    
                    // Check if requirement has test cases
                    if (requirement.GeneratedTestCases == null || !requirement.GeneratedTestCases.Any())
                    {
                        SelectedRequirement = requirement;
                        break;
                    }
                    
                    // If wrap is disabled and we've gone through all, stop
                    if (!WrapOnNextWithoutTestCase && index < startIndex && i > 0)
                        break;
                }
            },
            () => _mediator.Requirements.Count > 0);

        /// <summary>
        /// Command to search for requirements
        /// </summary>
        public ICommand SearchCommand => _searchCommand ??= new RelayCommand(ExecuteSearch);

        // ===== PRIVATE METHODS =====

        /// <summary>
        /// Execute search functionality
        /// </summary>
        private void ExecuteSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;
            
            var query = SearchQuery.Trim();
            var found = _requirements.FirstOrDefault(r =>
                (!string.IsNullOrEmpty(r.Name) && r.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(r.Item) && r.Item.Contains(query, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(r.GlobalId) && r.GlobalId.Contains(query, StringComparison.OrdinalIgnoreCase)));
                
            if (found != null)
            {
                SelectedRequirement = found;
            }
        }

        /// <summary>
        /// Refresh command CanExecute states
        /// </summary>
        private void RefreshNavCommands()
        {
            (_previousCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (_nextCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (_nextWithoutTestCaseCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }

        // ===== EVENT HANDLERS =====

        /// <summary>
        /// Handle requirement selection events from domain mediator
        /// </summary>
        private void OnRequirementSelected(TestCaseGenerationEvents.RequirementSelected e)
        {
            if (!ReferenceEquals(_selectedRequirement, e.Requirement))
            {
                _selectedRequirement = e.Requirement;
                _selectedRequirementIndex = _requirements.IndexOf(e.Requirement);
                
                // Update dropdown text to show selected requirement
                if (RequirementsDropdown != null && e.Requirement != null)
                {
                    RequirementsDropdown.Text = $"{e.Requirement.Item} — {e.Requirement.Name}";
                }
                
                OnPropertyChanged(nameof(SelectedRequirement));
                OnPropertyChanged(nameof(SelectedRequirementIndex));
                OnPropertyChanged(nameof(RequirementPositionDisplay));
                RefreshNavCommands();
                
                _logger.LogDebug("Navigation updated for requirement: {RequirementId}", e.Requirement?.GlobalId ?? "null");
            }
        }

        /// <summary>
        /// Handle requirements collection changes from domain mediator
        /// </summary>
        private void OnRequirementsCollectionChanged(TestCaseGenerationEvents.RequirementsCollectionChanged e)
        {
            _logger.LogDebug("Requirements collection changed: {Action}, Count: {Count}", e.Action, e.NewCount);
            
            // If collection was cleared, clear selection
            if (e.Action == "Clear" || e.NewCount == 0)
            {
                _selectedRequirement = null;
                _selectedRequirementIndex = -1;
            }
            
            // Update the dropdown with new requirements
            UpdateDropdownFromRequirements();
            
            // Notify UI that RequirementsView has updated (it's bound to mediator's Requirements collection)
            OnPropertyChanged(nameof(RequirementsView));
            OnPropertyChanged(nameof(Requirements));
            OnPropertyChanged(nameof(SelectedRequirement));
            OnPropertyChanged(nameof(SelectedRequirementIndex));
            OnPropertyChanged(nameof(RequirementPositionDisplay));
            RefreshNavCommands();
        }

        /// <summary>
        /// Dispose pattern implementation
        /// </summary>
        public new void Dispose()
        {
            // Unsubscribe from mediator events
            try
            {
                _mediator.Unsubscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);
                _mediator.Unsubscribe<TestCaseGenerationEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error unsubscribing from mediator events during dispose");
            }
            
            base.Dispose();
        }

        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====
        
        /// <summary>
        /// Navigation ViewModels typically don't save data directly
        /// </summary>
        protected override bool CanSave() => false;
        
        /// <summary>
        /// Navigation ViewModels typically don't save data directly
        /// </summary>
        protected override async Task SaveAsync()
        {
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// Navigation ViewModels can be refreshed to reload requirements
        /// </summary>
        protected override bool CanRefresh() => true;
        
        /// <summary>
        /// Refresh requirements from mediator
        /// </summary>
        protected override async Task RefreshAsync()
        {
            _logger.LogDebug("Refreshing requirements navigation");
            // TODO: Implement requirements refresh when mediator supports it
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// Navigation ViewModels typically don't have cancellable operations
        /// </summary>
        protected override bool CanCancel() => false;
        
        /// <summary>
        /// Navigation ViewModels typically don't have cancellable operations
        /// </summary>
        protected override void Cancel()
        {
            // No-op for navigation VM
        }
    }
}