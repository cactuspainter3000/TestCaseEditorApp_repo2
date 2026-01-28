using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Focused ViewModel for requirement analysis UI concerns in the Requirements domain.
    /// 
    /// This replaces the monolithic TestCaseGenerator_AnalysisVM with a clean, focused implementation
    /// that follows proper MVVM principles:
    /// - UI state management only
    /// - Business logic delegated to services
    /// - Proper dependency injection
    /// - Clear separation of concerns
    /// 
    /// Business logic is handled by IRequirementAnalysisEngine, making this ViewModel lightweight and testable.
    /// </summary>
    public partial class RequirementAnalysisViewModel : ObservableObject
    {
        private readonly IRequirementAnalysisEngine _analysisEngine;
        private readonly ILogger<RequirementAnalysisViewModel> _logger;
        private readonly TestCaseEditorApp.Services.IEditDetectionService? _editDetectionService;
        private readonly TestCaseEditorApp.Services.ILLMLearningService? _learningService;
        private CancellationTokenSource? _analysisCancellation;

        // Timer for tracking analysis duration
        private System.Timers.Timer? _analysisTimer;
        private DateTime _analysisStartTime;
        
        // Smart clipboard functionality
        private System.Windows.Threading.DispatcherTimer? _clipboardMonitorTimer;
        private string _lastClipboardContent = string.Empty;
        private string? _pendingExternalAnalysis;
        private bool _isWaitingForExternalResponse;

        // UI State Properties  
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoAnalysis))]
        private bool hasAnalysis;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoAnalysis))]
        private bool isAnalyzing;

        [ObservableProperty]
        private string analysisStatusMessage = string.Empty;

        [ObservableProperty]
        private int qualityScore;

        [ObservableProperty]
        private List<AnalysisIssue> issues = new();

        [ObservableProperty]
        private List<AnalysisRecommendation> recommendations = new();

        [ObservableProperty]
        private string freeformFeedback = string.Empty;

        [ObservableProperty]
        private string analysisTimestamp = string.Empty;

        [ObservableProperty]
        private string? improvedRequirement;

        [ObservableProperty]
        private bool hasImprovedRequirement;

        [ObservableProperty]
        private string engineStatusText = string.Empty;

        [ObservableProperty]
        private string analysisElapsedTime = string.Empty;

        [ObservableProperty] 
        private bool isEditingRequirement;

        [ObservableProperty]
        private string editingRequirementText = string.Empty;

        // Smart clipboard button text
        [ObservableProperty]
        private string copyAnalysisButtonText = "LLM Analysis Request → Clipboard";
        
        // Computed properties for UI binding
        public bool HasNoAnalysis => !HasAnalysis && !IsAnalyzing;
        public bool HasFreeformFeedback => !string.IsNullOrWhiteSpace(FreeformFeedback);
        public bool HasRecommendations => Recommendations?.Count > 0;
        public bool HasIssues => Issues?.Count > 0;
        public bool ShouldHideCopyButton => false; // Requirements domain doesn't have unsaved editing changes concept
        
        // Override property change notifications to trigger HasNoAnalysis updates
        partial void OnHasAnalysisChanged(bool value)
        {
            OnPropertyChanged(nameof(HasNoAnalysis));
        }
        
        partial void OnIsAnalyzingChanged(bool value)
        {
            OnPropertyChanged(nameof(HasNoAnalysis));
            
            // Start/stop timer when analysis state changes
            if (value)
            {
                StartAnalysisTimer();
            }
            else
            {
                StopAnalysisTimer();
            }
        }

        partial void OnIsEditingRequirementChanged(bool value)
        {
            ((RelayCommand)CancelEditRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)SaveRequirementCommand).NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CopyAnalysisButtonText));
        }

        partial void OnEditingRequirementTextChanged(string value)
        {
            ((RelayCommand)SaveRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CopyAnalysisPromptCommand).NotifyCanExecuteChanged();
            UpdateCopyButtonText();
        }

        // Current requirement being analyzed
        private Requirement? _currentRequirement;
        public Requirement? CurrentRequirement 
        { 
            get => _currentRequirement;
            set
            {
                _logger.LogInformation("[RequirementAnalysisVM] CurrentRequirement setter called. Old={OldReq}, New={NewReq}", 
                    _currentRequirement?.Item ?? "null", value?.Item ?? "null");
                
                if (SetProperty(ref _currentRequirement, value))
                {
                    // Event-driven: Let property change notifications handle UI updates
                    OnPropertyChanged(nameof(HasAnalysis));
                    OnPropertyChanged(nameof(HasNoAnalysis));
                    ((AsyncRelayCommand)AnalyzeRequirementCommand).NotifyCanExecuteChanged();
                    
                    // Update UI state based on current requirement's analysis
                    RefreshAnalysisDisplay();
                }
            }
        }

        // Commands
        public ICommand AnalyzeRequirementCommand { get; }
        public ICommand CancelAnalysisCommand { get; }
        public ICommand RefreshEngineStatusCommand { get; }
        public ICommand EditRequirementCommand { get; }
        public ICommand CancelEditRequirementCommand { get; }
        public ICommand SaveRequirementCommand { get; }
        public ICommand CommitImprovedRequirementCommand { get; }
        public ICommand CopyAnalysisPromptCommand { get; }

        public RequirementAnalysisViewModel(
            IRequirementAnalysisEngine analysisEngine,
            ILogger<RequirementAnalysisViewModel> logger,
            TestCaseEditorApp.Services.IEditDetectionService? editDetectionService = null,
            TestCaseEditorApp.Services.ILLMLearningService? learningService = null)
        {
            _analysisEngine = analysisEngine ?? throw new ArgumentNullException(nameof(analysisEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _editDetectionService = editDetectionService;
            _learningService = learningService;

            // Initialize commands
            AnalyzeRequirementCommand = new AsyncRelayCommand(AnalyzeRequirementAsync, CanAnalyzeRequirement);
            CancelAnalysisCommand = new RelayCommand(CancelAnalysis, () => IsAnalyzing);
            RefreshEngineStatusCommand = new RelayCommand(RefreshEngineStatus);
            EditRequirementCommand = new RelayCommand(StartEditingRequirement, CanEditRequirement);
            CancelEditRequirementCommand = new RelayCommand(CancelEditingRequirement, () => IsEditingRequirement);
            SaveRequirementCommand = new RelayCommand(SaveRequirementEdit, CanSaveRequirement);
            CommitImprovedRequirementCommand = new RelayCommand(CommitImprovedRequirement, CanCommitImprovement);
            CopyAnalysisPromptCommand = new RelayCommand(CopyToClipboard, CanCopyToClipboard);

            // Subscribe to requirement navigation and analysis events from Requirements mediator
            var mediator = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator>();
            if (mediator != null)
            {
                mediator.Subscribe<TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.RequirementSelected>(OnRequirementSelected);
                mediator.Subscribe<TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.RequirementAnalyzed>(OnRequirementAnalyzed);
                mediator.Subscribe<TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.RequirementUpdated>(OnRequirementUpdatedEvent);
                _logger.LogInformation("[RequirementAnalysisVM] Subscribed to RequirementSelected, RequirementAnalyzed, and RequirementUpdated events from Requirements mediator");
                
                // Initialize from mediator's current state (handles late-binding after project already loaded)
                if (mediator.CurrentRequirement != null)
                {
                    _logger.LogInformation("[RequirementAnalysisVM] Initializing from mediator's current requirement: {Item}", mediator.CurrentRequirement.Item);
                    CurrentRequirement = mediator.CurrentRequirement;
                }
            }
            else
            {
                _logger.LogWarning("[RequirementAnalysisVM] Could not subscribe to mediator - Requirements mediator not found in DI");
            }

            // Initialize engine status
            RefreshEngineStatus();
        }

        /// <summary>
        /// Analyzes the current requirement using the analysis engine.
        /// Demonstrates proper separation: ViewModel handles UI state, engine handles business logic.
        /// </summary>
        private async Task AnalyzeRequirementAsync()
        {
            _logger.LogInformation("[RequirementAnalysisVM] AnalyzeRequirementAsync called. CurrentRequirement={HasRequirement}", CurrentRequirement != null);
            
            if (CurrentRequirement == null) 
            {
                _logger.LogWarning("[RequirementAnalysisVM] Cannot analyze: no requirement selected");
                
                // Try to find the selected requirement from the service provider
                try
                {
                    var mainViewModel = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_MainViewModel>();
                    if (mainViewModel?.SelectedRequirement != null)
                    {
                        _logger.LogInformation("[RequirementAnalysisVM] Found selected requirement from MainViewModel: {RequirementItem}", mainViewModel.SelectedRequirement.Item);
                        CurrentRequirement = mainViewModel.SelectedRequirement;
                    }
                    else
                    {
                        _logger.LogWarning("[RequirementAnalysisVM] MainViewModel SelectedRequirement is also null");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RequirementAnalysisVM] Error trying to get selected requirement from MainViewModel");
                }
                
                // If still null after trying to sync, show error
                if (CurrentRequirement == null)
                {
                    AnalysisStatusMessage = "Please select a requirement from the list to analyze";
                    OnPropertyChanged(nameof(AnalysisStatusMessage));
                    return;
                }
            }

            _logger.LogDebug("[RequirementAnalysisVM] Starting analysis for {RequirementId}", CurrentRequirement.Item);

            try
            {
                // Create cancellation token for this analysis
                _analysisCancellation?.Cancel();
                _analysisCancellation = new CancellationTokenSource();

                // Update UI state
                IsAnalyzing = true;
                HasAnalysis = false;
                AnalysisStatusMessage = "Initializing analysis...";
                
                // Start clipboard monitoring for external LLM workflow
                StartClipboardMonitoring();

                // Delegate business logic to the analysis engine
                var analysis = await _analysisEngine.AnalyzeRequirementAsync(
                    CurrentRequirement,
                    progressMessage => 
                    {
                        // Update UI with progress from the engine
                        AnalysisStatusMessage = progressMessage;
                    },
                    _analysisCancellation.Token);

                // Update UI with results
                if (analysis.IsAnalyzed)
                {
                    _logger.LogDebug("[RequirementAnalysisVM] Analysis completed for {RequirementId}", CurrentRequirement.Item);
                    UpdateUIFromAnalysis(analysis);
                    AnalysisStatusMessage = string.Empty; // Clear status on success
                }
                else
                {
                    _logger.LogWarning("[RequirementAnalysisVM] Analysis failed for {RequirementId}: {Error}", 
                        CurrentRequirement.Item, analysis.ErrorMessage);
                    AnalysisStatusMessage = analysis.ErrorMessage ?? "Analysis failed";
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("[RequirementAnalysisVM] Analysis cancelled for {RequirementId}", CurrentRequirement?.Item);
                AnalysisStatusMessage = "Analysis cancelled";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisVM] Unexpected error during analysis");
                AnalysisStatusMessage = "Analysis failed due to unexpected error";
            }
            finally
            {
                IsAnalyzing = false;
                ((RelayCommand)CancelAnalysisCommand).NotifyCanExecuteChanged();
                RefreshEngineStatus(); // Update engine status after analysis
            }
        }

        /// <summary>
        /// Updates the UI properties from an analysis result.
        /// Pure UI logic - no business rules here.
        /// </summary>
        private void UpdateUIFromAnalysis(RequirementAnalysis analysis)
        {
            _logger.LogInformation("[RequirementAnalysisVM] UpdateUIFromAnalysis called with OriginalQualityScore: {OriginalScore}, ImprovedQualityScore: {ImprovedScore}", 
                analysis.OriginalQualityScore, analysis.ImprovedQualityScore);
            
            // DEBUG: Log the actual analysis object properties to see what we're getting
            _logger.LogWarning("[RequirementAnalysisVM] DEBUG SCORE INVESTIGATION - OriginalQualityScore: {Original}, ImprovedQualityScore: {Improved}, Legacy QualityScore property: {Legacy}",
                analysis.OriginalQualityScore, analysis.ImprovedQualityScore, analysis.OriginalQualityScore); // Use OriginalQualityScore instead of obsolete QualityScore
            
            HasAnalysis = true;
            
            // Ensure we're showing the ORIGINAL requirement score, not the LLM's self-rated improved score
            QualityScore = analysis.OriginalQualityScore; // This should be the user's original requirement quality
            
            // Log what we're actually displaying
            _logger.LogInformation("[RequirementAnalysisVM] Displaying QualityScore: {DisplayScore} (should be original, not improved)", QualityScore);
            
            Issues = analysis.Issues ?? new List<AnalysisIssue>();
            Recommendations = analysis.Recommendations ?? new List<AnalysisRecommendation>();
            OnPropertyChanged(nameof(HasRecommendations)); // Update computed property
            OnPropertyChanged(nameof(HasIssues)); // Update computed property
            FreeformFeedback = analysis.FreeformFeedback ?? string.Empty;
            ImprovedRequirement = analysis.ImprovedRequirement;
            HasImprovedRequirement = !string.IsNullOrWhiteSpace(analysis.ImprovedRequirement);
            AnalysisTimestamp = $"Analyzed on {analysis.Timestamp:MMM d, yyyy 'at' h:mm tt}";

            _logger.LogDebug("[RequirementAnalysisVM] UI updated with analysis results");
        }

        /// <summary>
        /// Refreshes the analysis display when the current requirement changes.
        /// </summary>
        private void RefreshAnalysisDisplay()
        {
            var analysis = CurrentRequirement?.Analysis;

            _logger.LogDebug("[RequirementAnalysisVM] RefreshAnalysisDisplay for {RequirementId}: IsAnalyzed={IsAnalyzed}", 
                CurrentRequirement?.Item ?? "null", 
                analysis?.IsAnalyzed ?? false);

            if (analysis?.IsAnalyzed == true)
            {
                UpdateUIFromAnalysis(analysis);
            }
            else
            {
                // Clear display for requirements without analysis - ensure complete state reset
                HasAnalysis = false;
                QualityScore = 0;
                Issues = new List<AnalysisIssue>();
                Recommendations = new List<AnalysisRecommendation>();
                OnPropertyChanged(nameof(HasRecommendations)); // Update computed property
                OnPropertyChanged(nameof(HasIssues)); // Update computed property
                FreeformFeedback = string.Empty;
                ImprovedRequirement = null;
                HasImprovedRequirement = false;
                AnalysisTimestamp = string.Empty;
                AnalysisStatusMessage = string.Empty; // Critical: Clear any stale error messages
                
                // Clear any stale analysis data from the requirement to prevent UI inconsistencies
                if (CurrentRequirement?.Analysis != null && !CurrentRequirement.Analysis.IsAnalyzed)
                {
                    CurrentRequirement.Analysis.ErrorMessage = null; // Clear persisted error state
                }
                
                _logger.LogDebug("[RequirementAnalysisVM] Cleared display state for {RequirementId}", CurrentRequirement?.Item);
            }
        }

        /// <summary>
        /// Cancels the current analysis operation.
        /// </summary>
        private void CancelAnalysis()
        {
            _logger.LogDebug("[RequirementAnalysisVM] User requested analysis cancellation");
            _analysisCancellation?.Cancel();
        }

        /// <summary>
        /// Updates the engine status display.
        /// </summary>
        private void RefreshEngineStatus()
        {
            try
            {
                var status = _analysisEngine.GetEngineStatus();
                EngineStatusText = status.IsHealthy ? status.StatusMessage : $"⚠️ {status.StatusMessage}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisVM] Failed to refresh engine status");
                EngineStatusText = "Engine status unavailable";
            }
        }

        /// <summary>
        /// Determines if analysis can be performed.
        /// </summary>
        private bool CanAnalyzeRequirement()
        {
            // Always allow the button to be clicked - we'll handle the no-requirement case in the command itself
            return !IsAnalyzing;
        }

        /// <summary>
        /// Gets diagnostic information for the current requirement.
        /// Useful for debugging and troubleshooting.
        /// </summary>
        public string GetDiagnosticInfo()
        {
            if (CurrentRequirement == null)
                return "No requirement selected";

            try
            {
                return _analysisEngine.GeneratePromptForInspection(CurrentRequirement);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisVM] Failed to generate diagnostic info");
                return $"Diagnostic generation failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Starts editing the current requirement.
        /// Puts the UI into edit mode for requirement refinement.
        /// </summary>
        private void StartEditingRequirement()
        {
            EditingRequirementText = ImprovedRequirement ?? string.Empty;
            IsEditingRequirement = true;
            _logger.LogDebug("[RequirementAnalysisVM] Started editing requirement");
        }

        /// <summary>
        /// Cancels requirement editing and returns to read-only mode.
        /// </summary>
        private void CancelEditingRequirement()
        {
            EditingRequirementText = string.Empty;
            IsEditingRequirement = false;
            
            // Clear any pending external analysis since edit was cancelled
            _pendingExternalAnalysis = null;
            
            _logger.LogDebug("[RequirementAnalysisVM] Cancelled requirement editing");
        }

        /// <summary>
        /// Determines if requirement editing is allowed.
        /// </summary>
        private bool CanEditRequirement()
        {
            return HasImprovedRequirement && !IsAnalyzing;
        }

        private void SaveRequirementEdit()
        {
            if (CurrentRequirement?.Analysis != null && !string.IsNullOrWhiteSpace(EditingRequirementText))
            {
                var newImprovedText = EditingRequirementText.Trim();
                var originalImprovedText = CurrentRequirement.Analysis.ImprovedRequirement ?? string.Empty;
                
                // Update the improved requirement
                CurrentRequirement.Analysis.ImprovedRequirement = newImprovedText;
                ImprovedRequirement = newImprovedText;
                HasImprovedRequirement = true;
                
                // Check for learning feedback if we have significant changes
                if (newImprovedText != originalImprovedText)
                {
                    // If this was from external analysis, use the external learning workflow
                    if (!string.IsNullOrWhiteSpace(_pendingExternalAnalysis))
                    {
                        _ = FeedLearningToAnythingLLM(_pendingExternalAnalysis);
                        _pendingExternalAnalysis = null; // Clear after processing
                    }
                    else
                    {
                        // Regular text edit detection
                        _ = CheckForLearningFeedbackAsync(originalImprovedText, newImprovedText, "improved requirement");
                    }
                }
                
                IsEditingRequirement = false;
                EditingRequirementText = string.Empty;
                
                // Publish RequirementUpdated event so mediator marks workspace as dirty
                var mediator = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator>();
                if (mediator != null)
                {
                    mediator.PublishEvent(new TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.RequirementUpdated
                    {
                        Requirement = CurrentRequirement,
                        ModifiedFields = new List<string> { "Analysis.ImprovedRequirement" },
                        UpdatedBy = "RequirementAnalysisViewModel"
                    });
                    _logger.LogInformation("[RequirementAnalysisVM] Published RequirementUpdated event for requirement: {RequirementId}", CurrentRequirement.Item);
                }
                
                _logger.LogInformation("[RequirementAnalysisVM] Saved edited requirement text");
            }
        }

        private bool CanSaveRequirement()
        {
            return IsEditingRequirement && !string.IsNullOrWhiteSpace(EditingRequirementText);
        }

        private void CommitImprovedRequirement()
        {
            if (CurrentRequirement == null || string.IsNullOrWhiteSpace(ImprovedRequirement))
            {
                _logger.LogWarning("[RequirementAnalysisVM] Cannot commit improved requirement - missing data");
                return;
            }

            try
            {
                _logger.LogInformation("[RequirementAnalysisVM] Committing improved requirement for {RequirementId}", CurrentRequirement.Item);
                
                // Update the requirement description with the improved version
                var originalDescription = CurrentRequirement.Description;
                CurrentRequirement.Description = ImprovedRequirement;
                CurrentRequirement.Analysis.ImprovedRequirement = null; // Clear the improved version
                ImprovedRequirement = null;
                HasImprovedRequirement = false;
                
                // Mediator publishes RequirementUpdated event which handles all downstream updates
                var mediator = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator>();
                if (mediator != null)
                {
                    mediator.UpdateRequirement(CurrentRequirement, new[] { "Description" });
                }
                
                _logger.LogInformation("[RequirementAnalysisVM] Committed improved requirement: changed from {OldLen} to {NewLen} chars",
                    originalDescription?.Length ?? 0, CurrentRequirement.Description?.Length ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisVM] Failed to commit improved requirement");
            }
        }

        private bool CanCommitImprovement()
        {
            return !IsEditingRequirement && HasImprovedRequirement && !string.IsNullOrWhiteSpace(ImprovedRequirement);
        }

        private void CopyToClipboard()
        {
            try
            {
                _logger.LogInformation("[RequirementAnalysisVM] CopyToClipboard called. ButtonText='{ButtonText}', IsEditing={IsEditing}, HasRequirement={HasRequirement}", 
                    CopyAnalysisButtonText, IsEditingRequirement, CurrentRequirement != null);
                
                if (CopyAnalysisButtonText.Contains("Clipboard →"))
                {
                    // Smart clipboard: Paste external response
                    _logger.LogInformation("[RequirementAnalysisVM] Executing paste operation");
                    PasteExternalAnalysisFromClipboard();
                    return;
                }
                
                if (CopyAnalysisButtonText == "Copy to Clipboard" && IsEditingRequirement && !string.IsNullOrWhiteSpace(EditingRequirementText))
                {
                    // Copy edited requirement text when actively editing
                    var textLength = EditingRequirementText?.Length ?? 0;
                    _logger.LogInformation("[RequirementAnalysisVM] Copying edited requirement text: '{Text}'", EditingRequirementText?.Substring(0, Math.Min(100, textLength)));
                    if (!string.IsNullOrWhiteSpace(EditingRequirementText))
                    {
                        System.Windows.Clipboard.SetText(EditingRequirementText.Trim());
                    }
                    _logger.LogInformation("[RequirementAnalysisVM] Copied edited requirement text to clipboard");
                }
                else if (CurrentRequirement != null)
                {
                    // Default: Generate comprehensive analysis prompt for external LLM
                    _logger.LogInformation("[RequirementAnalysisVM] Generating comprehensive analysis prompt for requirement {RequirementId}", CurrentRequirement.Item);
                    
                    var analysisPrompt = GenerateComprehensiveAnalysisPrompt(CurrentRequirement);
                    
                    _logger.LogInformation("[RequirementAnalysisVM] Generated prompt of length {Length} characters", analysisPrompt.Length);
                    
                    System.Windows.Clipboard.SetText(analysisPrompt);
                    
                    // Update state for clipboard monitoring
                    _lastClipboardContent = analysisPrompt;
                    CopyAnalysisButtonText = "⏳ Waiting for external response...";
                    _isWaitingForExternalResponse = true;
                    
                    // Start clipboard monitoring
                    StartClipboardMonitoring();
                    
                    _logger.LogInformation("[RequirementAnalysisVM] Copied comprehensive analysis prompt to clipboard");
                }
                else
                {
                    _logger.LogWarning("[RequirementAnalysisVM] No valid copy operation matched. ButtonText='{ButtonText}', CurrentRequirement={HasReq}, IsEditing={IsEdit}", 
                        CopyAnalysisButtonText, CurrentRequirement != null, IsEditingRequirement);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisVM] Failed to copy to clipboard");
            }
        }

        /// <summary>
        /// Generate comprehensive analysis prompt for external LLM including all requirement details,
        /// supplemental data, refined versions, and complete analysis methodology
        /// </summary>
        private string GenerateComprehensiveAnalysisPrompt(Requirement requirement)
        {
            var sb = new System.Text.StringBuilder();
            
            // Header
            sb.AppendLine("=== EXTERNAL LLM ANALYSIS REQUEST ===");
            sb.AppendLine();
            sb.AppendLine("Please analyze this requirement using the methodology shown below.");
            if (!string.IsNullOrWhiteSpace(ImprovedRequirement))
            {
                sb.AppendLine("Compare your analysis with the AnythingLLM refined version included.");
            }
            sb.AppendLine();
            
            // Original requirement
            sb.AppendLine("ORIGINAL REQUIREMENT:");
            sb.AppendLine("=" + new string('=', 50));
            sb.AppendLine($"ID: {requirement.Item}");
            sb.AppendLine($"Name: {requirement.Name}");
            sb.AppendLine($"Description: {requirement.Description}");
            
            // Add supplemental tables if available
            if (requirement.Tables?.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Supplemental Tables:");
                foreach (var table in requirement.Tables)
                {
                    sb.AppendLine($"### {table.EditableTitle}");
                    if (table.Table?.Count > 0)
                    {
                        foreach (var row in table.Table)
                        {
                            sb.AppendLine("| " + string.Join(" | ", row) + " |");
                        }
                    }
                    sb.AppendLine();
                }
            }
            
            // Add loose content if available
            if (requirement.LooseContent?.Paragraphs?.Count > 0)
            {
                sb.AppendLine("Supplemental Paragraphs:");
                foreach (var para in requirement.LooseContent.Paragraphs)
                {
                    sb.AppendLine($"- {para}");
                }
                sb.AppendLine();
            }
            
            if (requirement.LooseContent?.Tables?.Count > 0)
            {
                sb.AppendLine("Supplemental Loose Tables:");
                foreach (var table in requirement.LooseContent.Tables)
                {
                    sb.AppendLine($"### {table.EditableTitle}");
                    if (table.Rows?.Count > 0)
                    {
                        foreach (var row in table.Rows)
                        {
                            sb.AppendLine("| " + string.Join(" | ", row) + " |");
                        }
                    }
                    sb.AppendLine();
                }
            }
            
            sb.AppendLine();
            
            // AnythingLLM refined version (if available)
            if (!string.IsNullOrWhiteSpace(ImprovedRequirement))
            {
                sb.AppendLine("ANYTHINGLM REFINED VERSION:");
                sb.AppendLine("=" + new string('=', 50));
                sb.AppendLine(ImprovedRequirement);
                sb.AppendLine();
            }
            else if (IsEditingRequirement && !string.IsNullOrWhiteSpace(EditingRequirementText))
            {
                sb.AppendLine("CURRENT EDITED VERSION:");
                sb.AppendLine("=" + new string('=', 50));
                sb.AppendLine(EditingRequirementText);
                sb.AppendLine();
            }
            
            // Complete analysis methodology
            sb.AppendLine("ANALYSIS METHODOLOGY & SYSTEM INSTRUCTIONS:");
            sb.AppendLine("=" + new string('=', 50));
            sb.AppendLine();
            
            try
            {
                sb.AppendLine(TestCaseEditorApp.Services.AnythingLLMService.GetOptimalSystemPrompt());
            }
            catch
            {
                // Fallback analysis instructions if system prompt not available
                sb.AppendLine("Please analyze this requirement for:");
                sb.AppendLine("1. Clarity and specificity");
                sb.AppendLine("2. Testability and verifiability");
                sb.AppendLine("3. Completeness and consistency");
                sb.AppendLine("4. Technical accuracy");
                sb.AppendLine("5. Potential ambiguities or conflicts");
            }
            
            sb.AppendLine();
            
            // Instructions for external LLM
            sb.AppendLine("=" + new string('=', 50));
            sb.AppendLine("INSTRUCTIONS FOR YOUR ANALYSIS:");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(ImprovedRequirement))
            {
                sb.AppendLine("1. Compare the original requirement with the AnythingLLM refined version");
                sb.AppendLine("2. Apply the analysis methodology above to create your own refined version");
                sb.AppendLine("3. Identify any differences in approach or interpretation");
            }
            else
            {
                sb.AppendLine("1. Apply the analysis methodology above to the original requirement");
                sb.AppendLine("2. Create your own refined version");
                sb.AppendLine("3. Identify areas for improvement");
            }
            sb.AppendLine("4. Provide your assessment of the requirement's quality and clarity");
            sb.AppendLine("5. Suggest improvements or highlight potential issues");
            sb.AppendLine();
            sb.AppendLine("Please return your analysis in a structured format showing:");
            sb.AppendLine("- Your refined requirement text");
            if (!string.IsNullOrWhiteSpace(ImprovedRequirement))
            {
                sb.AppendLine("- Comparison with AnythingLLM's version");
            }
            sb.AppendLine("- Quality assessment and recommendations");
            sb.AppendLine();
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return sb.ToString();
        }

        private bool CanCopyToClipboard()
        {
            // Can copy if we have a requirement (for analysis prompt) or if actively editing (for edited text)
            return CurrentRequirement != null;
        }

        /// <summary>
        /// Start clipboard monitoring for external LLM workflow
        /// </summary>
        private void StartClipboardMonitoring()
        {
            try
            {
                _logger.LogInformation("[RequirementAnalysisVM] Starting clipboard monitoring...");
                _clipboardMonitorTimer?.Stop();
                _clipboardMonitorTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1000) // Check every second
                };
                _clipboardMonitorTimer.Tick += OnClipboardMonitorTick;
                _clipboardMonitorTimer.Start();
                
                _logger.LogInformation("[RequirementAnalysisVM] Clipboard monitoring started successfully. IsWaitingForExternalResponse: {IsWaiting}", _isWaitingForExternalResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisVM] Failed to start clipboard monitoring: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Monitor clipboard for external LLM responses
        /// </summary>
        private void OnClipboardMonitorTick(object? sender, EventArgs e)
        {
            try
            {
                _logger.LogDebug("[RequirementAnalysisVM] Clipboard monitor tick - checking...");
                
                // Check clipboard content
                var currentClipboard = System.Windows.Clipboard.GetText();
                _logger.LogDebug("[RequirementAnalysisVM] Current clipboard length: {Length}, Last length: {LastLength}", 
                    currentClipboard?.Length ?? 0, _lastClipboardContent?.Length ?? 0);
                
                if (string.IsNullOrWhiteSpace(currentClipboard) || currentClipboard == _lastClipboardContent)
                {
                    _logger.LogDebug("[RequirementAnalysisVM] No clipboard change detected");
                    return;
                }

                // Update tracking
                _lastClipboardContent = currentClipboard;

                // Check if clipboard content changed while waiting for external response
                if (_isWaitingForExternalResponse)
                {
                    _isWaitingForExternalResponse = false; // Reset waiting state
                    CopyAnalysisButtonText = "Clipboard → LLM Analysis Response";
                    _logger.LogInformation("[RequirementAnalysisVM] Detected clipboard content change - external response ready");
                }
                else
                {
                    _logger.LogDebug("[RequirementAnalysisVM] Clipboard changed but not waiting for external response");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RequirementAnalysisVM] Error monitoring clipboard: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Heuristic to detect if clipboard content is likely an external LLM response
        /// </summary>
        private bool IsLikelyExternalLLMResponse(string content)
        {
            if (string.IsNullOrWhiteSpace(content) || content.Length < 100) return false;

            // Look for common LLM response patterns
            var indicators = new[]
            {
                "quality score", "analysis", "assessment", "recommendation",
                "improved version", "clarity", "issues", "feedback",
                "score:", "rating"
            };

            var lowContent = content.ToLowerInvariant();
            var indicatorCount = indicators.Count(indicator => lowContent.Contains(indicator));
            
            return indicatorCount >= 2;
        }

        /// <summary>
        /// Paste external LLM analysis from clipboard and process
        /// </summary>
        private void PasteExternalAnalysisFromClipboard()
        {
            try
            {
                var clipboardContent = System.Windows.Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(clipboardContent))
                {
                    _logger.LogWarning("[RequirementAnalysisVM] No content in clipboard to paste");
                    return;
                }

                // Extract improved requirement if available
                var improvedText = ExtractImprovedRequirementFromResponse(clipboardContent);
                if (!string.IsNullOrWhiteSpace(improvedText))
                {
                    EditingRequirementText = improvedText;
                    IsEditingRequirement = true;
                    
                    // Store the external analysis for learning feedback when user saves the improved requirement
                    _pendingExternalAnalysis = clipboardContent;
                    
                    _logger.LogInformation("[RequirementAnalysisVM] Extracted improved requirement from clipboard - additional learning will trigger on save");
                }

                // Extract and update issues from external LLM response
                var extractedIssues = ExtractIssuesFromExternalResponse(clipboardContent);
                if (extractedIssues.Count > 0)
                {
                    Issues = extractedIssues;
                    OnPropertyChanged(nameof(HasIssues));
                    _logger.LogInformation("[RequirementAnalysisVM] Updated {Count} issues from external LLM", extractedIssues.Count);
                }
                else
                {
                    _logger.LogInformation("[RequirementAnalysisVM] No valid issues found in external LLM response");
                }

                // Extract quality score if available
                var qualityScore = ExtractQualityScoreFromExternalResponse(clipboardContent);
                if (qualityScore.HasValue)
                {
                    QualityScore = qualityScore.Value;
                    _logger.LogInformation("[RequirementAnalysisVM] Updated quality score from external LLM: {Score}", qualityScore.Value);
                }

                // Mark as having analysis
                HasAnalysis = true;
                AnalysisTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                // ALWAYS trigger learning for external LLM analysis (if improved requirement was found)
                // This feeds the external response to AnythingLLM so it can learn from the external LLM's analysis
                // The FeedLearningToAnythingLLM method will extract the improved requirement and prompt user for consent
                if (!string.IsNullOrWhiteSpace(improvedText))
                {
                    _ = FeedLearningToAnythingLLM(clipboardContent);
                    _logger.LogInformation("[RequirementAnalysisVM] Triggered AnythingLLM learning consent prompt from external analysis");
                }
                else
                {
                    _logger.LogInformation("[RequirementAnalysisVM] No improved requirement found in external response - learning skipped");
                }

                // Reset UI state
                _isWaitingForExternalResponse = false;
                CopyAnalysisButtonText = "LLM Analysis Request → Clipboard";
                
                _logger.LogInformation("[RequirementAnalysisVM] Processed external LLM analysis from clipboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisVM] Failed to process external LLM analysis");
                
                // Reset UI state on error
                _isWaitingForExternalResponse = false;
                CopyAnalysisButtonText = "LLM Analysis Request → Clipboard";
            }
        }

        /// <summary>
        /// Extract improved requirement text from external LLM response using heuristics
        /// </summary>
        private string ExtractImprovedRequirementFromResponse(string response)
        {
            var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Look for "IMPROVED" or "REVISED" sections
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                var upperLine = line.ToUpperInvariant();
                
                if ((upperLine.Contains("IMPROVED") || upperLine.Contains("REVISED") || upperLine.Contains("BETTER")) && 
                    (upperLine.Contains("REQUIREMENT") || upperLine.Contains("VERSION")))
                {
                    // Try to find content after this header
                    if (i + 1 < lines.Length)
                    {
                        var nextLine = lines[i + 1].Trim();
                        if (nextLine.Length > 50) // Reasonable requirement length
                        {
                            return nextLine;
                        }
                    }
                }
            }
            
            return string.Empty;
        }

        /// <summary>
        /// Extract issues from external LLM response
        /// </summary>
        private List<AnalysisIssue> ExtractIssuesFromExternalResponse(string response)
        {
            var issues = new List<AnalysisIssue>();
            
            try
            {
                var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                bool inIssuesSection = false;
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    var upper = trimmed.ToUpperInvariant();
                    
                    // Check if we're entering issues section
                    if (upper.StartsWith("ISSUES FOUND") || upper.StartsWith("ISSUES:") || 
                        upper.StartsWith("ISSUES IDENTIFIED") || upper.Contains("ISSUES FOUND"))
                    {
                        inIssuesSection = true;
                        continue;
                    }
                    
                    // Check if we're leaving issues section
                    if (inIssuesSection && (upper.StartsWith("STRENGTHS") || upper.StartsWith("IMPROVED") || 
                                          upper.StartsWith("RECOMMENDATIONS") || upper.StartsWith("OVERALL") ||
                                          upper.StartsWith("BETTER") || upper.StartsWith("REVISED")))
                    {
                        break;
                    }
                    
                    // Extract issue if we're in the section
                    if (inIssuesSection && (trimmed.StartsWith("*") || trimmed.StartsWith("-") || trimmed.StartsWith("•")))
                    {
                        var issue = ParseIssueFromExternalLine(trimmed);
                        if (issue != null)
                        {
                            issues.Add(issue);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RequirementAnalysisVM] Error extracting issues from external response");
            }
            
            return issues;
        }

        /// <summary>
        /// Parse individual issue from line
        /// </summary>
        private AnalysisIssue? ParseIssueFromExternalLine(string line)
        {
            try
            {
                // Remove leading asterisk and trim
                var content = line.TrimStart('*', '-', '•').Trim();
                
                // Pattern 1: "Category Issue (Priority): Description | Fix: Solution"
                var match1 = System.Text.RegularExpressions.Regex.Match(content, 
                    @"^(.+?)\s+Issue\s*\((.+?)\):\s*(.+?)(?:\s*\|\s*Fix:\s*(.+))?$", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (match1.Success && !string.IsNullOrWhiteSpace(match1.Groups[4].Value))
                {
                    return CreateIssueFromExternal(match1.Groups[1].Value, match1.Groups[2].Value, 
                                     match1.Groups[3].Value, match1.Groups[4].Value);
                }
                
                // Pattern 2: "Category (Priority): Description. Fix: Solution" 
                var match2 = System.Text.RegularExpressions.Regex.Match(content,
                    @"^(.+?)\s*\((.+?)\):\s*(.+?)\.?\s*(?:Fix|Fixed|Resolution|Resolved):\s*(.+)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (match2.Success && !string.IsNullOrWhiteSpace(match2.Groups[4].Value))
                {
                    return CreateIssueFromExternal(match2.Groups[1].Value, match2.Groups[2].Value,
                                     match2.Groups[3].Value, match2.Groups[4].Value);
                }
                
                // Pattern 3: Look for any "Fix:" anywhere in the line
                var fixMatch = System.Text.RegularExpressions.Regex.Match(content,
                    @"(.+?)(?:Fix|Fixed|Resolution|Resolved):\s*(.+?)(?:\s*\((.+?)\))?$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (fixMatch.Success && !string.IsNullOrWhiteSpace(fixMatch.Groups[2].Value))
                {
                    var category = ExtractCategoryFromIssueDescription(fixMatch.Groups[1].Value.Trim());
                    return CreateIssueFromExternal(category, "Medium", fixMatch.Groups[1].Value, fixMatch.Groups[2].Value);
                }

                // Pattern 4: Simple "Category: Description" without explicit fix
                // For external LLMs that don't provide explicit fixes, create issue with description as fix hint
                var simpleMatch = System.Text.RegularExpressions.Regex.Match(content,
                    @"^\*?\*?(.+?)\*?\*?\s*[\(:]\s*(.+)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (simpleMatch.Success)
                {
                    var category = simpleMatch.Groups[1].Value.Trim().TrimEnd(':');
                    var description = simpleMatch.Groups[2].Value.Trim();
                    
                    // Look for severity in parentheses
                    var severityMatch = System.Text.RegularExpressions.Regex.Match(description, @"\((High|Medium|Low)\)", 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var severity = severityMatch.Success ? severityMatch.Groups[1].Value : "Medium";
                    
                    if (severityMatch.Success)
                    {
                        description = description.Replace(severityMatch.Value, "").Trim();
                    }
                    
                    return new AnalysisIssue
                    {
                        Category = category,
                        Severity = ParseSeverityFromString(severity),
                        Description = description,
                        Fix = "See description for suggested improvement"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RequirementAnalysisVM] Error parsing issue from line: {Line}", line);
            }
            
            return null;
        }

        /// <summary>
        /// Helper to create AnalysisIssue with consistent formatting
        /// </summary>
        private AnalysisIssue CreateIssueFromExternal(string category, string priority, string description, string fix)
        {
            return new AnalysisIssue
            {
                Category = category.Trim(),
                Severity = ParseSeverityFromString(priority.Trim()),
                Description = description.Trim(),
                Fix = fix.Trim()
            };
        }

        /// <summary>
        /// Extract likely category from description text when not explicitly provided
        /// </summary>
        private string ExtractCategoryFromIssueDescription(string description)
        {
            var lowerDesc = description.ToLowerInvariant();
            
            if (lowerDesc.Contains("unclear") || lowerDesc.Contains("ambiguous") || lowerDesc.Contains("vague"))
                return "Clarity";
            if (lowerDesc.Contains("test") || lowerDesc.Contains("verify") || lowerDesc.Contains("measurable"))
                return "Testability";
            if (lowerDesc.Contains("missing") || lowerDesc.Contains("incomplete") || lowerDesc.Contains("specify"))
                return "Completeness";
            if (lowerDesc.Contains("multiple") || lowerDesc.Contains("combined") || lowerDesc.Contains("split"))
                return "Atomicity";
            if (lowerDesc.Contains("implement") || lowerDesc.Contains("actionable") || lowerDesc.Contains("abstract"))
                return "Actionability";
            if (lowerDesc.Contains("consistent") || lowerDesc.Contains("terminology") || lowerDesc.Contains("format"))
                return "Consistency";
            
            return "General";
        }

        /// <summary>
        /// Parse severity from priority text
        /// </summary>
        private string ParseSeverityFromString(string priority)
        {
            var upper = priority.ToUpperInvariant();
            return upper switch
            {
                var p when p.Contains("HIGH") || p.Contains("CRITICAL") => "High",
                var p when p.Contains("MEDIUM") || p.Contains("MODERATE") => "Medium",
                var p when p.Contains("LOW") || p.Contains("MINOR") => "Low",
                _ => "Medium"
            };
        }

        /// <summary>
        /// Extract quality score from external LLM response
        /// </summary>
        private int? ExtractQualityScoreFromExternalResponse(string response)
        {
            try
            {
                var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var upper = line.ToUpperInvariant();
                    if (upper.Contains("QUALITY") && upper.Contains("SCORE"))
                    {
                        // Try to extract number
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s*(?:/\s*100|%)?");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int score))
                        {
                            return Math.Clamp(score, 0, 100);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RequirementAnalysisVM] Error extracting quality score");
            }
            
            return null;
        }

        /// <summary>
        /// Check if user edit should trigger learning feedback
        /// </summary>
        private async Task CheckForLearningFeedbackAsync(string originalText, string editedText, string context)
        {
            try
            {
                _logger.LogDebug("[RequirementAnalysisVM] CheckForLearningFeedbackAsync called - Original: '{Original}', Edited: '{Edited}', Context: '{Context}'", 
                    originalText, editedText, context);
                
                // Skip if either text is empty
                if (string.IsNullOrWhiteSpace(originalText) || string.IsNullOrWhiteSpace(editedText))
                {
                    _logger.LogDebug("[RequirementAnalysisVM] Original or edited text is empty - learning feedback skipped");
                    return;
                }

                // Get EditDetectionService - should be injected via DI
                if (_editDetectionService == null)
                {
                    _logger.LogWarning("[RequirementAnalysisVM] EditDetectionService not injected - check DI registration chain");
                    return;
                }

                _logger.LogDebug("[RequirementAnalysisVM] Calling EditDetectionService.ProcessTextEditAsync");
                // Trigger learning feedback detection
                await _editDetectionService.ProcessTextEditAsync(originalText, editedText, context);
                _logger.LogDebug("[RequirementAnalysisVM] EditDetectionService.ProcessTextEditAsync completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisVM] Error checking for learning feedback");
            }
        }

        /// <summary>
        /// Feed learning data to AnythingLLM system for external LLM integration
        /// </summary>
        private async Task FeedLearningToAnythingLLM(string learningData)
        {
            try
            {
                if (_learningService == null)
                {
                    _logger.LogInformation("[RequirementAnalysisVM] LLM learning service not available - skipping learning feedback");
                    return;
                }

                var requirement = CurrentRequirement;
                if (requirement == null)
                {
                    _logger.LogWarning("[RequirementAnalysisVM] No current requirement for learning feedback");
                    return;
                }

                // Check if learning service is available
                if (!await _learningService.IsLearningFeedbackAvailableAsync())
                {
                    _logger.LogWarning("[RequirementAnalysisVM] Learning feedback not available");
                    return;
                }

                // Extract texts for comparison
                var originalText = requirement.Description;
                var externalLLMText = ExtractImprovedRequirementFromResponse(learningData);
                
                if (string.IsNullOrWhiteSpace(externalLLMText))
                {
                    _logger.LogWarning("[RequirementAnalysisVM] Could not extract refined requirement from external LLM response");
                    return;
                }

                // Use the same consent workflow as manual edits
                var (userConsent, feedback) = await _learningService.PromptUserForLearningConsentAsync(
                    originalText, externalLLMText, 100.0); // External LLM changes considered 100% change

                if (userConsent && feedback != null)
                {
                    // Populate additional context for external LLM integration
                    feedback.RequirementId = requirement.Item;
                    feedback.OriginalRequirement = originalText;
                    feedback.FeedbackCategory = "External LLM Integration";
                    feedback.Context = "Learning from external LLM analysis and user acceptance";
                    feedback.UserComments = "User accepted external LLM analysis via clipboard paste";

                    var success = await _learningService.SendLearningFeedbackAsync(feedback);
                    if (success)
                    {
                        _logger.LogInformation("[RequirementAnalysisVM] Learning feedback sent successfully for requirement {RequirementId}", requirement.Item);
                    }
                    else
                    {
                        _logger.LogWarning("[RequirementAnalysisVM] Failed to send learning feedback for requirement {RequirementId}", requirement.Item);
                    }
                }
                else
                {
                    _logger.LogInformation("[RequirementAnalysisVM] User declined to send learning feedback for external LLM integration");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisVM] Failed to send learning data to AnythingLLM");
            }
        }

        /// <summary>
        /// Update the copy button text based on current state
        /// </summary>
        private void UpdateCopyButtonText()
        {
            if (CopyAnalysisButtonText.Contains("Clipboard →") || CopyAnalysisButtonText.Contains("⏳"))
            {
                // Don't update if we're in a special clipboard state
                return;
            }
            
            // Only show "Copy to Clipboard" when actively editing (IsEditingRequirement = true)
            if (IsEditingRequirement && !string.IsNullOrWhiteSpace(EditingRequirementText))
            {
                CopyAnalysisButtonText = "Copy to Clipboard";
            }
            else
            {
                // Default state - always generate comprehensive analysis prompt
                CopyAnalysisButtonText = "LLM Analysis Request → Clipboard";
            }
        }

        /// <summary>
        /// Stop clipboard monitoring and clean up resources
        /// </summary>
        private void StopClipboardMonitoring()
        {
            try
            {
                _clipboardMonitorTimer?.Stop();
                _clipboardMonitorTimer = null;
                _logger.LogDebug("[RequirementAnalysisVM] Stopped clipboard monitoring");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RequirementAnalysisVM] Error stopping clipboard monitoring: {Error}", ex.Message);
            }
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            
            // Update command states when relevant properties change
            if (e.PropertyName == nameof(IsAnalyzing))
            {
                ((RelayCommand)CancelAnalysisCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)AnalyzeRequirementCommand).NotifyCanExecuteChanged();
                ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(HasNoAnalysis)); // Computed property depends on IsAnalyzing
            }
            else if (e.PropertyName == nameof(HasAnalysis))
            {
                OnPropertyChanged(nameof(HasNoAnalysis)); // Computed property depends on HasAnalysis
            }
            else if (e.PropertyName == nameof(HasImprovedRequirement))
            {
                ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
            }
            else if (e.PropertyName == nameof(IsEditingRequirement))
            {
                ((RelayCommand)CancelEditRequirementCommand).NotifyCanExecuteChanged();
                ((RelayCommand)SaveRequirementCommand).NotifyCanExecuteChanged();
            }
            else if (e.PropertyName == nameof(FreeformFeedback))
            {
                OnPropertyChanged(nameof(HasFreeformFeedback)); // Computed property depends on FreeformFeedback
            }
        }

        private void StartAnalysisTimer()
        {
            AnalysisElapsedTime = "";
            _analysisStartTime = DateTime.Now;
            
            _analysisTimer?.Stop();
            _analysisTimer = new System.Timers.Timer(1000); // Update every second
            _analysisTimer.Elapsed += (_, _) =>
            {
                var elapsed = DateTime.Now - _analysisStartTime;
                AnalysisElapsedTime = $"{elapsed.TotalSeconds:F0}s";
            };
            _analysisTimer.Start();
        }

        private void StopAnalysisTimer()
        {
            _analysisTimer?.Stop();
            _analysisTimer?.Dispose();
            _analysisTimer = null;

            if (_analysisStartTime != default)
            {
                var totalElapsed = DateTime.Now - _analysisStartTime;
                AnalysisElapsedTime = $"Completed in {totalElapsed.TotalSeconds:F0}s";
            }
        }

        /// <summary>
        /// Handles requirement navigation events from the Requirements mediator
        /// </summary>
        private void OnRequirementSelected(TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.RequirementSelected e)
        {
            _logger.LogInformation("[RequirementAnalysisVM] OnRequirementSelected called with requirement: {RequirementId}", e.Requirement?.GlobalId ?? "null");
            
            // Update current requirement and refresh analysis display
            CurrentRequirement = e.Requirement;
            
            _logger.LogInformation("[RequirementAnalysisVM] CurrentRequirement updated, HasAnalysis: {HasAnalysis}", HasAnalysis);
        }

        /// <summary>
        /// Handle RequirementAnalyzed event from mediator - triggered when "Run Analysis" completes
        /// </summary>
        private void OnRequirementAnalyzed(TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.RequirementAnalyzed e)
        {
            _logger.LogInformation("[RequirementAnalysisVM] OnRequirementAnalyzed called for requirement: {RequirementId}, Success: {Success}", 
                e.Requirement?.GlobalId ?? "null", e.Success);
            
            // If this is the currently selected requirement, refresh the display
            if (e.Requirement == CurrentRequirement && e.Success && e.Analysis != null)
            {
                _logger.LogInformation("[RequirementAnalysisVM] Refreshing display after analysis completion");
                RefreshAnalysisDisplay();
            }
        }

        private void OnRequirementUpdatedEvent(TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.RequirementUpdated e)
        {
            _logger.LogInformation("[RequirementAnalysisVM] OnRequirementUpdatedEvent called: Event requirement GlobalId={EventReqId}, Current requirement GlobalId={CurrentReqId}", 
                e.Requirement?.GlobalId ?? "null", CurrentRequirement?.GlobalId ?? "null");
            
            // If this is the currently selected requirement, refresh to show the updates (e.g., committed improvements)
            // Compare by GlobalId since the requirement object reference might differ
            if (e.Requirement != null && CurrentRequirement != null && e.Requirement.GlobalId == CurrentRequirement.GlobalId)
            {
                _logger.LogInformation("[RequirementAnalysisVM] Match! Refreshing display after requirement update for {RequirementId}", e.Requirement.GlobalId);
                
                // Notify that CurrentRequirement properties have changed (for Description bindings)
                OnPropertyChanged(nameof(CurrentRequirement));
                
                // Refresh analysis display if present
                RefreshAnalysisDisplay();
            }
            else
            {
                _logger.LogInformation("[RequirementAnalysisVM] No match - skipping refresh. Event={EventReq}, Current={CurrentReq}", 
                    e.Requirement?.GlobalId ?? "null", CurrentRequirement?.GlobalId ?? "null");
            }
        }

        public void Dispose()
        {
            _analysisCancellation?.Cancel();
            _analysisCancellation?.Dispose();
            _analysisTimer?.Stop();
            _analysisTimer?.Dispose();
            StopClipboardMonitoring();
        }
    }
}