using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Models.DataDrivenMenu;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Events;
using System.Linq;
using System.Windows.Input;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class NavigationViewModel : ObservableObject
    {
        private readonly ITestCaseGenerationMediator? _mediator;
        private readonly ILogger<NavigationViewModel>? _logger;

        [ObservableProperty]
        private MenuAction? requirementsDropdown;

        [ObservableProperty]
        private Requirement? selectedRequirement;

        [ObservableProperty]
        private ObservableCollection<Requirement> requirements = new();

        [ObservableProperty]
        private string? searchText;

        [ObservableProperty]
        private int currentIndex;

        [ObservableProperty]
        private int totalCount;

        public NavigationViewModel()
        {
            InitializeDropdown();
        }

        public NavigationViewModel(ITestCaseGenerationMediator mediator, ILogger<NavigationViewModel> logger)
        {
            _mediator = mediator;
            _logger = logger;
            InitializeDropdown();
            
            // Subscribe to requirement changes from the mediator
            if (_mediator != null)
            {
                _mediator.Subscribe<TestCaseGenerationEvents.RequirementsImported>(OnRequirementsImported);
            }
        }

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
            
            UpdateIndexCounters();
        }

        private void OnRequirementsImported(TestCaseGenerationEvents.RequirementsImported evt)
        {
            UpdateRequirements(evt.Requirements);
        }

        /// <summary>
        /// Update the dropdown with current requirements
        /// </summary>
        public void UpdateRequirements(IEnumerable<Requirement> newRequirements)
        {
            Requirements.Clear();
            RequirementsDropdown!.Children.Clear();

            if (newRequirements?.Any() == true)
            {
                foreach (var req in newRequirements)
                {
                    Requirements.Add(req);
                    RequirementsDropdown.Children.Add(new MenuAction
                    {
                        Id = $"req-{req.GlobalId}",
                        Text = $"{req.Item} — {req.Name}",
                        Icon = "📄",
                        Command = new RelayCommand(() => SelectRequirement(req)),
                        Level = 1
                    });
                }
                
                RequirementsDropdown.Text = $"Requirements ({Requirements.Count})";
                SelectedRequirement = Requirements.FirstOrDefault();
                TotalCount = Requirements.Count;
                CurrentIndex = SelectedRequirement != null ? 1 : 0;
            }
            else
            {
                RequirementsDropdown.Text = "No requirements loaded";
                SelectedRequirement = null;
                TotalCount = 0;
                CurrentIndex = 0;
            }
        }

        private void SelectRequirement(Requirement requirement)
        {
            SelectedRequirement = requirement;
            RequirementsDropdown!.IsExpanded = false;
            
            // Update position indicator
            var index = Requirements.IndexOf(requirement);
            CurrentIndex = index >= 0 ? index + 1 : 0;
            
            _logger?.LogDebug("Selected requirement: {RequirementName} (position {Index}/{Total})", 
                requirement.Name, CurrentIndex, TotalCount);
        }

        [RelayCommand]
        private void PreviousRequirement()
        {
            if (Requirements.Count == 0) return;
            
            var currentIndex = SelectedRequirement != null ? Requirements.IndexOf(SelectedRequirement) : -1;
            var newIndex = currentIndex <= 0 ? Requirements.Count - 1 : currentIndex - 1;
            
            SelectRequirement(Requirements[newIndex]);
        }

        [RelayCommand]
        private void NextRequirement()
        {
            if (Requirements.Count == 0) return;
            
            var currentIndex = SelectedRequirement != null ? Requirements.IndexOf(SelectedRequirement) : -1;
            var newIndex = currentIndex >= Requirements.Count - 1 ? 0 : currentIndex + 1;
            
            SelectRequirement(Requirements[newIndex]);
        }

        [RelayCommand]
        private void Search()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;

            var q = SearchText!.Trim();
            var found = Requirements.FirstOrDefault(r => 
                r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.Item.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.GlobalId.ToString().Contains(q));
                
            if (found != null) 
            {
                SelectRequirement(found);
            }
        }

        private void UpdateIndexCounters()
        {
            TotalCount = Requirements.Count;
            CurrentIndex = SelectedRequirement != null ? Requirements.IndexOf(SelectedRequirement) + 1 : 0;
        }
    }
}