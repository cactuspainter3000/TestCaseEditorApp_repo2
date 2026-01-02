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
    public partial class TestCaseGenerator_NavigationVM : BaseDomainViewModel, IDisposable
    {
        private new readonly ITestCaseGenerationMediator _mediator;
        
        [ObservableProperty]
        private int selectedRequirementIndex = -1;
        
        [ObservableProperty] 
        private Requirement? selectedRequirement;
        
        [ObservableProperty]
        private string? searchQuery;
        
        [ObservableProperty]
        private bool wrapOnNextWithoutTestCase = false;
        
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
            
            Title = "Requirements Navigation";
            InitializeRequirementsDropdown();
            _logger.LogDebug("TestCaseGenerator_NavigationVM created with mediator");
        }

        // ===== PROPERTIES =====

        private MenuAction? _requirementsDropdown;
        
        /// <summary>
        /// Requirements dropdown for navigation
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
        /// Currently selected requirement - ObservableProperty with change handler
        /// </summary>
        partial void OnSelectedRequirementChanged(Requirement? value)
        {
            // Update index using mediator's requirements collection
            SelectedRequirementIndex = value != null ? _mediator.Requirements.IndexOf(value) : -1;
            
            // Notify mediator of selection
            if (value != null)
            {                        
                _mediator.SelectRequirement(value);
                _logger.LogDebug("Requirement selected: {RequirementId}", value.GlobalId);
            }
            
            // Update UI
            OnPropertyChanged(nameof(RequirementPositionDisplay));
            
            // Update dropdown text
            if (value != null && RequirementsDropdown != null)
            {   
                RequirementsDropdown.IsExpanded = false;
                RequirementsDropdown.Text = $"{value.Item} - {value.Name}";
            }
        }

        /// <summary>
        /// Selected requirement index change handler - ObservableProperty
        /// </summary>
        partial void OnSelectedRequirementIndexChanged(int value)
        {
            // Update selected requirement using mediator's requirements collection
            if (value >= 0 && value < _mediator.Requirements.Count)
            {
                var newRequirement = _mediator.Requirements[value];
                if (SelectedRequirement != newRequirement)
                {
                    SelectedRequirement = newRequirement;
                }
            }
            else if (SelectedRequirement != null)
            {
                SelectedRequirement = null;
            }
            
            // Update navigation command states when index changes
            NotifyNavigationCommandsCanExecuteChanged();
        }

        /// <summary>
        /// Display text for current position (e.g. "1 / 5")
        /// </summary>
        public string RequirementPositionDisplay
        {
            get
            {
                var total = RequirementsView.Count;
                var pos = (SelectedRequirementIndex >= 0 && total > 0) ? (SelectedRequirementIndex + 1).ToString() : "1";
                return $"{pos} / {total}";
            }
        }

        // ===== NAVIGATION COMMANDS =====

        /// <summary>
        /// Command to navigate to previous requirement
        /// </summary>
        public ICommand PreviousRequirementCommand => _previousCommand ??= new RelayCommand(
            () =>
            {
                if (_mediator.Requirements.Count == 0 || SelectedRequirementIndex <= 0) return;
                
                var newIndex = SelectedRequirementIndex - 1;
                SelectedRequirement = _mediator.Requirements[newIndex];
            },
            () => _mediator.Requirements.Count > 0 && SelectedRequirementIndex > 0);

        /// <summary>
        /// Command to navigate to next requirement
        /// </summary>
        public ICommand NextRequirementCommand => _nextCommand ??= new RelayCommand(
            () =>
            {
                if (_mediator.Requirements.Count == 0 || SelectedRequirementIndex >= _mediator.Requirements.Count - 1) return;
                
                var newIndex = SelectedRequirementIndex + 1;
                SelectedRequirement = _mediator.Requirements[newIndex];
            },
            () => _mediator.Requirements.Count > 0 && SelectedRequirementIndex < _mediator.Requirements.Count - 1);

        /// <summary>
        /// Command to navigate to next requirement without test cases
        /// </summary>
        public ICommand NextWithoutTestCaseCommand => _nextWithoutTestCaseCommand ??= new RelayCommand(
            () =>
            {
                if (_mediator.Requirements.Count == 0) return;
                
                var startIndex = SelectedRequirementIndex >= 0 ? SelectedRequirementIndex + 1 : 0;
                
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

        // ===== EVENT HANDLERS =====

        /// <summary>
        /// Handle requirement selection events from domain mediator
        /// </summary>
        private void OnRequirementSelected(TestCaseGenerationEvents.RequirementSelected e)
        {
            if (!ReferenceEquals(SelectedRequirement, e.Requirement))
            {
                SelectedRequirement = e.Requirement;
                
                // Update dropdown text to show selected requirement
                if (RequirementsDropdown != null && e.Requirement != null)
                {
                    RequirementsDropdown.IsExpanded = false;
                    RequirementsDropdown.Text = $"{e.Requirement.Item} - {e.Requirement.Name}";
                }
                
                OnPropertyChanged(nameof(RequirementPositionDisplay));

                
                if (e.Requirement != null)
                {
                    _logger.LogDebug("Navigation updated for requirement: {RequirementId}", e.Requirement.GlobalId);
                }
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
                SelectedRequirement = null;
                SelectedRequirementIndex = -1;
            }
            // If requirements were loaded and none is selected, select the first one
            else if (e.NewCount > 0 && SelectedRequirementIndex < 0 && _mediator.Requirements.Count > 0)
            {
                SelectedRequirement = _mediator.Requirements[0];
            }
            
            // Notify UI that RequirementsView has updated (it's bound to mediator's Requirements collection)
            OnPropertyChanged(nameof(RequirementsView));
            OnPropertyChanged(nameof(Requirements));
            OnPropertyChanged(nameof(RequirementPositionDisplay));
            
            // Update the dropdown
            UpdateDropdownFromRequirements();
            
            // Update navigation command states when requirements collection changes
            NotifyNavigationCommandsCanExecuteChanged();
        }

        /// <summary>
        /// Notify all navigation commands that their CanExecute state may have changed
        /// </summary>
        private void NotifyNavigationCommandsCanExecuteChanged()
        {
            _previousCommand?.NotifyCanExecuteChanged();
            _nextCommand?.NotifyCanExecuteChanged();
            _nextWithoutTestCaseCommand?.NotifyCanExecuteChanged();
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

        private void InitializeRequirementsDropdown()
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

            var requirements = RequirementsView;
            if (requirements?.Any() == true)
            {
                foreach (var req in requirements)
                {
                    RequirementsDropdown.Children.Add(new MenuAction
                    {
                        Text = $"{req.Item} - {req.Name}",
                        Icon = "📄",
                        Command = new RelayCommand(() => 
                        {
                            _mediator.PublishEvent(new TestCaseGenerationEvents.RequirementSelected 
                            {
                                Requirement = req,
                                SelectedBy = "NavigationDropdown"
                            });
                        }),
                        Level = 1
                    });
                }
                
                var selectedReq = SelectedRequirement;
                RequirementsDropdown.Text = selectedReq != null 
                    ? $"{selectedReq.Item} - {selectedReq.Name}"
                    : $"Requirements ({requirements.Count})";
            }
            else
            {
                RequirementsDropdown.Text = "No requirements loaded";
            }
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
