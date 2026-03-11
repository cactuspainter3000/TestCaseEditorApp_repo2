using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.Notification.Mediators;
using TestCaseEditorApp.MVVM.Domains.Notification.Events;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.Notification.ViewModels
{
    /// <summary>
    /// Domain-agnostic notification workspace ViewModel
    /// Aggregates status information from all domains and displays unified notification area
    /// Replaces legacy TestCaseGeneratorNotificationViewModel
    /// </summary>
    public partial class NotificationWorkspaceViewModel : BaseDomainViewModel, IDisposable
    {
        private new readonly INotificationMediator _mediator;
        private readonly IOllamaStatusMonitor? _ollamaStatusMonitor;

        // === ANYTHINGLLLM STATUS ===
        
        /// <summary>
        /// AnythingLLM connection status indicator
        /// </summary>
        [ObservableProperty]
        private bool isLlmConnected = false;

        /// <summary>
        /// Display text for AnythingLLM status
        /// </summary>
        [ObservableProperty]
        private string anythingLlmStatusText = "AnythingLLM: Disconnected";

        /// <summary>
        /// Color indicator for AnythingLLM status
        /// </summary>
        [ObservableProperty]
        private string anythingLlmStatusColor = "#FF6B6B"; // Red for disconnected

        /// <summary>
        /// LLM provider name (e.g., "OpenAI", "Ollama")
        /// </summary>
        [ObservableProperty]
        private string? llmProvider;

        /// <summary>
        /// LLM model name (e.g., "gpt-4", "phi4-mini")
        /// </summary>
        [ObservableProperty]
        private string? llmModel;

        // === OLLAMA STATUS ===
        
        /// <summary>
        /// Ollama model status (NotLoaded, Loading, Loaded)
        /// </summary>
        [ObservableProperty]
        private string ollamaStatusText = "Ollama: Not Loaded";

        /// <summary>
        /// Color indicator for Ollama model status
        /// </summary>
        [ObservableProperty]
        private string ollamaStatusColor = "#999999"; // Gray for not loaded

        /// <summary>
        /// Name of currently loaded Ollama model
        /// </summary>
        [ObservableProperty]
        private string? ollamaLoadedModel;

        /// <summary>
        /// Whether to keep Ollama model loaded in memory
        /// </summary>
        [ObservableProperty]
        private bool keepOllamaLoaded = false;

        // === REQUIREMENTS PROGRESS ===

        /// <summary>
        /// Total number of requirements in current project
        /// </summary>
        [ObservableProperty]
        private int totalRequirements = 0;

        /// <summary>
        /// Number of requirements that have been analyzed
        /// </summary>
        [ObservableProperty]
        private int analyzedRequirements = 0;

        /// <summary>
        /// Number of requirements that have test cases generated
        /// </summary>
        [ObservableProperty]
        private int requirementsWithTestCases = 0;

        /// <summary>
        /// Display text for requirements progress
        /// </summary>
        [ObservableProperty]
        private string requirementsProgressText = "0 requirements | 0% analyzed | 0% with test cases";

        // === CURRENT REQUIREMENT ===

        /// <summary>
        /// Verification method of the currently selected requirement
        /// </summary>
        [ObservableProperty]
        private string currentRequirementVerificationMethod = "No requirement selected";

        /// <summary>
        /// ID of the currently selected requirement
        /// </summary>
        [ObservableProperty]
        private string? currentRequirementId;

        /// <summary>
        /// Title of the currently selected requirement
        /// </summary>
        [ObservableProperty]
        private string? currentRequirementTitle;

        // === VISIBILITY & STATUS ===

        /// <summary>
        /// Whether the notification area is visible
        /// </summary>
        [ObservableProperty]
        private bool isVisible = true;

        /// <summary>
        /// Custom status message from requesting domain
        /// </summary>
        [ObservableProperty]
        private string? customStatusMessage;

        /// <summary>
        /// Domain that last updated the notification area
        /// </summary>
        [ObservableProperty]
        private string lastUpdatedByDomain = "System";

        public NotificationWorkspaceViewModel(
            INotificationMediator mediator,
            ILogger<NotificationWorkspaceViewModel> logger,
            IOllamaStatusMonitor? ollamaStatusMonitor = null)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _ollamaStatusMonitor = ollamaStatusMonitor;
            _logger?.LogInformation("NotificationWorkspaceViewModel initialized");
            TestCaseEditorApp.Services.Logging.Log.Info($"[NotificationWorkspaceVM] MEDIATOR DEBUG: NotificationWorkspaceViewModel created and subscribing to events");
            
            // Subscribe to all notification events
            SubscribeToEvents();
            
            // Subscribe to Ollama status changes
            if (_ollamaStatusMonitor != null)
            {
                System.Diagnostics.Debug.WriteLine("[NotificationWorkspaceVM] ===== SUBSCRIBING TO OLLAMA MONITORING =====");
                System.Console.WriteLine("[NotificationWorkspaceVM] ===== SUBSCRIBING TO OLLAMA MONITORING =====");
                _ollamaStatusMonitor.StatusChanged += OnOllamaStatusChanged;
                _ollamaStatusMonitor.StartMonitoring();
                
                // Immediately update UI with current status (don't wait for first timer tick or async check)
                var currentStatus = _ollamaStatusMonitor.CurrentStatus;
                var currentModel = _ollamaStatusMonitor.LoadedModelName;
                var currentSize = _ollamaStatusMonitor.LoadedModelSize;
                System.Diagnostics.Debug.WriteLine($"[NotificationWorkspaceVM] Initial Ollama status: {currentStatus}, Model: {currentModel ?? "none"}");
                OnOllamaStatusChanged(this, new OllamaStatusChangedEventArgs 
                { 
                    Status = currentStatus, 
                    ModelName = currentModel, 
                    ModelSize = currentSize 
                });
                
                TestCaseEditorApp.Services.Logging.Log.Info("[NotificationWorkspaceVM] Ollama status monitoring started");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[NotificationWorkspaceVM] ===== WARNING: IOllamaStatusMonitor is NULL =====");
                System.Console.WriteLine("[NotificationWorkspaceVM] ===== WARNING: IOllamaStatusMonitor is NULL =====");
                TestCaseEditorApp.Services.Logging.Log.Warn("[NotificationWorkspaceVM] IOllamaStatusMonitor not available - Ollama status will not be displayed");
            }
            
            // Initialize with default state
            ResetToDefaults();
            
            // Request current LLM status to initialize LED correctly
            TestCaseEditorApp.Services.Logging.Log.Info($"[NotificationWorkspaceVM] MEDIATOR DEBUG: Requesting current LLM status");
            AnythingLLMMediator.RequestCurrentStatus();
            
            // Check current OLLAMA_KEEP_ALIVE setting
            var keepAliveValue = Environment.GetEnvironmentVariable("OLLAMA_KEEP_ALIVE", EnvironmentVariableTarget.User);
            KeepOllamaLoaded = !string.IsNullOrEmpty(keepAliveValue) && keepAliveValue != "5m"; // Default is 5m, anything else means "keep loaded"
        }

        /// <summary>
        /// Subscribe to notification events from the mediator
        /// </summary>
        private void SubscribeToEvents()
        {
            _mediator.Subscribe<NotificationEvents.LlmStatusChanged>(OnLlmStatusChanged);
            _mediator.Subscribe<NotificationEvents.RequirementsProgressChanged>(OnRequirementsProgressChanged);
            _mediator.Subscribe<NotificationEvents.CurrentRequirementChanged>(OnCurrentRequirementChanged);
            _mediator.Subscribe<NotificationEvents.DomainStatusChanged>(OnDomainStatusChanged);
            _mediator.Subscribe<NotificationEvents.NotificationAreaUpdate>(OnNotificationAreaUpdate);
            
            _logger?.LogDebug("Subscribed to all notification events");
        }

        /// <summary>
        /// Handle Ollama status changes from monitor
        /// </summary>
        private void OnOllamaStatusChanged(object? sender, OllamaStatusChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[NotificationWorkspaceVM] ===== OLLAMA STATUS EVENT RECEIVED: {e.Status} =====");
            System.Console.WriteLine($"[NotificationWorkspaceVM] ===== OLLAMA STATUS EVENT RECEIVED: {e.Status} =====");
            
            OllamaLoadedModel = e.ModelName;
            
            switch (e.Status)
            {
                case OllamaModelStatus.Loaded:
                    OllamaStatusText = $"Ollama: {e.ModelName ?? "Model"} Loaded";
                    OllamaStatusColor = "#51CF66"; // Green
                    break;
                    
                case OllamaModelStatus.Loading:
                    OllamaStatusText = "Ollama: Loading...";
                    OllamaStatusColor = "#FFA500"; // Orange
                    break;
                    
                case OllamaModelStatus.NotLoaded:
                    OllamaStatusText = "Ollama: Not Loaded";
                    OllamaStatusColor = "#999999"; // Gray
                    break;
                    
                case OllamaModelStatus.Unknown:
                default:
                    OllamaStatusText = "Ollama: Unknown";
                    OllamaStatusColor = "#FF6B6B"; // Red
                    break;
            }
            
            System.Diagnostics.Debug.WriteLine($"[NotificationWorkspaceVM] Status display set to: {OllamaStatusText}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[NotificationWorkspaceVM] Ollama status display updated: {OllamaStatusText}");
        }

        /// <summary>
        /// Toggle OLLAMA_KEEP_ALIVE environment variable
        /// </summary>
        [RelayCommand]
        private void ToggleKeepOllamaLoaded()
        {
            KeepOllamaLoaded = !KeepOllamaLoaded;
            
            if (KeepOllamaLoaded)
            {
                // Set to 30 minutes (long enough to keep active during work session)
                Environment.SetEnvironmentVariable("OLLAMA_KEEP_ALIVE", "30m", EnvironmentVariableTarget.User);
                TestCaseEditorApp.Services.Logging.Log.Info("[NotificationWorkspaceVM] OLLAMA_KEEP_ALIVE set to 30m - model will stay in memory");
            }
            else
            {
                // Set to 5 minutes (Ollama default)
                Environment.SetEnvironmentVariable("OLLAMA_KEEP_ALIVE", "5m", EnvironmentVariableTarget.User);
                TestCaseEditorApp.Services.Logging.Log.Info("[NotificationWorkspaceVM] OLLAMA_KEEP_ALIVE set to 5m - model will unload after 5 minutes of inactivity");
            }
            
            _logger?.LogInformation("OLLAMA_KEEP_ALIVE toggled: KeepLoaded={KeepLoaded}", KeepOllamaLoaded);
        }

        /// <summary>
        /// Handle LLM status changes
        /// </summary>
        private void OnLlmStatusChanged(NotificationEvents.LlmStatusChanged eventData)
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[NotificationWorkspaceVM] MEDIATOR DEBUG: OnLlmStatusChanged received - Connected={eventData.IsConnected}, Text={eventData.StatusText}");
            IsLlmConnected = eventData.IsConnected;
            AnythingLlmStatusText = eventData.StatusText;
            LlmProvider = eventData.Provider;
            LlmModel = eventData.Model;
            
            // Update status color
            AnythingLlmStatusColor = eventData.IsConnected ? "#51CF66" : "#FF6B6B"; // Green/Red

            _logger?.LogDebug("LLM status updated: Connected={Connected}, Text={StatusText}", 
                eventData.IsConnected, eventData.StatusText);
        }

        /// <summary>
        /// Handle requirements progress changes
        /// </summary>
        private void OnRequirementsProgressChanged(NotificationEvents.RequirementsProgressChanged eventData)
        {
            TotalRequirements = eventData.TotalRequirements;
            AnalyzedRequirements = eventData.AnalyzedRequirements;
            RequirementsWithTestCases = eventData.RequirementsWithTestCases;
            LastUpdatedByDomain = eventData.SourceDomain;
            
            // Calculate percentages
            var analyzedPercentage = TotalRequirements > 0 ? (double)AnalyzedRequirements / TotalRequirements * 100 : 0.0;
            var testCasesPercentage = TotalRequirements > 0 ? (double)RequirementsWithTestCases / TotalRequirements * 100 : 0.0;
            
            // Format progress text
            RequirementsProgressText = $"{TotalRequirements} requirements | {analyzedPercentage:F0}% analyzed | {testCasesPercentage:F0}% with test cases";

            _logger?.LogDebug("Requirements progress updated from {SourceDomain}: {Analyzed}/{Total} analyzed, {WithTestCases}/{Total} with test cases", 
                eventData.SourceDomain, eventData.AnalyzedRequirements, eventData.TotalRequirements, 
                eventData.RequirementsWithTestCases, eventData.TotalRequirements);
        }

        /// <summary>
        /// Handle current requirement changes
        /// </summary>
        private void OnCurrentRequirementChanged(NotificationEvents.CurrentRequirementChanged eventData)
        {
            CurrentRequirementId = eventData.RequirementId;
            CurrentRequirementTitle = eventData.RequirementTitle;
            CurrentRequirementVerificationMethod = string.IsNullOrWhiteSpace(eventData.VerificationMethod) 
                ? "No verification method" 
                : eventData.VerificationMethod;
            LastUpdatedByDomain = eventData.SourceDomain;

            _logger?.LogDebug("Current requirement updated from {SourceDomain}: {RequirementId}, Method={VerificationMethod}", 
                eventData.SourceDomain, eventData.RequirementId, eventData.VerificationMethod);
        }

        /// <summary>
        /// Handle domain status changes
        /// </summary>
        private void OnDomainStatusChanged(NotificationEvents.DomainStatusChanged eventData)
        {
            // This can be used for domain-specific status indicators in the future
            LastUpdatedByDomain = eventData.DomainName;
            
            _logger?.LogDebug("Domain status updated: {DomainName} - {StatusType}: {StatusMessage}", 
                eventData.DomainName, eventData.StatusType, eventData.StatusMessage);
        }

        /// <summary>
        /// Handle notification area visibility and content updates
        /// </summary>
        private void OnNotificationAreaUpdate(NotificationEvents.NotificationAreaUpdate eventData)
        {
            IsVisible = eventData.IsVisible;
            CustomStatusMessage = eventData.CustomMessage;
            
            if (!string.IsNullOrWhiteSpace(eventData.RequestingDomain))
            {
                LastUpdatedByDomain = eventData.RequestingDomain;
            }

            _logger?.LogDebug("Notification area updated by {RequestingDomain}: Visible={IsVisible}, Message={CustomMessage}", 
                eventData.RequestingDomain, eventData.IsVisible, eventData.CustomMessage);
        }

        /// <summary>
        /// Reset all status indicators to defaults
        /// </summary>
        public void ResetToDefaults()
        {
            IsLlmConnected = false;
            AnythingLlmStatusText = "AnythingLLM: Disconnected";
            AnythingLlmStatusColor = "#FF6B6B";
            LlmProvider = null;
            LlmModel = null;
            
            OllamaStatusText = "Ollama: Not Loaded";
            OllamaStatusColor = "#999999";
            OllamaLoadedModel = null;
            
            TotalRequirements = 0;
            AnalyzedRequirements = 0;
            RequirementsWithTestCases = 0;
            RequirementsProgressText = "0 requirements | 0% analyzed | 0% with test cases";
            
            CurrentRequirementId = null;
            CurrentRequirementTitle = null;
            CurrentRequirementVerificationMethod = "No requirement selected";
            
            IsVisible = true;
            CustomStatusMessage = null;
            LastUpdatedByDomain = "System";

            _logger?.LogInformation("Notification workspace reset to defaults");
        }

        // ===== BASE DOMAIN VIEWMODEL IMPLEMENTATIONS =====
        // Notification ViewModels don't need save/cancel/refresh operations
        
        protected override bool CanSave() => false; // No save operations for notifications
        protected override bool CanCancel() => false; // No cancel operations for notifications
        protected override bool CanRefresh() => true; // Can refresh status
        
        protected override async Task SaveAsync()
        {
            // No save operation needed for notification display
            await Task.CompletedTask;
        }
        
        protected override void Cancel()
        {
            // No cancel operation needed for notification display

        }
        
        protected override async Task RefreshAsync()
        {
            // Refresh by resetting to defaults and requesting current status from all domains
            ResetToDefaults();
            _logger?.LogInformation("Notification workspace refreshed");
            await Task.CompletedTask;
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public override void Dispose()
        {
            _logger?.LogDebug("NotificationWorkspaceViewModel disposing");
            
            // Unsubscribe from Ollama status changes
            if (_ollamaStatusMonitor != null)
            {
                _ollamaStatusMonitor.StatusChanged -= OnOllamaStatusChanged;
                _ollamaStatusMonitor.StopMonitoring();
            }
            
            base.Dispose();
        }
    }
}