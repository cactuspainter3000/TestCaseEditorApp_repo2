using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.Events;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Events;
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
        private readonly INewProjectMediator _newProjectMediator;
        private readonly IOpenProjectMediator _openProjectMediator;
        private readonly PromptDiagnosticsViewModel _promptDiagnostics;
        
        private bool _isGenerating;
        private string _statusMessage = "Ready to generate test cases";
        private double _progress;
        private string _progressCounter = string.Empty;
        private string _generationElapsedTime = string.Empty;
        private TestCaseCoverageSummary? _coverageSummary;
        private CancellationTokenSource? _cancellationTokenSource;
        private System.Timers.Timer? _generationTimer;
        private DateTime _generationStartTime;

        public LLMTestCaseGeneratorViewModel(
            ILogger<LLMTestCaseGeneratorViewModel> logger,
            ITestCaseGenerationService generationService,
            ITestCaseDeduplicationService deduplicationService,
            IRequirementsMediator requirementsMediator,
            INewProjectMediator newProjectMediator,
            IOpenProjectMediator openProjectMediator,
            PromptDiagnosticsViewModel promptDiagnostics)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _generationService = generationService ?? throw new ArgumentNullException(nameof(generationService));
            _deduplicationService = deduplicationService ?? throw new ArgumentNullException(nameof(deduplicationService));
            _requirementsMediator = requirementsMediator ?? throw new ArgumentNullException(nameof(requirementsMediator));
            _newProjectMediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
            _openProjectMediator = openProjectMediator ?? throw new ArgumentNullException(nameof(openProjectMediator));
            _promptDiagnostics = promptDiagnostics ?? throw new ArgumentNullException(nameof(promptDiagnostics));

            GeneratedTestCases = new ObservableCollection<LLMTestCase>();
            SimilarRequirementGroups = new ObservableCollection<RequirementGroup>();
            AvailableRequirements = new ObservableCollection<SelectableRequirement>();
            SavedTestCases = new ObservableCollection<TestCaseGroup>();

            // Subscribe to project lifecycle events
            _newProjectMediator.Subscribe<NewProjectEvents.ProjectCreated>(OnProjectCreated);
            _openProjectMediator.Subscribe<OpenProjectEvents.ProjectOpened>(OnProjectOpened);
            
            // Subscribe to requirements events to reload when requirements become available
            _requirementsMediator.Subscribe<RequirementsEvents.RequirementsImported>(OnRequirementsImported);
            _requirementsMediator.Subscribe<RequirementsEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
            
            // Subscribe to prompt diagnostics events
            _promptDiagnostics.ParseExternalResponse += OnParseExternalResponse;
            
            // Initialize workspace context for the generation service (in case project already open)
            InitializeWorkspaceContext();
            
            // Load requirements and wrap them for selection
            LoadAvailableRequirements();

            // Initialize saved test cases display
            LoadSavedTestCasesFromAllRequirements();

            GenerateTestCasesCommand = new AsyncRelayCommand(GenerateTestCasesAsync, CanGenerate);
            GenerateForSelectionCommand = new AsyncRelayCommand(GenerateForSelectedRequirementsAsync, CanGenerateForSelection);
            FindSimilarGroupsCommand = new AsyncRelayCommand(FindSimilarRequirementGroupsAsync, CanGenerate);
            DeduplicateTestCasesCommand = new AsyncRelayCommand(DeduplicateTestCasesAsync, () => GeneratedTestCases.Count > 1);
            CancelGenerationCommand = new RelayCommand(CancelGeneration, () => IsGenerating);
            ClearResultsCommand = new RelayCommand(ClearResults, () => GeneratedTestCases.Any());
            SelectAllCommand = new RelayCommand(SelectAll);
            ClearSelectionCommand = new RelayCommand(ClearSelection);
            RefreshSavedTestCasesCommand = new RelayCommand(RefreshSavedTestCases);
        }

        // ===== PROPERTIES =====

        public ObservableCollection<LLMTestCase> GeneratedTestCases { get; }
        public ObservableCollection<RequirementGroup> SimilarRequirementGroups { get; }
        public ObservableCollection<SelectableRequirement> AvailableRequirements { get; }
        public ObservableCollection<TestCaseGroup> SavedTestCases { get; }
        public PromptDiagnosticsViewModel PromptDiagnostics => _promptDiagnostics;
        
        public int SelectedCount => AvailableRequirements.Count(r => r.IsSelected);
        public int TotalCount => AvailableRequirements.Count;
        public int SavedTestCasesCount => SavedTestCases.Count;
        public int SavedRequirementsCount => SavedTestCases.Sum(g => g.AssociatedRequirements.Count);

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

        public string ProgressCounter
        {
            get => _progressCounter;
            set => SetProperty(ref _progressCounter, value);
        }

        public string GenerationElapsedTime
        {
            get => _generationElapsedTime;
            set => SetProperty(ref _generationElapsedTime, value);
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
        public IRelayCommand SelectAllCommand { get; }
        public IRelayCommand ClearSelectionCommand { get; }
        public IRelayCommand RefreshSavedTestCasesCommand { get; }

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

        private async Task GenerateTestCasesForRequirementsAsync(List<Requirement> requirements)
        {
            IsGenerating = true;
            Progress = 0;
            GeneratedTestCases.Clear();
            CoverageSummary = null;
            StatusMessage = $"Starting generation for {requirements.Count} requirements...";
            ProgressCounter = $"0/{requirements.Count}";
            StartGenerationTimer();
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                _logger.LogInformation("Starting test case generation for {Count} requirements", requirements.Count);

                // Safety check: verify workspace context before generation
                if (!HasValidWorkspaceContext())
                {
                    StatusMessage = "✕ Cannot generate test cases - AnythingLLM workspace not configured";
                    _logger.LogWarning("[LLMTestCaseGenerator] Cannot generate - no valid AnythingLLM workspace context");
                    return;
                }

                // Safety check: re-initialize workspace context before generation
                _logger.LogDebug("[LLMTestCaseGenerator] Re-initializing workspace context before generation...");
                InitializeWorkspaceContext();

                // Generate test cases with diagnostics
                var result = await _generationService.GenerateTestCasesWithDiagnosticsAsync(
                    requirements,
                    (message, current, total) => 
                    {
                        StatusMessage = message;
                        Progress = total > 0 ? (double)current / total * 100 : 0;
                        ProgressCounter = $"{current}/{total}";
                    },
                    _cancellationTokenSource.Token);

                // Update diagnostics
                _promptDiagnostics.UpdatePrompt(result.GeneratedPrompt, requirements.Count, DateTime.Now);
                _promptDiagnostics.UpdateAnythingLLMResponse(result.LLMResponse);

                Progress = 100;

                if (result.TestCases.Any())
                {
                    _logger.LogInformation("[LLMTestCaseGenerator] GENERATION SUCCESS: Generated {Count} test cases, preparing to attach to requirements", result.TestCases.Count);
                    
                    // Add to collection
                    foreach (var tc in result.TestCases)
                    {
                        GeneratedTestCases.Add(tc);
                        _logger.LogDebug("[LLMTestCaseGenerator] GENERATION: Added test case '{TestCaseId}' to UI collection. CoveredRequirementIds: [{RequirementIds}]", 
                            tc.Id ?? tc.Title, string.Join(", ", tc.CoveredRequirementIds ?? new List<string>()));
                    }

                    // CRITICAL: Attach test cases to their corresponding requirement objects
                    _logger.LogInformation("[LLMTestCaseGenerator] GENERATION: About to attach {TestCaseCount} test cases to {RequirementCount} requirements", 
                        result.TestCases.Count, requirements.Count);
                    AttachTestCasesToRequirements(result.TestCases, requirements);

                    // Calculate coverage
                    CoverageSummary = _generationService.CalculateCoverage(requirements, GeneratedTestCases);

                    StatusMessage = $"✓ Generated {result.TestCases.Count} test cases covering {CoveredCount}/{requirements.Count} requirements";
                    _logger.LogInformation("Generation complete: {TestCaseCount} test cases, {Coverage}% coverage",
                        result.TestCases.Count, CoveragePercentage);
                        
                    // Refresh saved test cases display after successful generation
                    LoadSavedTestCasesFromAllRequirements();
                }
                else
                {
                    StatusMessage = "⚠ No test cases generated - check logs for details (may be timeout, LLM issue, or parsing error)";
                    _logger.LogWarning("No test cases generated for {Count} requirements", requirements.Count);
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "⊘ Generation cancelled by user";
                _logger.LogInformation("Test case generation cancelled by user");
            }
            catch (Exception ex)
            {
                StatusMessage = $"✕ Generation failed: {ex.Message}";
                _logger.LogError(ex, "Failed to generate test cases");
            }
            finally
            {
                IsGenerating = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                ProgressCounter = string.Empty;
                StopGenerationTimer();
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
                    // CRITICAL: Clear old test cases and re-attach deduplicated test cases to requirements
                    ClearTestCasesFromRequirements(requirements);
                    AttachTestCasesToRequirements(deduplicated, requirements);

                    CoverageSummary = _generationService.CalculateCoverage(requirements, GeneratedTestCases);
                    
                    // Refresh saved test cases display after deduplication
                    LoadSavedTestCasesFromAllRequirements();
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

        private void StartGenerationTimer()
        {
            GenerationElapsedTime = string.Empty;
            _generationStartTime = DateTime.Now;

            _generationTimer?.Stop();
            _generationTimer?.Dispose();
            _generationTimer = new System.Timers.Timer(1000);
            _generationTimer.Elapsed += (_, _) =>
            {
                var elapsed = DateTime.Now - _generationStartTime;
                GenerationElapsedTime = $"{elapsed.TotalSeconds:F0}s";
            };
            _generationTimer.Start();
        }

        private void StopGenerationTimer()
        {
            _generationTimer?.Stop();
            _generationTimer?.Dispose();
            _generationTimer = null;

            if (_generationStartTime != default)
            {
                var totalElapsed = DateTime.Now - _generationStartTime;
                GenerationElapsedTime = $"Completed in {totalElapsed.TotalSeconds:F0}s";
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

        private bool CanGenerateForSelection()
        {
            if (IsGenerating)
                return false;

            return SelectedCount > 0;
        }

        private bool HasValidWorkspaceContext()
        {
            _logger.LogDebug("[LLMTestCaseGenerator] HasValidWorkspaceContext() - Starting validation");
            
            // Primary check: Does the generation service have workspace context?
            var hasServiceContext = _generationService.HasWorkspaceContext;
            _logger.LogDebug("[LLMTestCaseGenerator] Generation service has workspace context: {HasContext}", hasServiceContext);
            
            if (hasServiceContext)
            {
                _logger.LogDebug("[LLMTestCaseGenerator] Validation passed - using generation service workspace context");
                return true;
            }
            
            // Fallback check: IWorkspaceContext service
            var workspaceContext = App.ServiceProvider?.GetService(typeof(TestCaseEditorApp.Services.IWorkspaceContext)) 
                as TestCaseEditorApp.Services.IWorkspaceContext;
            
            _logger.LogDebug("[LLMTestCaseGenerator] IWorkspaceContext service available: {HasService}", workspaceContext != null);
            
            if (workspaceContext?.CurrentWorkspaceInfo?.AnythingLLMSlug != null)
            {
                var hasContextSlug = !string.IsNullOrEmpty(workspaceContext.CurrentWorkspaceInfo.AnythingLLMSlug);
                _logger.LogDebug("[LLMTestCaseGenerator] IWorkspaceContext has valid slug: {HasSlug}, Slug: {Slug}", 
                    hasContextSlug, workspaceContext.CurrentWorkspaceInfo.AnythingLLMSlug ?? "<null>");
                    
                return hasContextSlug;
            }
            
            // Final fallback to NewProjectMediator
            try
            {
                var workspaceInfo = _newProjectMediator.GetCurrentWorkspaceInfo();
                var hasWorkspaceInfo = workspaceInfo != null;
                var hasSlug = !string.IsNullOrEmpty(workspaceInfo?.AnythingLLMSlug);
                
                _logger.LogDebug("[LLMTestCaseGenerator] NewProjectMediator workspace info available: {HasInfo}, Has slug: {HasSlug}, Slug: {Slug}", 
                    hasWorkspaceInfo, hasSlug, workspaceInfo?.AnythingLLMSlug ?? "<null>");
                    
                return hasSlug;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[LLMTestCaseGenerator] Exception checking NewProjectMediator workspace info");
                return false;
            }
        }

        // ===== SELECTION MANAGEMENT =====

        private void InitializeWorkspaceContext()
        {
            _logger.LogDebug("[LLMTestCaseGenerator] InitializeWorkspaceContext called");
            
            // Method 1: Try to get workspace context from the IWorkspaceContext service
            var workspaceContext = App.ServiceProvider?.GetService(typeof(TestCaseEditorApp.Services.IWorkspaceContext)) 
                as TestCaseEditorApp.Services.IWorkspaceContext;
            
            _logger.LogDebug("[LLMTestCaseGenerator] IWorkspaceContext service: {HasService}", workspaceContext != null);
            
            if (workspaceContext != null)
            {
                var currentWorkspaceInfo = workspaceContext.CurrentWorkspaceInfo;
                _logger.LogDebug("[LLMTestCaseGenerator] CurrentWorkspaceInfo: {HasInfo}", currentWorkspaceInfo != null);
                
                if (currentWorkspaceInfo != null)
                {
                    _logger.LogDebug("[LLMTestCaseGenerator] WorkspaceInfo - Name: {Name}, Path: {Path}, AnythingLLMSlug: {Slug}",
                        currentWorkspaceInfo.Name ?? "<null>",
                        currentWorkspaceInfo.Path ?? "<null>", 
                        currentWorkspaceInfo.AnythingLLMSlug ?? "<null>");
                }
                
                var workspaceName = currentWorkspaceInfo?.AnythingLLMSlug;
                
                if (!string.IsNullOrEmpty(workspaceName))
                {
                    _generationService.SetWorkspaceContext(workspaceName);
                    _logger.LogInformation("[LLMTestCaseGenerator] Initialized workspace context to: {WorkspaceName}", workspaceName);
                    return;
                }
            }
            
            // Method 2: If IWorkspaceContext is null/empty, try to get from NewProjectMediator
            try
            {
                var currentWorkspaceInfo = _newProjectMediator.GetCurrentWorkspaceInfo();
                _logger.LogDebug("[LLMTestCaseGenerator] NewProjectMediator workspace info: {HasWorkspaceInfo}", currentWorkspaceInfo != null);
                
                if (currentWorkspaceInfo != null)
                {
                    _logger.LogDebug("[LLMTestCaseGenerator] Mediator WorkspaceInfo - Name: {Name}, Path: {Path}, AnythingLLMSlug: {Slug}",
                        currentWorkspaceInfo.Name ?? "<null>",
                        currentWorkspaceInfo.Path ?? "<null>", 
                        currentWorkspaceInfo.AnythingLLMSlug ?? "<null>");
                }
                
                var fallbackWorkspaceName = currentWorkspaceInfo?.AnythingLLMSlug;
                
                if (!string.IsNullOrEmpty(fallbackWorkspaceName))
                {
                    _generationService.SetWorkspaceContext(fallbackWorkspaceName);
                    _logger.LogInformation("[LLMTestCaseGenerator] Initialized workspace context from mediator to: {WorkspaceName}", fallbackWorkspaceName);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[LLMTestCaseGenerator] Failed to get workspace info from NewProjectMediator");
            }
            
            _logger.LogWarning("[LLMTestCaseGenerator] No AnythingLLM workspace found in current project context during initialization");
        }

        private void LoadAvailableRequirements()
        {
            AvailableRequirements.Clear();
            var requirements = _requirementsMediator.Requirements;
            
            if (requirements != null && requirements.Count > 0)
            {
                _logger.LogDebug("[LLMTestCaseGenerator] Loading {Count} requirements into dropdown", requirements.Count);
                foreach (var req in requirements)
                {
                    var selectable = new SelectableRequirement(req);
                    selectable.SelectionChanged += OnRequirementSelectionChanged;
                    AvailableRequirements.Add(selectable);
                }
                
                // Check workspace context for status message
                if (HasValidWorkspaceContext())
                {
                    StatusMessage = $"Ready to generate test cases ({AvailableRequirements.Count} requirements available)";
                }
                else
                {
                    StatusMessage = $"⚠ AnythingLLM workspace not configured - {AvailableRequirements.Count} requirements loaded";
                }
                
                _logger.LogInformation("[LLMTestCaseGenerator] Loaded {Count} requirements for selection", AvailableRequirements.Count);
            }
            else
            {
                StatusMessage = "No requirements available";
                _logger.LogDebug("[LLMTestCaseGenerator] No requirements available to load");
            }
            
            UpdateSelectionCounts();
        }

        private void OnRequirementsImported(RequirementsEvents.RequirementsImported evt)
        {
            _logger.LogInformation("[LLMTestCaseGenerator] Requirements imported, reloading dropdown");
            
            // Reinitialize workspace context (in case it changed or wasn't set initially)
            InitializeWorkspaceContext();
            
            LoadAvailableRequirements();
        }

        private void OnProjectCreated(NewProjectEvents.ProjectCreated evt)
        {
            _logger.LogInformation("[LLMTestCaseGenerator] Project created, setting workspace context: {WorkspaceName}", evt.WorkspaceName);
            
            // Set workspace context from the project created event
            if (!string.IsNullOrEmpty(evt.AnythingLLMWorkspaceSlug))
            {
                _generationService.SetWorkspaceContext(evt.AnythingLLMWorkspaceSlug);
                _logger.LogInformation("[LLMTestCaseGenerator] Set workspace context to: {WorkspaceName}", evt.AnythingLLMWorkspaceSlug);
            }
            else
            {
                _logger.LogWarning("[LLMTestCaseGenerator] Project created but no AnythingLLM workspace slug provided");
            }
            
            // Requirements will be loaded via RequirementsCollectionChanged event
        }

        private void OnProjectOpened(OpenProjectEvents.ProjectOpened evt)
        {
            _logger.LogInformation("[LLMTestCaseGenerator] Project opened - WorkspaceName: {WorkspaceName}, Path: {Path}, AnythingLLMSlug: {Slug}", 
                evt.WorkspaceName, evt.WorkspacePath, evt.AnythingLLMWorkspaceSlug ?? "<null>");
            
            // Set workspace context from the project opened event
            if (!string.IsNullOrEmpty(evt.AnythingLLMWorkspaceSlug))
            {
                _logger.LogDebug("[LLMTestCaseGenerator] Setting workspace context on generation service: {Slug}", evt.AnythingLLMWorkspaceSlug);
                _generationService.SetWorkspaceContext(evt.AnythingLLMWorkspaceSlug);
                _logger.LogInformation("[LLMTestCaseGenerator] Set workspace context to: {WorkspaceName}", evt.AnythingLLMWorkspaceSlug);
                _logger.LogDebug("[LLMTestCaseGenerator] Generation service now reports HasWorkspaceContext: {HasContext}", _generationService.HasWorkspaceContext);
            }
            else
            {
                _logger.LogWarning("[LLMTestCaseGenerator] Project opened but no AnythingLLM workspace slug provided in event");
            }
            
            // Requirements will be loaded via RequirementsCollectionChanged event
        }

        private void OnRequirementsCollectionChanged(RequirementsEvents.RequirementsCollectionChanged evt)
        {
            _logger.LogInformation("[LLMTestCaseGenerator] Requirements collection changed, reloading dropdown");
            LoadAvailableRequirements();
            
            // Also refresh saved test cases when requirements collection changes
            _logger.LogInformation("[LLMTestCaseGenerator] 🔍 SAVED TEST CASES DEBUG: Requirements collection changed, refreshing saved test cases");
            LoadSavedTestCasesFromAllRequirements();
        }

        private void OnRequirementSelectionChanged(object? sender, EventArgs e)
        {
            UpdateSelectionCounts();
            GenerateForSelectionCommand.NotifyCanExecuteChanged();
            // Don't filter saved test cases based on requirement selection - they should always be visible
        }

        private async void OnParseExternalResponse(object? sender, string response)
        {
            try
            {
                _logger.LogInformation("[LLMTestCaseGenerator] Parsing external LLM response ({Length} chars)", response.Length);
                
                // Use the generation service's existing parsing logic
                var selectedRequirements = AvailableRequirements
                    .Where(r => r.IsSelected)
                    .Select(r => r.Requirement)
                    .ToList();
                
                // If no requirements are selected, use all available requirements
                if (!selectedRequirements.Any())
                {
                    selectedRequirements = AvailableRequirements.Select(r => r.Requirement).ToList();
                }

                IsGenerating = true;
                StatusMessage = "Parsing external LLM response...";

                // Parse the external response using the same logic as normal generation
                var testCases = await Task.Run(() => ParseExternalLLMResponse(response, selectedRequirements));
                
                if (testCases.Any())
                {
                    // Clear existing results and add parsed test cases
                    GeneratedTestCases.Clear();
                    foreach (var testCase in testCases)
                    {
                        GeneratedTestCases.Add(testCase);
                    }

                    // CRITICAL: Attach test cases to their corresponding requirement objects
                    AttachTestCasesToRequirements(testCases, selectedRequirements);

                    StatusMessage = $"✅ Successfully parsed {testCases.Count} test cases from external LLM response";
                    
                    // Refresh saved test cases display after successful parsing
                    LoadSavedTestCasesFromAllRequirements();
                    
                    _logger.LogInformation("[LLMTestCaseGenerator] Successfully parsed {Count} test cases from external response", 
                        testCases.Count);
                }
                else
                {
                    StatusMessage = "⚠️ No test cases could be parsed from the external LLM response. Check the format.";
                    _logger.LogWarning("[LLMTestCaseGenerator] Failed to parse any test cases from external response");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Error parsing external response: {ex.Message}";
                _logger.LogError(ex, "[LLMTestCaseGenerator] Error parsing external LLM response");
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private void UpdateSelectionCounts()
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(SavedTestCasesCount));
            OnPropertyChanged(nameof(SavedRequirementsCount));
            // Only notify commands if they've been initialized (avoid NullReferenceException during construction)
            GenerateForSelectionCommand?.NotifyCanExecuteChanged();
            GenerateTestCasesCommand?.NotifyCanExecuteChanged();
            FindSimilarGroupsCommand?.NotifyCanExecuteChanged();
        }

        private void SelectAll()
        {
            foreach (var req in AvailableRequirements)
            {
                req.IsSelected = true;
            }
        }

        private void ClearSelection()
        {
            foreach (var req in AvailableRequirements)
            {
                req.IsSelected = false;
            }
        }

        private async Task GenerateForSelectedRequirementsAsync()
        {
            var selectedReqs = AvailableRequirements
                .Where(r => r.IsSelected)
                .Select(r => r.Requirement)
                .ToList();

            if (!selectedReqs.Any())
            {
                StatusMessage = "No requirements selected";
                return;
            }

            await GenerateTestCasesForRequirementsAsync(selectedReqs);
        }

        /// <summary>
        /// Parses an external LLM response into test cases using the same logic as the generation service
        /// </summary>
        private List<LLMTestCase> ParseExternalLLMResponse(string response, List<Requirement> requirements)
        {
            try
            {
                _logger.LogInformation("[LLMTestCaseGenerator] Attempting to parse external response using generation service");
                
                // Use reflection to access the private ParseTestCasesFromResponse method
                var serviceType = _generationService.GetType();
                var parseMethod = serviceType.GetMethod("ParseTestCasesFromResponse", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (parseMethod == null)
                {
                    _logger.LogError("[LLMTestCaseGenerator] Could not find ParseTestCasesFromResponse method in generation service");
                    return new List<LLMTestCase>();
                }

                // Invoke the method with the external response and requirements
                var result = parseMethod.Invoke(_generationService, new object[] { response, requirements });
                
                if (result is List<LLMTestCase> testCases)
                {
                    // Mark these as externally generated
                    foreach (var testCase in testCases)
                    {
                        testCase.CreatedBy = "External LLM";
                        testCase.Notes = $"{testCase.Notes}\n\n[Imported from external LLM on {DateTime.Now:yyyy-MM-dd HH:mm:ss}]".Trim();
                    }
                    
                    _logger.LogInformation("[LLMTestCaseGenerator] Successfully parsed {Count} test cases from external response", 
                        testCases.Count);
                    return testCases;
                }
                
                _logger.LogWarning("[LLMTestCaseGenerator] Parse method returned unexpected type: {Type}", 
                    result?.GetType().Name ?? "null");
                return new List<LLMTestCase>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LLMTestCaseGenerator] Failed to parse external LLM response");
                return new List<LLMTestCase>();
            }
        }

        /// <summary>
        /// Refresh saved test cases display for all requirements
        /// </summary>
        private void RefreshSavedTestCases()
        {
            _logger.LogDebug("[LLMTestCaseGenerator] Manual refresh triggered");
            SavedTestCases.Clear();
            LoadSavedTestCasesFromAllRequirements();
        }

        /// <summary>
        /// Load saved test cases only for selected requirements
        /// </summary>
        private void LoadSavedTestCasesForSelection()
        {
            SavedTestCases.Clear();
            
            _logger.LogInformation("[LLMTestCaseGenerator] 🔍 SAVED TEST CASES DEBUG: LoadSavedTestCasesForSelection called. Total available requirements: {Total}", 
                AvailableRequirements.Count);
            
            var selectedRequirements = AvailableRequirements
                .Where(r => r.IsSelected)
                .Select(r => r.Requirement)
                .ToList();

            _logger.LogInformation("[LLMTestCaseGenerator] 🔍 SAVED TEST CASES DEBUG: Found {Count} selected requirements in AvailableRequirements", 
                selectedRequirements.Count);
            
            if (selectedRequirements.Any())
            {
                LoadSavedTestCasesFromRequirements(selectedRequirements);
                _logger.LogInformation("[LLMTestCaseGenerator] 🔍 SAVED TEST CASES DEBUG: Loaded saved test cases for {Count} selected requirements", 
                    selectedRequirements.Count);
            }
            else
            {
                // Fallback: If no requirements are explicitly selected, try loading from all requirements
                // This handles cases where requirement selection happens outside our AvailableRequirements collection
                _logger.LogInformation("[LLMTestCaseGenerator] 🔍 SAVED TEST CASES DEBUG: No requirements selected via AvailableRequirements, trying all requirements as fallback");
                
                var allRequirements = AvailableRequirements.Select(r => r.Requirement).ToList();
                LoadSavedTestCasesFromRequirements(allRequirements);
            }
        }

        /// <summary>
        /// Load saved test cases from all available requirements
        /// </summary>
        private void LoadSavedTestCasesFromAllRequirements()
        {
            // Get requirements directly from the mediator to avoid any UI filtering or timing issues
            var allRequirements = _requirementsMediator.Requirements?.ToList() ?? new List<Requirement>();
            
            _logger.LogDebug("[LLMTestCaseGenerator] LoadSavedTestCasesFromAllRequirements: Getting {Count} requirements from mediator", allRequirements.Count);

            LoadSavedTestCasesFromRequirements(allRequirements);
        }

        /// <summary>
        /// Load saved test cases from specified requirements
        /// </summary>
        private void LoadSavedTestCasesFromRequirements(List<Requirement> requirements)
        {
            // Clear existing saved test cases to prevent duplicates
            SavedTestCases.Clear();
            
            _logger.LogInformation("[LLMTestCaseGenerator] 🔍 SAVED TEST CASES DEBUG: LoadSavedTestCasesFromRequirements called with {RequirementCount} requirements", 
                requirements.Count);
            
            // Dictionary to group test cases by their ID and track associated requirements
            var testCaseGroups = new Dictionary<string, (TestCase TestCase, List<Requirement> Requirements)>();
            
            // Collect all test cases and their associated requirements
            foreach (var requirement in requirements)
            {
                var reqId = requirement.GlobalId ?? requirement.Name ?? "Unknown";
                var testCaseCount = requirement.GeneratedTestCases?.Count ?? 0;
                
                _logger.LogInformation("[LLMTestCaseGenerator] 🔍 SAVED TEST CASES DEBUG: Checking requirement {RequirementId}: has {TestCaseCount} generated test cases", 
                    reqId, testCaseCount);
                
                if (requirement.GeneratedTestCases != null && requirement.GeneratedTestCases.Any())
                {
                    foreach (var testCase in requirement.GeneratedTestCases)
                    {
                        var testCaseId = testCase.Id ?? testCase.Name ?? "Unknown";
                        var testCaseName = testCase.Name ?? "Unnamed Test Case";
                        
                        _logger.LogInformation("[LLMTestCaseGenerator] 🔍 SAVED TEST CASES DEBUG: Found test case: {TestCaseId} - {TestCaseName}", 
                            testCaseId, testCaseName);
                        
                        if (testCaseGroups.ContainsKey(testCaseId))
                        {
                            // Add this requirement to existing test case group
                            testCaseGroups[testCaseId].Requirements.Add(requirement);
                        }
                        else
                        {
                            // Create new test case group with this requirement
                            testCaseGroups[testCaseId] = (testCase, new List<Requirement> { requirement });
                        }
                    }
                    
                    _logger.LogInformation("[LLMTestCaseGenerator] 🔍 SAVED TEST CASES DEBUG: Processed {TestCaseCount} test cases from requirement {RequirementId}", 
                        requirement.GeneratedTestCases.Count, reqId);
                }
                else
                {
                    _logger.LogInformation("[LLMTestCaseGenerator] 🔍 SAVED TEST CASES DEBUG: Requirement {RequirementId} has no generated test cases", reqId);
                }
            }
            
            // Create the hierarchical structure: Test Case -> Associated Requirements
            foreach (var kvp in testCaseGroups)
            {
                var testCaseGroup = new TestCaseGroup(kvp.Value.TestCase);
                
                foreach (var associatedRequirement in kvp.Value.Requirements)
                {
                    testCaseGroup.AddRequirement(associatedRequirement);
                }
                
                SavedTestCases.Add(testCaseGroup);
            }

            // Notify UI about count changes
            OnPropertyChanged(nameof(SavedTestCasesCount));
            OnPropertyChanged(nameof(SavedRequirementsCount));

            _logger.LogInformation("[LLMTestCaseGenerator] Loaded {TestCaseCount} unique test cases from {RequirementCount} requirements, {TotalAssociations} total associations", 
                SavedTestCases.Count, requirements.Count, SavedTestCases.Sum(g => g.AssociatedRequirements.Count));
        }

        /// <summary>
        /// Attaches generated test cases to their corresponding requirement objects based on CoveredRequirementIds.
        /// This ensures test cases are available for saved test cases display.
        /// </summary>
        private void AttachTestCasesToRequirements(List<LLMTestCase> testCases, List<Requirement> requirements)
        {
            _logger.LogInformation("[LLMTestCaseGenerator] ATTACHMENT: Starting attachment process. TestCases={TestCaseCount}, Requirements={RequirementCount}", 
                testCases.Count, requirements.Count);
            
            var requirementLookup = requirements.ToDictionary(r => r.Item, r => r);
            _logger.LogDebug("[LLMTestCaseGenerator] ATTACHMENT: Built requirement lookup with keys: [{RequirementKeys}]", 
                string.Join(", ", requirementLookup.Keys));
            
            int attachedCount = 0;

            foreach (var testCase in testCases)
            {
                var testCaseId = testCase.Id ?? testCase.Title ?? "Unknown";
                _logger.LogDebug("[LLMTestCaseGenerator] ATTACHMENT: Processing test case '{TestCaseId}'", testCaseId);
                
                if (testCase.CoveredRequirementIds?.Any() == true)
                {
                    _logger.LogDebug("[LLMTestCaseGenerator] ATTACHMENT: Test case '{TestCaseId}' covers {Count} requirements: [{RequirementIds}]", 
                        testCaseId, testCase.CoveredRequirementIds.Count, string.Join(", ", testCase.CoveredRequirementIds));
                    
                    foreach (var requirementId in testCase.CoveredRequirementIds)
                    {
                        if (requirementLookup.TryGetValue(requirementId, out var requirement))
                        {
                            // Create a simple TestCase wrapper for the LLMTestCase
                            var simpleTestCase = CreateTestCaseFromLLMTestCase(testCase);
                            
                            // Add test case if not already present
                            if (!requirement.GeneratedTestCases.Any(tc => 
                                tc.Id == simpleTestCase.Id || (tc.Name == simpleTestCase.Name && tc.Name != null)))
                            {
                                requirement.GeneratedTestCases.Add(simpleTestCase);
                                attachedCount++;
                                _logger.LogInformation("[LLMTestCaseGenerator] ATTACHMENT: ✅ Successfully attached test case '{TestCaseId}' to requirement '{RequirementId}'. Requirement now has {Count} test cases.", 
                                    testCaseId, requirementId, requirement.GeneratedTestCases.Count);
                            }
                            else
                            {
                                _logger.LogWarning("[LLMTestCaseGenerator] ATTACHMENT: Test case '{TestCaseId}' already attached to requirement '{RequirementId}'", 
                                    testCaseId, requirementId);
                            }
                        }
                        else
                        {
                            _logger.LogError("[LLMTestCaseGenerator] ATTACHMENT: ❌ Test case '{TestCaseId}' references unknown requirement '{RequirementId}'. Available: [{AvailableIds}]", 
                                testCaseId, requirementId, string.Join(", ", requirementLookup.Keys));
                        }
                    }
                }
                else
                {
                    _logger.LogError("[LLMTestCaseGenerator] ATTACHMENT: ❌ Test case '{TestCaseId}' has no CoveredRequirementIds! Cannot attach to any requirements.", testCaseId);
                }
            }

            _logger.LogInformation("[LLMTestCaseGenerator] ATTACHMENT: Completed attachment process. Successfully attached {AttachedCount} test case associations to requirements", attachedCount);
        }

        /// <summary>
        /// Creates a simple TestCase wrapper from an LLMTestCase for compatibility with the requirements system.
        /// </summary>
        private TestCase CreateTestCaseFromLLMTestCase(LLMTestCase llmTestCase)
        {
            return new TestCase
            {
                Id = llmTestCase.Id,
                Name = llmTestCase.Title,
                TestCaseText = llmTestCase.Description,
                // Additional properties can be mapped as needed
            };
        }

        /// <summary>
        /// Clears all generated test cases from requirement objects.
        /// Used before re-attaching test cases after operations like deduplication.
        /// </summary>
        private void ClearTestCasesFromRequirements(List<Requirement> requirements)
        {
            int clearedCount = 0;
            foreach (var requirement in requirements) 
            {
                if (requirement.GeneratedTestCases?.Any() == true)
                {
                    clearedCount += requirement.GeneratedTestCases.Count;
                    requirement.GeneratedTestCases.Clear();
                }
            }
            _logger.LogDebug("[LLMTestCaseGenerator] Cleared {ClearedCount} test case associations from requirements", clearedCount);
        }
    }

    /// <summary>
    /// Wrapper class for requirements to support selection in UI
    /// </summary>
    public class SelectableRequirement : ObservableObject
    {
        private bool _isSelected;
        
        public SelectableRequirement(Requirement requirement)
        {
            Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        }

        public Requirement Requirement { get; }
        
        public string Item => Requirement.Item;
        public string Description => Requirement.Description ?? string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public event EventHandler? SelectionChanged;
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
