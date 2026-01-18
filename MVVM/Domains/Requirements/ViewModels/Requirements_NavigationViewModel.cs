using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Models.DataDrivenMenu;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using RequirementsMediator = TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.RequirementsMediator;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Navigation ViewModel for Requirements domain - uses RequirementsMediator as data source.
    /// Provides requirement navigation functionality with autonomous Requirements domain operations.
    /// </summary>
    public partial class Requirements_NavigationViewModel : BaseDomainViewModel, IDisposable
    {
        private readonly IRequirementsMediator _requirementsMediator;
        
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
        /// Constructor for Requirements mediator injection (independent data source)
        /// </summary>
        public Requirements_NavigationViewModel(IRequirementsMediator mediator, ILogger<Requirements_NavigationViewModel> logger) 
            : base(mediator, logger)
        {
            _requirementsMediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            
            // Subscribe to Requirements domain events (autonomous data source)
            if (_requirementsMediator is RequirementsMediator concreteMediator)
            {
                concreteMediator.Subscribe<RequirementsEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
                concreteMediator.Subscribe<RequirementsEvents.RequirementSelected>(OnRequirementSelected);
            }
            
            // Initialize requirements dropdown
            RequirementsDropdown = new MenuAction
            {
                Text = "No requirements loaded",
                IsExpanded = false,
                Children = new ObservableCollection<MenuContentItem>()
            };
            
            // Load initial requirements from Requirements domain mediator
            RefreshRequirementsFromMediator();
        }

        /// <summary>
        /// View of requirements for dropdown/navigation (public read-only access)
        /// </summary>
        public ObservableCollection<Requirement> RequirementsView => _requirements;

        /// <summary>
        /// Navigation dropdown for requirement selection
        /// </summary>
        public MenuAction RequirementsDropdown { get; private set; }
        
        /// <summary>
        /// Handle requirements collection change events from Requirements domain mediator
        /// </summary>
        private void OnRequirementsCollectionChanged(RequirementsEvents.RequirementsCollectionChanged e)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"🔔 [Requirements_NavigationViewModel] Requirements collection changed: Action={e.Action}, AffectedRequirements={e.AffectedRequirements?.Count ?? 0}, NewCount={e.NewCount}");
            
            // Refresh the local requirements collection from Requirements domain mediator
            RefreshRequirementsFromMediator();
            
            // Handle selection based on new collection state
            if (RequirementsView.Count == 0)
            {
                // Collection is empty (project closed/unloaded), reset navigation state
                TestCaseEditorApp.Services.Logging.Log.Debug("🧹 [Requirements_NavigationViewModel] Collection is empty, resetting navigation state");
                SelectedRequirement = null;
                SelectedRequirementIndex = -1;
                TestCaseEditorApp.Services.Logging.Log.Debug("✅ [Requirements_NavigationViewModel] Navigation state reset complete");
                return;
            }
            
            // If a requirement was previously selected, try to maintain selection
            if (SelectedRequirement != null)
            {
                var index = RequirementsView.IndexOf(SelectedRequirement);
                if (index >= 0)
                {
                    SelectedRequirementIndex = index;
                }
                else
                {
                    // Selected requirement no longer exists, select first available
                    SelectedRequirement = RequirementsView[0];
                    SelectedRequirementIndex = 0;
                }
            }
            else if (RequirementsView.Count > 0)
            {
                // No previous selection, select first requirement if available
                SelectedRequirement = RequirementsView[0];
                SelectedRequirementIndex = 0;
            }
        }

        /// <summary>
        /// Handle requirement selection events from Requirements domain mediator
        /// </summary>
        private void OnRequirementSelected(RequirementsEvents.RequirementSelected e)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_NavigationViewModel] Requirement selected: {e.Requirement?.GlobalId ?? "NULL"}");
            
            if (SelectedRequirement != e.Requirement)
            {
                SelectedRequirement = e.Requirement;
                
                // Update selected index to match
                var index = RequirementsView.IndexOf(e.Requirement);
                if (index >= 0)
                {
                    SelectedRequirementIndex = index;
                }
            }
            
            // Update UI
            OnPropertyChanged(nameof(RequirementPositionDisplay));
            
            // Update dropdown text
            if (e.Requirement != null && RequirementsDropdown != null)
            {   
                RequirementsDropdown.IsExpanded = false;
                RequirementsDropdown.Text = $"{e.Requirement.Item} - {e.Requirement.Name}";
            }
        }

        /// <summary>
        /// Selected requirement index change handler - ObservableProperty
        /// </summary>
        partial void OnSelectedRequirementIndexChanged(int value)
        {
            // Update selected requirement using Requirements domain mediator's requirements collection
            if (value >= 0 && value < _requirementsMediator.Requirements.Count)
            {
                var newRequirement = _requirementsMediator.Requirements[value];
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
                var pos = (SelectedRequirementIndex >= 0 && total > 0) ? (SelectedRequirementIndex + 1).ToString() : "0";
                return $"{pos} / {total}";
            }
        }

        // ===== NAVIGATION COMMANDS =====

        /// <summary>
        /// Navigate to previous requirement
        /// </summary>
        public ICommand PreviousCommand => _previousCommand ??= new RelayCommand(MoveToPrevious, CanMoveToPrevious);
        
        /// <summary>
        /// Navigate to next requirement
        /// </summary>
        public ICommand NextCommand => _nextCommand ??= new RelayCommand(MoveToNext, CanMoveToNext);
        
        /// <summary>
        /// Navigate to next requirement without test cases
        /// </summary>
        public ICommand NextWithoutTestCaseCommand => _nextWithoutTestCaseCommand ??= new RelayCommand(MoveToNextWithoutTestCase, CanMoveToNextWithoutTestCase);

        /// <summary>
        /// Search command for requirement lookup
        /// </summary>
        public ICommand SearchCommand => _searchCommand ??= new RelayCommand<string>(ExecuteSearch);

        /// <summary>
        /// Move to previous requirement
        /// </summary>
        private void MoveToPrevious()
        {
            if (CanMoveToPrevious() && SelectedRequirementIndex > 0)
            {
                var newIndex = SelectedRequirementIndex - 1;
                var requirement = RequirementsView[newIndex];
                
                _requirementsMediator.PublishEvent(new RequirementsEvents.RequirementSelected 
                { 
                    Requirement = requirement,
                    SelectedBy = "NavigationPrevious"
                });
            }
        }
        
        /// <summary>
        /// Move to next requirement
        /// </summary>
        private void MoveToNext()
        {
            if (CanMoveToNext())
            {
                var newIndex = SelectedRequirementIndex + 1;
                var requirement = RequirementsView[newIndex];
                
                _requirementsMediator.PublishEvent(new RequirementsEvents.RequirementSelected 
                { 
                    Requirement = requirement,
                    SelectedBy = "NavigationNext"
                });
            }
        }
        
        /// <summary>
        /// Move to next requirement that doesn't have test cases
        /// </summary>
        private void MoveToNextWithoutTestCase()
        {
            if (!CanMoveToNextWithoutTestCase()) return;
            
            var startIndex = Math.Max(0, SelectedRequirementIndex + 1);
            var totalRequirements = RequirementsView.Count;
            
            for (int i = 0; i < totalRequirements; i++)
            {
                var index = (startIndex + i) % totalRequirements;
                
                // Break if we've wrapped around and reached the starting point without wrap enabled
                if (!WrapOnNextWithoutTestCase && index < startIndex)
                    break;
                    
                var requirement = RequirementsView[index];
                
                // Check if requirement has test cases (simplified check)
                if (!HasTestCases(requirement))
                {
                    _requirementsMediator.PublishEvent(new RequirementsEvents.RequirementSelected 
                    { 
                        Requirement = requirement,
                        SelectedBy = "NavigationNextWithoutTestCase"
                    });
                    return;
                }
            }
            
            TestCaseEditorApp.Services.Logging.Log.Debug("[Requirements_NavigationViewModel] No requirements without test cases found");
        }
        
        /// <summary>
        /// Simple check if requirement has test cases (can be enhanced)
        /// </summary>
        private bool HasTestCases(Requirement requirement)
        {
            // Placeholder logic - enhance based on actual test case tracking
            return false; // For now, assume no requirements have test cases
        }

        /// <summary>
        /// Check if can move to previous requirement
        /// </summary>
        private bool CanMoveToPrevious()
        {
            return SelectedRequirementIndex > 0 && RequirementsView.Count > 0;
        }
        
        /// <summary>
        /// Check if can move to next requirement
        /// </summary>
        private bool CanMoveToNext()
        {
            return SelectedRequirementIndex >= 0 && 
                   SelectedRequirementIndex < (RequirementsView.Count - 1) && 
                   RequirementsView.Count > 0;
        }
        
        /// <summary>
        /// Check if can move to next requirement without test cases
        /// </summary>
        private bool CanMoveToNextWithoutTestCase()
        {
            return RequirementsView.Count > 0;
        }

        /// <summary>
        /// Execute search for requirements
        /// </summary>
        private void ExecuteSearch(string? query)
        {
            if (string.IsNullOrEmpty(query))
            {
                RefreshRequirementsFromMediator();
                return;
            }
            
            var filteredRequirements = _requirementsMediator.Requirements
                .Where(r => r.GlobalId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           r.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            _requirements.Clear();
            foreach (var req in filteredRequirements)
            {
                _requirements.Add(req);
            }
            
            UpdateDropdownItems(_requirements);
            OnPropertyChanged(nameof(RequirementPositionDisplay));
        }

        /// <summary>
        /// Refresh local requirements collection from Requirements domain mediator
        /// </summary>
        private void RefreshRequirementsFromMediator()
        {
            _requirements.Clear();
            
            foreach (var req in _requirementsMediator.Requirements)
            {
                _requirements.Add(req);
            }
            
            // Set SelectedRequirement to match the mediator's CurrentRequirement
            var currentRequirement = _requirementsMediator.CurrentRequirement;
            if (currentRequirement != null)
            {
                SelectedRequirement = currentRequirement;
                var index = _requirements.IndexOf(currentRequirement);
                if (index >= 0)
                {
                    SelectedRequirementIndex = index;
                }
            }
            
            UpdateDropdownItems(_requirements);
            OnPropertyChanged(nameof(RequirementPositionDisplay));
            NotifyNavigationCommandsCanExecuteChanged();
        }

        /// <summary>
        /// Update dropdown items
        /// </summary>
        private void UpdateDropdownItems(IEnumerable<Requirement> requirements)
        {
            // Ensure UI updates happen on the dispatcher thread
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                RequirementsDropdown.Children.Clear();
                
                if (requirements.Any())
                {
                    foreach (var req in requirements)
                    {
                        RequirementsDropdown.Children.Add(new MenuAction
                        {
                            Text = $"{req.Item} - {req.Name}",
                            Command = new RelayCommand(() => 
                            {
                                _requirementsMediator.PublishEvent(new RequirementsEvents.RequirementSelected 
                                {
                                    Requirement = req,
                                    SelectedBy = "NavigationDropdown"
                                });
                            })
                        });
                    }
                    
                    var selectedReq = SelectedRequirement;
                    RequirementsDropdown.Text = selectedReq != null 
                        ? $"{selectedReq.Item} - {selectedReq.Name}"
                        : $"Requirements ({requirements.Count()})";
                }
                else
                {
                    RequirementsDropdown.Text = "No requirements loaded";
                }
            });
        }

        /// <summary>
        /// Notify all navigation commands to update their CanExecute state
        /// THREADING: Dispatches to UI thread to prevent cross-thread access violations
        /// </summary>
        private void NotifyNavigationCommandsCanExecuteChanged()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _previousCommand?.NotifyCanExecuteChanged();
                _nextCommand?.NotifyCanExecuteChanged();
                _nextWithoutTestCaseCommand?.NotifyCanExecuteChanged();
            });
        }

        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====

        protected override async Task SaveAsync()
        {
            // Navigation state doesn't typically need persistence
            await Task.CompletedTask;
        }

        protected override void Cancel()
        {
            // No cancellable operations for navigation
        }

        protected override async Task RefreshAsync()
        {
            RefreshRequirementsFromMediator();
            await Task.CompletedTask;
        }

        protected override bool CanSave() => false;
        protected override bool CanCancel() => false;
        protected override bool CanRefresh() => true;

        /// <summary>
        /// Dispose pattern implementation
        /// </summary>
        public override void Dispose()
        {
            // Unsubscribe from mediator events to prevent memory leaks
            if (_requirementsMediator is RequirementsMediator concreteMediator)
            {
                concreteMediator.Unsubscribe<RequirementsEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
                concreteMediator.Unsubscribe<RequirementsEvents.RequirementSelected>(OnRequirementSelected);
            }
            base.Dispose();
        }
    }
}