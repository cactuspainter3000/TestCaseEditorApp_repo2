using System;
using System.Collections;
using System.Text.RegularExpressions;
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

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
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
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            InitializeDropdown();
            
            // Subscribe to requirement changes from the mediator
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementsImported>(OnRequirementsImported);
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
                // Sort requirements using natural numeric order (same as RequirementsIndexViewModel)
                var sortedRequirements = newRequirements.OrderBy(r => r, new RequirementNaturalComparer()).ToList();
                
                foreach (var req in sortedRequirements)
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
    
    /// <summary>
    /// Custom comparer for natural numeric sorting of requirements (exact logic from RequirementsIndexViewModel)
    /// Ensures DECAGON-REQ_RC-5 comes before DECAGON-REQ_RC-12, etc.
    /// </summary>
    internal class RequirementNaturalComparer : IComparer<Requirement>
    {
        private static readonly Regex _trailingNumberRegex = new Regex(@"^(.*?)(\d+)$", RegexOptions.Compiled);

        public int Compare(Requirement? x, Requirement? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            // Prefer 'Item' then 'Name' as the canonical id string (same as RequirementsIndexViewModel)
            var sa = (x.Item ?? x.Name ?? string.Empty).Trim();
            var sb = (y.Item ?? y.Name ?? string.Empty).Trim();

            // If identical strings, consider them equal
            if (string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase)) return 0;

            var ma = _trailingNumberRegex.Match(sa);
            var mb = _trailingNumberRegex.Match(sb);

            if (ma.Success && mb.Success)
            {
                var prefixA = ma.Groups[1].Value;
                var prefixB = mb.Groups[1].Value;
                if (!string.Equals(prefixA, prefixB, StringComparison.OrdinalIgnoreCase))
                {
                    // Compare prefixes alphabetically
                    return StringComparer.OrdinalIgnoreCase.Compare(prefixA, prefixB);
                }

                // Both prefixes equal – compare numeric suffix ascending so 5 comes before 12
                if (long.TryParse(ma.Groups[2].Value, out var na) && long.TryParse(mb.Groups[2].Value, out var nb))
                {
                    // Ascending numeric order
                    var numCompare = na.CompareTo(nb);
                    if (numCompare != 0) return numCompare;
                }

                // Fallback to full-string compare if numeric equal
                return StringComparer.OrdinalIgnoreCase.Compare(sa, sb);
            }

            // If one has numeric suffix and other not, place numeric-suffixed after/before depending on prefix
            if (ma.Success && !mb.Success)
            {
                var prefixA = ma.Groups[1].Value;
                var prefixB = sb;
                var cmp = StringComparer.OrdinalIgnoreCase.Compare(prefixA, prefixB);
                if (cmp != 0) return cmp;
                // If prefixes same, treat the numeric-suffixed as less (so similar entries cluster)
                return -1;
            }
            if (!ma.Success && mb.Success)
            {
                var prefixA = sa;
                var prefixB = mb.Groups[1].Value;
                var cmp = StringComparer.OrdinalIgnoreCase.Compare(prefixA, prefixB);
                if (cmp != 0) return cmp;
                return 1;
            }

            // No numeric suffixes – plain string compare
            return StringComparer.OrdinalIgnoreCase.Compare(sa, sb);
        }
    }
}