using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Models.DataDrivenMenu;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Requirements domain navigation ViewModel that matches the interface expected by RequirementsNavigationView.
    /// Provides navigation controls, dropdown, and search functionality with the exact interface the XAML expects.
    /// </summary>
    public partial class Requirements_NavigationViewModel : ObservableObject
    {
        private readonly RequirementsIndexViewModel _indexViewModel;
        private readonly ILogger<Requirements_NavigationViewModel>? _logger;

        [ObservableProperty]
        private MenuAction? requirementsDropdown;

        public Requirements_NavigationViewModel(
            RequirementsIndexViewModel indexViewModel,
            ILogger<Requirements_NavigationViewModel>? logger = null)
        {
            _indexViewModel = indexViewModel ?? throw new ArgumentNullException(nameof(indexViewModel));
            _logger = logger;

            InitializeDropdown();

            // Subscribe to index ViewModel changes to keep navigation in sync
            _indexViewModel.PropertyChanged += OnIndexViewModelPropertyChanged;

            // CRITICAL: Subscribe to collection changes since requirements are loaded AFTER initialization
            _indexViewModel.Requirements.CollectionChanged += (_, __) =>
            {
                UpdateRequirementsDropdown(_indexViewModel.Requirements);
            };

            // Initialize dropdown with current requirements (if any already loaded)
            if (_indexViewModel.Requirements?.Count > 0)
            {
                UpdateRequirementsDropdown(_indexViewModel.Requirements);
            }

            _logger?.LogDebug("[Requirements_NavigationViewModel] Initialized with RequirementsIndexViewModel bridge");
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
        }

        // Commands with exact names expected by RequirementsNavigationView.xaml
        public ICommand PreviousCommand => _indexViewModel.PreviousRequirementCommand;
        public ICommand NextCommand => _indexViewModel.NextRequirementCommand;
        public ICommand SearchCommand => _indexViewModel.SearchCommand ?? new RelayCommand(() => { });

        // Properties with exact names expected by RequirementsNavigationView.xaml
        public string RequirementPositionDisplay => _indexViewModel.RequirementPositionDisplay;
        
        public string? SearchQuery
        {
            get => _indexViewModel.SearchQuery;
            set => _indexViewModel.SearchQuery = value;
        }

        public Requirement? SelectedRequirement
        {
            get => _indexViewModel.SelectedRequirement;
            set => _indexViewModel.SelectedRequirement = value;
        }

        /// <summary>
        /// Handle property changes from the wrapped RequirementsIndexViewModel
        /// </summary>
        private void OnIndexViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(RequirementsIndexViewModel.Requirements):
                    UpdateRequirementsDropdown(_indexViewModel.Requirements);
                    break;
                case nameof(RequirementsIndexViewModel.SelectedRequirement):
                    UpdateDropdownDisplayText();
                    OnPropertyChanged(nameof(SelectedRequirement));
                    OnPropertyChanged(nameof(RequirementPositionDisplay));
                    break;
                case nameof(RequirementsIndexViewModel.SearchQuery):
                    OnPropertyChanged(nameof(SearchQuery));
                    break;
                case nameof(RequirementsIndexViewModel.RequirementPositionDisplay):
                    OnPropertyChanged(nameof(RequirementPositionDisplay));
                    break;
            }
        }

        /// <summary>
        /// Update the dropdown with current requirements using the working MenuAction pattern
        /// </summary>
        private void UpdateRequirementsDropdown(ObservableCollection<Requirement> requirements)
        {
            if (RequirementsDropdown == null) return;

            RequirementsDropdown.Children.Clear();

            if (requirements?.Count > 0)
            {
                foreach (var req in requirements)
                {
                    var displayText = req.Description != null 
                        ? $"{req.Item} — {req.Name}"
                        : $"{req.Item}";
                    
                    RequirementsDropdown.Children.Add(new MenuAction
                    {
                        Id = $"req-{req.GlobalId}",
                        Text = displayText,
                        Icon = "📄",
                        Command = new RelayCommand(() => SelectRequirement(req)),
                        Level = 1
                    });
                }

                RequirementsDropdown.Icon = "📋";
                UpdateDropdownDisplayText();
            }
            else
            {
                RequirementsDropdown.Icon = "❌";
                RequirementsDropdown.Text = "No Requirements Available";
            }

            _logger?.LogDebug("[Requirements_NavigationViewModel] Updated dropdown with {Count} requirements", requirements?.Count ?? 0);
        }

        /// <summary>
        /// Update dropdown display text to show current selection
        /// </summary>
        private void UpdateDropdownDisplayText()
        {
            if (RequirementsDropdown == null) return;

            if (SelectedRequirement != null)
            {
                RequirementsDropdown.Text = $"{SelectedRequirement.Item} — {SelectedRequirement.Name}";
            }
            else if (_indexViewModel.Requirements?.Count > 0)
            {
                RequirementsDropdown.Text = $"Requirements ({_indexViewModel.Requirements.Count})";
            }
            else
            {
                RequirementsDropdown.Text = "No requirements loaded";
            }
        }

        /// <summary>
        /// Handle requirement selection from dropdown
        /// </summary>
        private void SelectRequirement(Requirement requirement)
        {
            _indexViewModel.SelectedRequirement = requirement;
            RequirementsDropdown!.IsExpanded = false;

            _logger?.LogDebug("[Requirements_NavigationViewModel] Selected requirement: {RequirementName} from dropdown", 
                requirement.Name);
        }
    }
}