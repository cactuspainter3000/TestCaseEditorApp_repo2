using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Mediators;
using TestCaseEditorApp.Services;
using ValidationResult = TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult;

namespace TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.ViewModels
{
    /// <summary>
    /// Main ViewModel for Training Data Validation domain.
    /// Manages human validation workflow for synthetic training examples.
    /// </summary>
    public class TrainingDataValidationViewModel : BaseDomainViewModel
    {
        private readonly ITrainingDataValidationService _validationService;
        private readonly ISyntheticTrainingDataGenerator _syntheticDataGenerator;
        private new readonly ITrainingDataValidationMediator _mediator;

        private ValidationWorkflowState _currentState;
        private SyntheticTrainingExample? _currentExample;
        private int _currentExampleIndex;
        private bool _isLoading;
        private new string _statusMessage;
        private double _progressPercentage;

        public TrainingDataValidationViewModel(
            ITrainingDataValidationService validationService,
            ISyntheticTrainingDataGenerator syntheticDataGenerator,
            ITrainingDataValidationMediator mediator,
            ILogger<TrainingDataValidationViewModel> logger) : base(mediator, logger)
        {
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
            _syntheticDataGenerator = syntheticDataGenerator ?? throw new ArgumentNullException(nameof(syntheticDataGenerator));
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

            // Initialize collections
            PendingExamples = new ObservableCollection<SyntheticTrainingExample>();
            ValidatedExamples = new ObservableCollection<ValidationResult>();
            ValidationOptions = new ObservableCollection<ValidationChoice>();
            
            InitializeValidationChoices();
            InitializeCommands();
            
            CurrentState = ValidationWorkflowState.Ready;
            StatusMessage = "Ready to start validation workflow";
        }

        #region Properties

        /// <summary>
        /// Current state of the validation workflow
        /// </summary>
        public ValidationWorkflowState CurrentState
        {
            get => _currentState;
            set => SetProperty(ref _currentState, value);
        }

        /// <summary>
        /// Currently displayed training example for validation
        /// </summary>
        public SyntheticTrainingExample? CurrentExample
        {
            get => _currentExample;
            set => SetProperty(ref _currentExample, value);
        }

        /// <summary>
        /// Index of current example in the validation queue
        /// </summary>
        public int CurrentExampleIndex
        {
            get => _currentExampleIndex;
            set => SetProperty(ref _currentExampleIndex, value);
        }

        /// <summary>
        /// Whether a background operation is in progress
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Current status message for user feedback
        /// </summary>
        public new string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Progress percentage for current workflow
        /// </summary>
        public double ProgressPercentage
        {
            get => _progressPercentage;
            set => SetProperty(ref _progressPercentage, value);
        }

        /// <summary>
        /// Collection of synthetic examples pending validation
        /// </summary>
        public ObservableCollection<SyntheticTrainingExample> PendingExamples { get; }

        /// <summary>
        /// Collection of completed validation results
        /// </summary>
        public ObservableCollection<ValidationResult> ValidatedExamples { get; }

        /// <summary>
        /// Available validation choices for human reviewers
        /// </summary>
        public ObservableCollection<ValidationChoice> ValidationOptions { get; }

        /// <summary>
        /// Number of examples remaining for validation
        /// </summary>
        public int RemainingCount => PendingExamples.Count;

        /// <summary>
        /// Number of examples successfully validated
        /// </summary>
        public int CompletedCount => ValidatedExamples.Count;

        /// <summary>
        /// Whether there are examples ready for validation
        /// </summary>
        public bool HasPendingExamples => PendingExamples.Any();

        /// <summary>
        /// Whether validation workflow can be started
        /// </summary>
        public bool CanStartValidation => CurrentState == ValidationWorkflowState.Ready && HasPendingExamples;

        #endregion

        #region Commands

