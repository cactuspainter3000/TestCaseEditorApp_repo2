using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels
{
    /// <summary>
    /// ViewModel for LLM-based test case generation with intelligent requirement coverage.
    /// Handles batch generation, similarity detection, and coverage tracking.
    /// </summary>
    public class LLMTestCaseGeneratorViewModel : ObservableObject
    {
        private readonly ILogger<LLMTestCaseGeneratorViewModel> _logger;
        private readonly ITestCaseGenerationService _generationService;
        private readonly ITestCaseDeduplicationService _deduplicationService;
        private readonly IRequirementsMediator _requirementsMediator;
        
        private bool _isGenerating;
        private string _statusMessage = "Ready to generate test cases";
        private double _progress;
        private TestCaseCoverageSummary? _coverageSummary;
        private CancellationTokenSource? _cancellationTokenSource;

        public LLMTestCaseGeneratorViewModel(
            ILogger<LLMTestCaseGeneratorViewModel> logger,
            ITestCaseGenerationService generationService,
            ITestCaseDeduplicationService deduplicationService,
            IRequirementsMediator requirementsMediator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _generationService = generationService ?? throw new ArgumentNullException(nameof(generationService));
            _deduplicationService = deduplicationService ?? throw new ArgumentNullException(nameof(deduplicationService));
            _requirementsMediator = requirementsMediator ?? throw new ArgumentNullException(nameof(requirementsMediator));

            GeneratedTestCases = new ObservableCollection<LLMTestCase>();
            SimilarRequirementGroups = new ObservableCollection<RequirementGroup>();

            GenerateTestCasesCommand = new AsyncRelayCommand(GenerateTestCasesAsync, CanGenerate);
            GenerateForSelectionCommand = new AsyncRelayCommand(GenerateForSelectedRequirementsAsync, () => SelectedRequirements.Any());
            FindSimilarGroupsCommand = new AsyncRelayCommand(FindSimilarRequirementGroupsAsync, CanGenerate);
            DeduplicateTestCasesCommand = new AsyncRelayCommand(DeduplicateTestCasesAsync, () => GeneratedTestCases.Count > 1);
            CancelGenerationCommand = new RelayCommand(CancelGeneration, () => IsGenerating);
            ClearResultsCommand = new RelayCommand(ClearResults, () => GeneratedTestCases.Any());
        }

        // ===== PROPERTIES =====

        public ObservableCollection<LLMTestCase> GeneratedTestCases { get; }
        public ObservableCollection<RequirementGroup> SimilarRequirementGroups { get; }
        public List<Requirement> SelectedRequirements { get; set; } = new List<Requirement>();

        public bool IsGenerating
        {
            get => _isGenerating;
            private set
            {
                if (SetProperty(ref _isGenerating, value))
                {
                    OnPropertyChanged(nameof(CanModify));
                    GenerateTestCasesCommand.NotifyCanExecuteChanged();
                    GenerateForSelectionCommand.NotifyCanExecuteChanged();
                    FindSimilarGroupsCommand.NotifyCanExecuteChanged();
                    DeduplicateTestCasesCommand.NotifyCanExecuteChanged();
                    CancelGenerationCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool CanModify => !IsGenerating;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public TestCaseCoverageSummary? CoverageSummary
        {
            get => _coverageSummary;
            set
            {
                if (SetProperty(ref _coverageSummary, value))
                {
                    OnPropertyChanged(nameof(HasCoverageData));
                    OnPropertyChanged(nameof(CoveragePercentage));
                    OnPropertyChanged(nameof(CoveredCount));
                    OnPropertyChanged(nameof(UncoveredCount));
                }
            }
        }

        public bool HasCoverageData => CoverageSummary != null;
        public double CoveragePercentage => CoverageSummary?.CoveragePercentage ?? 0.0;
        public int CoveredCount => CoverageSummary?.CoveredRequirements ?? 0;
        public int UncoveredCount => CoverageSummary?.UncoveredRequirements ?? 0;

        // ===== COMMANDS =====

        public IAsyncRelayCommand GenerateTestCasesCommand { get; }
        public IAsyncRelayCommand GenerateForSelectionCommand { get; }
        public IAsyncRelayCommand FindSimilarGroupsCommand { get; }
        public IAsyncRelayCommand DeduplicateTestCasesCommand { get; }
        public IRelayCommand CancelGenerationCommand { get; }
        public IRelayCommand ClearResultsCommand { get; }

        // ===== COMMAND IMPLEMENTATIONS =====

        private async Task GenerateTestCasesAsync()
        {
            var requirements = _requirementsMediator.Requirements?.ToList() ?? new List<Requirement>();
            if (!requirements.Any())
            {
                StatusMessage = "No requirements available";
                return;
            }

            await GenerateTestCasesForRequirementsAsync(requirements);
        }

        private async Task GenerateForSelectedRequirementsAsync()
        {
            if (!SelectedRequirements.Any())
            {
                StatusMessage = "No requirements selected";
                return;
            }

            await GenerateTestCasesForRequirementsAsync(SelectedRequirements);
        }

        private async Task GenerateTestCasesForRequirementsAsync(List<Requirement> requirements)
        {
            IsGenerating = true;
            Progress = 0;
            StatusMessage = $"Generating test cases for {requirements.Count} requirements...";
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                _logger.LogInformation("Starting test case generation for {Count} requirements", requirements.Count);

                // Generate test cases with LLM similarity detection
                var testCases = await _generationService.GenerateTestCasesAsync(
                    requirements,
                    _cancellationTokenSource.Token);

                Progress = 100;

                if (testCases.Any())
                {
                    // Add to collection
                    foreach (var tc in testCases)
                    {
                        GeneratedTestCases.Add(tc);
                    }

                    // Calculate coverage
                    CoverageSummary = _generationService.CalculateCoverage(requirements, GeneratedTestCases);

                    StatusMessage = $"Generated {testCases.Count} test cases covering {CoveredCount}/{requirements.Count} requirements";
                    _logger.LogInformation("Generation complete: {TestCaseCount} test cases, {Coverage}% coverage",
                        testCases.Count, CoveragePercentage);
                }
                else
                {
                    StatusMessage = "No test cases generated";
                    _logger.LogWarning("No test cases generated for {Count} requirements", requirements.Count);
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Generation cancelled";
                _logger.LogInformation("Test case generation cancelled by user");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Generation failed: {ex.Message}";
                _logger.LogError(ex, "Failed to generate test cases");
            }
            finally
            {
                IsGenerating = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task FindSimilarRequirementGroupsAsync()
        {
            var requirements = _requirementsMediator.Requirements?.ToList() ?? new List<Requirement>();
            if (requirements.Count < 2)
            {
                StatusMessage = "Need at least 2 requirements to find similarities";
                return;
            }

            IsGenerating = true;
            StatusMessage = "Finding similar requirement groups...";
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                _logger.LogInformation("Finding similar requirement groups among {Count} requirements", requirements.Count);

                var groups = await _deduplicationService.FindSimilarRequirementGroupsAsync(
                    requirements,
                    0.7, // 70% similarity threshold
                    _cancellationTokenSource.Token);

                SimilarRequirementGroups.Clear();

                foreach (var group in groups)
                {
                    var reqGroup = new RequirementGroup
                    {
                        RequirementIds = group.ToList(),
                        Requirements = requirements.Where(r => group.Contains(r.Item)).ToList()
                    };
                    SimilarRequirementGroups.Add(reqGroup);
                }

                StatusMessage = $"Found {groups.Count} similar requirement groups";
                _logger.LogInformation("Found {GroupCount} similar requirement groups", groups.Count);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Similarity analysis cancelled";
                _logger.LogInformation("Similarity analysis cancelled by user");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Similarity analysis failed: {ex.Message}";
                _logger.LogError(ex, "Failed to find similar requirement groups");
            }
            finally
            {
                IsGenerating = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task DeduplicateTestCasesAsync()
        {
            if (GeneratedTestCases.Count < 2)
            {
                StatusMessage = "Need at least 2 test cases to deduplicate";
                return;
            }

            IsGenerating = true;
            StatusMessage = "Deduplicating test cases...";
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var originalCount = GeneratedTestCases.Count;
                _logger.LogInformation("Deduplicating {Count} test cases", originalCount);

                var deduplicated = await _deduplicationService.DeduplicateTestCasesAsync(
                    GeneratedTestCases,
                    _cancellationTokenSource.Token);

                GeneratedTestCases.Clear();
                foreach (var tc in deduplicated)
                {
                    GeneratedTestCases.Add(tc);
                }

                var removedCount = originalCount - deduplicated.Count;
                StatusMessage = $"Removed {removedCount} duplicate test cases ({deduplicated.Count} remaining)";
                _logger.LogInformation("Deduplication complete: {Original} → {Final} test cases",
                    originalCount, deduplicated.Count);

                // Recalculate coverage
                var requirements = _requirementsMediator.Requirements?.ToList() ?? new List<Requirement>();
                if (requirements.Any())
                {
                    CoverageSummary = _generationService.CalculateCoverage(requirements, GeneratedTestCases);
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Deduplication cancelled";
                _logger.LogInformation("Deduplication cancelled by user");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Deduplication failed: {ex.Message}";
                _logger.LogError(ex, "Failed to deduplicate test cases");
            }
            finally
            {
                IsGenerating = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void CancelGeneration()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling...";
            _logger.LogInformation("User cancelled generation");
        }

        private void ClearResults()
        {
            GeneratedTestCases.Clear();
            SimilarRequirementGroups.Clear();
            CoverageSummary = null;
            StatusMessage = "Results cleared";
            _logger.LogInformation("Cleared generation results");
        }

        private bool CanGenerate()
        {
            if (IsGenerating)
                return false;

            var requirements = _requirementsMediator.Requirements;
            return requirements != null && requirements.Any();
        }
    }

    /// <summary>
    /// Represents a group of similar requirements that should share test cases
    /// </summary>
    public class RequirementGroup
    {
        public List<string> RequirementIds { get; set; } = new List<string>();
        public List<Requirement> Requirements { get; set; } = new List<Requirement>();
        
        public string DisplayText => $"{Requirements.Count} similar requirements: {string.Join(", ", RequirementIds.Take(3))}{(RequirementIds.Count > 3 ? "..." : "")}";
    }
}
