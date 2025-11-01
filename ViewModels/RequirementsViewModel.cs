using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.ViewModels
{
    public class RequirementsViewModel : ObservableObject
    {
        private readonly IPersistenceService _persistence;
        private const string PersistenceKey = "requirements";

        public RequirementsViewModel(IPersistenceService persistence)
        {
            _persistence = persistence;

            // Load persisted requirements if present
            var loaded = _persistence.Load<string[]>(PersistenceKey);
            if (loaded != null && loaded.Length > 0)
            {
                Requirements = new ObservableCollection<string>(loaded);
            }
            else
            {
                Requirements = new ObservableCollection<string>
                {
                    "REQ-001: Login should succeed with valid credentials",
                    "REQ-002: Logout clears session",
                    "REQ-003: Password reset flow"
                };
            }

            // keep persistence in sync
            Requirements.CollectionChanged += Requirements_CollectionChanged;

            AddRequirementCommand = new RelayCommand(() =>
            {
                Requirements.Add($"REQ-{Requirements.Count + 1:000}: New requirement");
            });

            RemoveRequirementCommand = new RelayCommand(() =>
            {
                if (SelectedRequirement != null)
                {
                    Requirements.Remove(SelectedRequirement);
                    SelectedRequirement = null;
                }
            });
        }

        private void Requirements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Save as array for simplicity
            _persistence.Save(PersistenceKey, Requirements.ToArray());
        }

        public ObservableCollection<string> Requirements { get; private set; }

        private string? _selectedRequirement;
        public string? SelectedRequirement
        {
            get => _selectedRequirement;
            set => SetProperty(ref _selectedRequirement, value);
        }

        public ICommand AddRequirementCommand { get; }
        public ICommand RemoveRequirementCommand { get; }
    }
}