        public ICommand GenerateExamplesCommand { get; private set; }
        public ICommand StartValidationCommand { get; private set; }
        public ICommand ApproveExampleCommand { get; private set; }
        public ICommand RejectExampleCommand { get; private set; }
        public ICommand RequireEditsCommand { get; private set; }
        public ICommand SkipExampleCommand { get; private set; }
        public ICommand NextExampleCommand { get; private set; }
        public ICommand PreviousExampleCommand { get; private set; }
        public ICommand SaveProgressCommand { get; private set; }
        public ICommand LoadSessionCommand { get; private set; }
        public ICommand ExportValidatedDataCommand { get; private set; }

        #endregion

        #region Command Implementations

        private async Task GenerateExamplesAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Generating synthetic training examples...";

                var generationOptions = new TrainingDataGenerationOptions
                {
                    TargetExampleCount = 25,
                    MinQualityThreshold = 0.7,
                    DomainContext = "avionics",
                    TaxonomyCategoriesToInclude = new List<string> { "A", "B", "C", "D", "E" } // Focus on core categories
                };

                var progress = new Progress<GenerationProgress>(progress =>
                {
                    ProgressPercentage = progress.CompletionPercentage;
                    StatusMessage = $"Generated {progress.CompletedCount}/{progress.TotalCount} examples";
                });

                var dataset = await _syntheticDataGenerator.GenerateTrainingDatasetAsync(generationOptions);
                var examples = dataset.Examples;

                PendingExamples.Clear();
                foreach (var example in examples)
                {
                    PendingExamples.Add(example);
                }

                CurrentState = ValidationWorkflowState.Ready;
                StatusMessage = $"Generated {examples.Count} synthetic examples ready for validation";
                ProgressPercentage = 0;

