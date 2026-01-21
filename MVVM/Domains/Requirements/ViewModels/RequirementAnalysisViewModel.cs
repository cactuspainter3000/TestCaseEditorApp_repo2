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
            CopyAnalysisPromptCommand = new RelayCommand(CopyToClipboard, CanCopyToClipboard);

            // Subscribe to requirement navigation events from Requirements mediator
            var mediator = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator>();
            if (mediator != null)
            {
                mediator.Subscribe<TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.RequirementSelected>(OnRequirementSelected);
                _logger.LogInformation("[RequirementAnalysisVM] Subscribed to RequirementSelected events from Requirements mediator");
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

            _logger.LogInformation("[RequirementAnalysisVM] RefreshAnalysisDisplay called for requirement: {RequirementId}, HasAnalysis: {HasAnalysis}", 
                CurrentRequirement?.Item ?? "null", analysis?.IsAnalyzed ?? false);
            Console.WriteLine($"*** [RequirementAnalysisVM] RefreshAnalysisDisplay: {CurrentRequirement?.Item ?? "null"}, HasAnalysis: {analysis?.IsAnalyzed ?? false} ***");

            if (analysis?.IsAnalyzed == true)
            {
                UpdateUIFromAnalysis(analysis);
                _logger.LogDebug("[RequirementAnalysisVM] Displayed existing analysis for {RequirementId}", CurrentRequirement?.Item);
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
                
                _logger.LogInformation("[RequirementAnalysisVM] Saved edited requirement text");
            }
        }

        private bool CanSaveRequirement()
        {
            return IsEditingRequirement && !string.IsNullOrWhiteSpace(EditingRequirementText);
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
                _clipboardMonitorTimer?.Stop();
                _clipboardMonitorTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1000) // Check every second
                };
                _clipboardMonitorTimer.Tick += OnClipboardMonitorTick;
                _clipboardMonitorTimer.Start();
                
                _logger.LogDebug("[RequirementAnalysisVM] Started clipboard monitoring for external LLM workflow");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RequirementAnalysisVM] Failed to start clipboard monitoring: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Monitor clipboard for external LLM responses
        /// </summary>
        private void OnClipboardMonitorTick(object? sender, EventArgs e)
        {
            try
            {
                // Check clipboard content
                var currentClipboard = System.Windows.Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(currentClipboard) || currentClipboard == _lastClipboardContent)
                    return;

                // Update tracking
                _lastClipboardContent = currentClipboard;

                // Check if this looks like an external LLM response (heuristic)
                if (_isWaitingForExternalResponse && IsLikelyExternalLLMResponse(currentClipboard))
                {
                    CopyAnalysisButtonText = "Clipboard → LLM Analysis Response";
                    _logger.LogInformation("[RequirementAnalysisVM] Detected potential external LLM response in clipboard");
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
                    
                    // Store the external analysis for learning feedback when user saves
                    _pendingExternalAnalysis = clipboardContent;
                    
                    _logger.LogInformation("[RequirementAnalysisVM] Extracted improved requirement from clipboard - learning will trigger on save");
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