using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Events;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels
{
    /// <summary>
    /// Main coordinator ViewModel for Test Case Creation domain
    /// AI Guidelines compliant with clean mediator integration
    /// </summary>
    public class TestCaseCreationMainVM : BaseDomainViewModel
    {
        private readonly ITestCaseCreationMediator _domainMediator;
        private EditableTestCase? _selectedTestCase;
        private Requirement? _currentRequirement;
        private bool _hasUnsavedChanges;

        public TestCaseCreationMainVM(ITestCaseCreationMediator mediator, ILogger<TestCaseCreationMainVM> logger)
            : base(mediator, logger)
        {
            _logger.LogDebug("TestCaseCreationMainVM constructor started");
            
            _domainMediator = mediator;
            _logger.LogDebug("Mediator assigned");

            // Initialize collections
            TestCases = new ObservableCollection<EditableTestCase>();
            _logger.LogDebug("TestCases collection initialized");

            // Subscribe to domain events
            _domainMediator.Subscribe<TestCaseCreationEvents.TestCaseCreated>(OnTestCaseCreated);
            _domainMediator.Subscribe<TestCaseCreationEvents.TestCaseDeleted>(OnTestCaseDeleted);
            _domainMediator.Subscribe<TestCaseCreationEvents.TestCasesSaved>(OnTestCasesSaved);
            _domainMediator.Subscribe<TestCaseCreationEvents.RequirementContextChanged>(OnRequirementContextChanged);
            _domainMediator.Subscribe<TestCaseCreationEvents.TestCaseSelectionChanged>(OnTestCaseSelectionChanged);
            _logger.LogDebug("Event subscriptions completed");
            
            // Initialize commands
            AddTestCaseCommand = new AsyncRelayCommand(AddTestCaseAsync, () => CurrentRequirement != null);
            RemoveTestCaseCommand = new AsyncRelayCommand(RemoveSelectedTestCaseAsync, () => SelectedTestCase != null);
            SaveAllCommand = new AsyncRelayCommand(SaveAllTestCasesAsync, () => TestCases.Any() && HasUnsavedChanges);
            GenerateTestCaseCommandCommand = new AsyncRelayCommand(GenerateTestCaseCommandAsync, () => SelectedTestCase != null);
            _logger.LogDebug("Commands initialized");
            
            Title = "Test Case Creation";
            _logger.LogDebug("TestCaseCreationMainVM constructor completed successfully");
        }

        // ===== PROPERTIES =====

        /// <summary>
        /// Collection of test cases for current requirement
        /// </summary>
        public ObservableCollection<EditableTestCase> TestCases { get; }

        /// <summary>
        /// Currently selected test case
        /// </summary>
        public EditableTestCase? SelectedTestCase
        {
            get => _selectedTestCase;
            set
            {
                var previousTestCase = _selectedTestCase;
                if (SetProperty(ref _selectedTestCase, value))
                {
                    _domainMediator.PublishEvent(new TestCaseCreationEvents.TestCaseSelectionChanged
                    {
                        PreviousTestCase = previousTestCase,
                        CurrentTestCase = value,
                        SelectedBy = "User",
                        Timestamp = DateTime.Now,
                        CorrelationId = Guid.NewGuid().ToString()
                    });
                    
                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Current requirement context
        /// </summary>
        public Requirement? CurrentRequirement
        {
            get => _currentRequirement;
            private set
            {
                if (SetProperty(ref _currentRequirement, value))
                {
                    UpdateCommandStates();
                    OnPropertyChanged(nameof(RequirementInfo));
                }
            }
        }

        /// <summary>
        /// Display info for current requirement
        /// </summary>
        public string RequirementInfo => CurrentRequirement != null 
            ? $"{CurrentRequirement.Item}: {CurrentRequirement.Name}"
            : "No requirement selected";

        // Use base class StatusMessage property instead of redefining it
        // public string StatusMessage is inherited from BaseDomainViewModel

        /// <summary>
        /// Whether there are unsaved changes
        /// </summary>
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                if (SetProperty(ref _hasUnsavedChanges, value))
                {
                    UpdateCommandStates();
                    _domainMediator.SetWorkspaceDirty(value);
                }
            }
        }

        // ===== COMMANDS =====

        public ICommand AddTestCaseCommand { get; }
        public ICommand RemoveTestCaseCommand { get; }
        public ICommand SaveAllCommand { get; }
        public ICommand GenerateTestCaseCommandCommand { get; }

        // ===== COMMAND HANDLERS =====

        private async Task AddTestCaseAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Creating new test case...";

                var newTestCase = await _domainMediator.CreateTestCaseAsync();
                
                StatusMessage = $"Created test case: {newTestCase.Title}";
                SelectedTestCase = newTestCase;
                
                _logger.LogInformation("Added new test case: {Title}", newTestCase.Title);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to create test case: {ex.Message}";
                _logger.LogError(ex, "Failed to add test case");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RemoveSelectedTestCaseAsync()
        {
            if (SelectedTestCase == null) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Removing test case...";

                var testCaseToRemove = SelectedTestCase;
                await _domainMediator.DeleteTestCaseAsync(testCaseToRemove);
                
                StatusMessage = $"Removed test case: {testCaseToRemove.Title}";
                
                _logger.LogInformation("Removed test case: {Title}", testCaseToRemove.Title);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to remove test case: {ex.Message}";
                _logger.LogError(ex, "Failed to remove test case");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SaveAllTestCasesAsync()
        {
            if (CurrentRequirement == null) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Saving test cases...";

                await _domainMediator.SaveTestCasesToRequirementAsync(CurrentRequirement, TestCases);
                
                StatusMessage = $"Saved {TestCases.Count} test cases";
                
                _logger.LogInformation("Saved {Count} test cases to requirement {RequirementId}", 
                    TestCases.Count, CurrentRequirement.GlobalId);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to save test cases: {ex.Message}";
                _logger.LogError(ex, "Failed to save test cases");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task GenerateTestCaseCommandAsync()
        {
            if (SelectedTestCase == null) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Generating test case command...";

                var command = await _domainMediator.GenerateTestCaseCommandAsync(SelectedTestCase, "jama");
                
                // Copy to clipboard
                System.Windows.Clipboard.SetText(command);
                
                StatusMessage = "Test case command copied to clipboard";
                
                _logger.LogInformation("Generated test case command for: {Title}", SelectedTestCase.Title);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to generate test case command: {ex.Message}";
                _logger.LogError(ex, "Failed to generate test case command");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ===== EVENT HANDLERS =====

        private void OnTestCaseCreated(TestCaseCreationEvents.TestCaseCreated e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!TestCases.Contains(e.TestCase))
                {
                    TestCases.Add(e.TestCase);
                    UpdateUnsavedChanges();
                }
            });
        }

        private void OnTestCaseDeleted(TestCaseCreationEvents.TestCaseDeleted e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var testCase = TestCases.FirstOrDefault(tc => tc.Id == e.TestCaseId);
                if (testCase != null)
                {
                    TestCases.Remove(testCase);
                    if (SelectedTestCase == testCase)
                    {
                        SelectedTestCase = TestCases.FirstOrDefault();
                    }
                    UpdateUnsavedChanges();
                }
            });
        }

        private void OnTestCasesSaved(TestCaseCreationEvents.TestCasesSaved e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                HasUnsavedChanges = false;
                StatusMessage = $"Saved {e.TestCases.Count} test cases";
            });
        }

        private async void OnRequirementContextChanged(TestCaseCreationEvents.RequirementContextChanged e)
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                CurrentRequirement = e.CurrentRequirement;
                
                if (CurrentRequirement != null)
                {
                    await LoadTestCasesFromRequirement(CurrentRequirement);
                }
                else
                {
                    TestCases.Clear();
                    SelectedTestCase = null;
                }
            });
        }

        private void OnTestCaseSelectionChanged(TestCaseCreationEvents.TestCaseSelectionChanged e)
        {
            // Update UI state based on selection change
            UpdateCommandStates();
        }

        // ===== PUBLIC METHODS =====

        /// <summary>
        /// Load test cases from a requirement
        /// </summary>
        public async Task LoadRequirementAsync(Requirement requirement)
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Loading test cases...";

                await _domainMediator.SetRequirementContextAsync(requirement);
                await LoadTestCasesFromRequirement(requirement);
                
                StatusMessage = $"Loaded {TestCases.Count} test cases";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to load requirement: {ex.Message}";
                _logger.LogError(ex, "Failed to load requirement {RequirementId}", requirement.GlobalId);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====

        protected override bool CanSave() => HasUnsavedChanges && CurrentRequirement != null;

        protected override async Task SaveAsync()
        {
            if (CurrentRequirement != null)
            {
                await SaveAllTestCasesAsync();
            }
        }

        protected override bool CanRefresh() => CurrentRequirement != null;

        protected override async Task RefreshAsync()
        {
            if (CurrentRequirement != null)
            {
                await LoadRequirementAsync(CurrentRequirement);
            }
        }

        protected override bool CanCancel() => HasUnsavedChanges;

        protected override void Cancel()
        {
            // Reload from requirement to discard changes
            if (CurrentRequirement != null)
            {
                _ = LoadRequirementAsync(CurrentRequirement);
            }
        }

        // ===== PRIVATE METHODS =====

        private async Task LoadTestCasesFromRequirement(Requirement requirement)
        {
            try
            {
                var loadedTestCases = await _domainMediator.LoadTestCasesFromRequirementAsync(requirement);
                
                TestCases.Clear();
                foreach (var testCase in loadedTestCases)
                {
                    TestCases.Add(testCase);
                    
                    // Subscribe to property changes for dirty tracking
                    testCase.PropertyChanged += OnTestCasePropertyChanged;
                }
                
                SelectedTestCase = TestCases.FirstOrDefault();
                HasUnsavedChanges = false;
                
                _logger.LogDebug("Loaded {Count} test cases from requirement {RequirementId}", 
                    TestCases.Count, requirement.GlobalId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load test cases from requirement {RequirementId}", requirement.GlobalId);
                throw;
            }
        }

        private void OnTestCasePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is EditableTestCase testCase && e.PropertyName != nameof(EditableTestCase.IsSelected))
            {
                UpdateUnsavedChanges();
            }
        }

        private void UpdateUnsavedChanges()
        {
            HasUnsavedChanges = TestCases.Any(tc => tc.IsDirty);
        }

        private void UpdateCommandStates()
        {
            ((AsyncRelayCommand)AddTestCaseCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)RemoveTestCaseCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)SaveAllCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)GenerateTestCaseCommandCommand).NotifyCanExecuteChanged();
        }

        // ===== DISPOSE =====

        public override void Dispose()
        {
            // Unsubscribe from mediator events
            try
            {
                _domainMediator.Unsubscribe<TestCaseCreationEvents.TestCaseCreated>(OnTestCaseCreated);
                _domainMediator.Unsubscribe<TestCaseCreationEvents.TestCaseDeleted>(OnTestCaseDeleted);
                _domainMediator.Unsubscribe<TestCaseCreationEvents.TestCasesSaved>(OnTestCasesSaved);
                _domainMediator.Unsubscribe<TestCaseCreationEvents.RequirementContextChanged>(OnRequirementContextChanged);
                _domainMediator.Unsubscribe<TestCaseCreationEvents.TestCaseSelectionChanged>(OnTestCaseSelectionChanged);

                // Unsubscribe from test case property changes
                foreach (var testCase in TestCases)
                {
                    testCase.PropertyChanged -= OnTestCasePropertyChanged;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error unsubscribing from events during dispose");
            }

            base.Dispose();
        }
    }
}