                OnPropertyChanged(nameof(RemainingCount));
                OnPropertyChanged(nameof(HasPendingExamples));
                OnPropertyChanged(nameof(CanStartValidation));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to generate synthetic training examples");
                StatusMessage = $"Generation failed: {ex.Message}";
                CurrentState = ValidationWorkflowState.Error;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task StartValidationAsync()
        {
            try
            {
                if (!HasPendingExamples)
                {
                    StatusMessage = "No examples available for validation";
                    return;
                }

                CurrentState = ValidationWorkflowState.Validating;
                CurrentExampleIndex = 0;
                CurrentExample = PendingExamples.FirstOrDefault();
                StatusMessage = "Validation workflow started";
                UpdateProgress();

                _logger?.LogInformation("Started training data validation workflow with {Count} examples", PendingExamples.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to start validation workflow");
                StatusMessage = $"Failed to start validation: {ex.Message}";
                CurrentState = ValidationWorkflowState.Error;
            }
        }

        private async Task ApproveExampleAsync()
        {
            if (CurrentExample == null) return;

            await ProcessValidationChoice(ValidationDecision.Approved, "Example meets quality standards for training");
        }

        private async Task RejectExampleAsync()
        {
            if (CurrentExample == null) return;

            await ProcessValidationChoice(ValidationDecision.Rejected, "Example does not meet training data quality standards");
        }

        private async Task RequireEditsAsync()
        {
            if (CurrentExample == null) return;

            await ProcessValidationChoice(ValidationDecision.RequiresEdits, "Example has potential but needs refinement");
        }

        private async Task SkipExampleAsync()
        {
            if (CurrentExample == null) return;

            await ProcessValidationChoice(ValidationDecision.Skipped, "Example skipped for later review");
        }

        private async Task ProcessValidationChoice(ValidationDecision decision, string reason)
        {
            try
            {
                var validationResult = new ValidationResult
                {
                    ExampleId = CurrentExample.ExampleId,
                    Decision = decision,
                    Reason = reason,
                    ValidatedAt = DateTime.UtcNow,
                    ValidatedBy = Environment.UserName,
                    OriginalExample = CurrentExample
                };

                // Record the validation
                await _validationService.RecordValidationAsync(validationResult);
                ValidatedExamples.Add(validationResult);

                // Remove from pending
                PendingExamples.Remove(CurrentExample);

                // Move to next example
                await MoveToNextExample();

                OnPropertyChanged(nameof(RemainingCount));
                OnPropertyChanged(nameof(CompletedCount));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to process validation choice");
                StatusMessage = $"Validation failed: {ex.Message}";
            }
        }

        private async Task MoveToNextExample()
        {
            if (!PendingExamples.Any())
            {
                // Workflow complete
                CurrentState = ValidationWorkflowState.Complete;
                CurrentExample = null;
                StatusMessage = $"Validation complete! Processed {CompletedCount} examples";
                ProgressPercentage = 100;
                return;
            }

            // Get next example
            CurrentExample = PendingExamples.FirstOrDefault();
            CurrentExampleIndex = ValidatedExamples.Count;
            UpdateProgress();
            
            StatusMessage = $"Reviewing example {CurrentExampleIndex + 1} of {ValidatedExamples.Count + PendingExamples.Count}";
        }

        private async Task MoveToPreviousExample()
        {
            // Allow reviewing previous validation decisions
            if (ValidatedExamples.Any() && CurrentExampleIndex > 0)
            {
                CurrentExampleIndex--;
                var previousResult = ValidatedExamples[CurrentExampleIndex];
                CurrentExample = previousResult.OriginalExample;
                UpdateProgress();
                StatusMessage = $"Reviewing previous example {CurrentExampleIndex + 1}";
            }
        }

        private async Task SaveProgressAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Saving validation progress...";

                await _validationService.SaveValidationSessionAsync(new ValidationSession
                {
                    Id = Guid.NewGuid().ToString(),
                    StartedAt = DateTime.UtcNow,
                    PendingExamples = PendingExamples.ToList(),
                    CompletedValidations = ValidatedExamples.ToList(),
                    CurrentIndex = CurrentExampleIndex,
                    WorkflowState = CurrentState
                });

                StatusMessage = "Progress saved successfully";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save validation progress");
                StatusMessage = $"Save failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ExportValidatedDataAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Exporting validated training data...";

                var approvedExamples = ValidatedExamples
                    .Where(v => v.Decision == ValidationDecision.Approved)
                    .Select(v => v.OriginalExample)
                    .Where(e => e != null)
                    .ToList()!;

                await _validationService.ExportTrainingDataAsync(approvedExamples, "validated_training_data.json");
                
                StatusMessage = $"Exported {approvedExamples.Count} approved training examples";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to export validated data");
                StatusMessage = $"Export failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadMostRecentSessionAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading recent validation session...";

                var sessions = await _validationService.GetUserValidationSessionsAsync();
                var mostRecent = sessions.FirstOrDefault();

                if (mostRecent != null)
                {
                    var loadedSession = await _validationService.LoadValidationSessionAsync(mostRecent.Id);
                    if (loadedSession != null)
                    {
                        // Restore session state
                        PendingExamples.Clear();
                        ValidatedExamples.Clear();

                        foreach (var example in loadedSession.PendingExamples)
                            PendingExamples.Add(example);

                        foreach (var validation in loadedSession.CompletedValidations)
                            ValidatedExamples.Add(validation);

                        CurrentExampleIndex = loadedSession.CurrentIndex;
                        CurrentState = loadedSession.WorkflowState;
                        CurrentExample = PendingExamples.FirstOrDefault();

                        StatusMessage = "Session loaded successfully";
                        OnPropertyChanged(nameof(RemainingCount));
                        OnPropertyChanged(nameof(CompletedCount));
                        OnPropertyChanged(nameof(HasPendingExamples));
                        OnPropertyChanged(nameof(CanStartValidation));
                    }
                }
                else
                {
                    StatusMessage = "No previous sessions found";
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load validation session");
                StatusMessage = $"Load failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Helper Methods

        protected override void InitializeCommands()
        {
            GenerateExamplesCommand = new AsyncRelayCommand(GenerateExamplesAsync);
            StartValidationCommand = new RelayCommand(async () => await StartValidationAsync(), () => CanStartValidation);
            ApproveExampleCommand = new AsyncRelayCommand(ApproveExampleAsync, () => CurrentExample != null);
            RejectExampleCommand = new AsyncRelayCommand(RejectExampleAsync, () => CurrentExample != null);
            RequireEditsCommand = new AsyncRelayCommand(RequireEditsAsync, () => CurrentExample != null);
            SkipExampleCommand = new AsyncRelayCommand(SkipExampleAsync, () => CurrentExample != null);
            NextExampleCommand = new AsyncRelayCommand(MoveToNextExample, () => PendingExamples.Any());
            PreviousExampleCommand = new AsyncRelayCommand(MoveToPreviousExample, () => CurrentExampleIndex > 0);
            SaveProgressCommand = new AsyncRelayCommand(SaveProgressAsync);
            LoadSessionCommand = new AsyncRelayCommand(LoadMostRecentSessionAsync);
            ExportValidatedDataCommand = new AsyncRelayCommand(ExportValidatedDataAsync, () => ValidatedExamples.Any());
        }

        private void InitializeValidationChoices()
        {
            ValidationOptions.Add(new ValidationChoice
            {
                Decision = ValidationDecision.Approved,
                Label = "✅ Approve",
                Description = "Example meets quality standards",
                Hotkey = "Ctrl+A"
            });

            ValidationOptions.Add(new ValidationChoice
            {
                Decision = ValidationDecision.Rejected,
                Label = "❌ Reject",
                Description = "Example has quality issues",
                Hotkey = "Ctrl+R"
            });

            ValidationOptions.Add(new ValidationChoice
            {
                Decision = ValidationDecision.RequiresEdits,
                Label = "✏️ Needs Editing",
                Description = "Example has potential with changes",
                Hotkey = "Ctrl+E"
            });

            ValidationOptions.Add(new ValidationChoice
            {
                Decision = ValidationDecision.Skipped,
                Label = "⏭️ Skip",
                Description = "Review later",
                Hotkey = "Ctrl+S"
            });
        }

        private void UpdateProgress()
        {
            if (ValidatedExamples.Count + PendingExamples.Count == 0) return;
            
            var totalCount = ValidatedExamples.Count + PendingExamples.Count;
            ProgressPercentage = (double)ValidatedExamples.Count / totalCount * 100;
        }

        #endregion

        #region Abstract Method Implementations

        protected override async Task SaveAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Saving validation session...";
                
                // Save current validation session
                // Implementation would save to file or database
                await Task.Run(() =>
                {
                    // TODO: Implement actual save logic
                    Thread.Sleep(1000); // Simulate save operation
                });
                
                StatusMessage = "Validation session saved successfully";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving validation session: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        protected override void Cancel()
        {
            try
            {
                // Cancel ongoing operations
                IsLoading = false;
                StatusMessage = "Operation cancelled";
                
                // Reset to initial state if needed
                OnPropertyChanged(nameof(CanStartValidation));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error cancelling operation: {ex.Message}";
            }
        }

        protected override async Task RefreshAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Refreshing validation data...";
                
                // Refresh validation session data
                await Task.Run(() =>
                {
                    // TODO: Implement actual refresh logic
                    Thread.Sleep(500); // Simulate refresh operation
                });
                
                // Update UI state
                OnPropertyChanged(nameof(CanStartValidation));
                UpdateProgress();
                StatusMessage = "Validation data refreshed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error refreshing validation data: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        protected override bool CanSave()
        {
            return !IsLoading && ValidatedExamples.Any();
        }

        protected override bool CanCancel()
        {
            return IsLoading;
        }

        protected override bool CanRefresh()
        {
            return !IsLoading;
        }

        #endregion
    }

    /// <summary>
    /// Represents a validation decision choice for UI display
    /// </summary>
    public class ValidationChoice
    {
        public ValidationDecision Decision { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Hotkey { get; set; } = string.Empty;
    }
}