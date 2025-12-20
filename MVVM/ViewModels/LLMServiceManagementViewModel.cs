using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using System.Windows;

namespace TestCaseEditorApp.MVVM.ViewModels;

/// <summary>
/// Manages LLM service initialization, status, and coordination
/// </summary>
public partial class LLMServiceManagementViewModel : ObservableObject
{
    private readonly ILogger<LLMServiceManagementViewModel> _logger;
    private readonly AnythingLLMService _anythingLLMService;
    private MainViewModel? _mainViewModel;
    private readonly object _initializationLock = new object();

    [ObservableProperty]
    private bool _isLlmConnected;

    [ObservableProperty]
    private string _llmStatus = "Disconnected";

    public LLMServiceManagementViewModel(ILogger<LLMServiceManagementViewModel> logger, AnythingLLMService anythingLLMService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
    }

    /// <summary>
    /// Set reference to MainViewModel for coordination
    /// </summary>
    public void Initialize(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _logger.LogInformation("LLMServiceManagementViewModel initialized with MainViewModel reference");
    }

    /// <summary>
    /// Initialize RAG (Retrieval Augmented Generation) for the current workspace
    /// </summary>
    public async Task InitializeRagForWorkspaceAsync()
    {
        _logger.LogInformation("Initializing RAG for workspace");
        
        try
        {
            LlmStatus = "Initializing RAG...";
            
            // TODO: Implement actual RAG initialization logic
            await Task.Delay(500); // Simulate initialization time
            
            LlmStatus = "RAG ready";
            _mainViewModel?.SetTransientStatus("RAG initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RAG");
            LlmStatus = "RAG initialization failed";
            _mainViewModel?.SetTransientStatus($"RAG initialization failed: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Initializes AnythingLLM connection and updates the LlmConnectionManager with the status.
    /// This integrates with the existing LLM connection system.
    /// </summary>
    public async Task InitializeAnythingLLMServiceAsync()
    {
        // Prevent multiple simultaneous initialization attempts across all instances
        lock (_initializationLock)
        {
            if (MainViewModel._anythingLLMInitializing == true)
            {
                _logger.LogInformation("[STARTUP] AnythingLLM initialization already in progress, skipping duplicate call");
                return;
            }
            MainViewModel._anythingLLMInitializing = true;
        }
        
        try
        {
            _logger.LogInformation("[STARTUP] Checking AnythingLLM availability...");
            
            // Set initial checking status via mediator
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var status = new AnythingLLMStatus
                {
                    IsAvailable = false,
                    IsStarting = true,
                    StatusMessage = "Checking AnythingLLM service..."
                };
                AnythingLLMMediator.NotifyStatusUpdated(status);
            });
            
            // Check if AnythingLLM is available
            bool isAvailable = await _anythingLLMService.IsServiceAvailableAsync();
            
            if (isAvailable)
            {
                _logger.LogInformation("[STARTUP] AnythingLLM is available and connected");
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var status = new AnythingLLMStatus
                    {
                        IsAvailable = true,
                        IsStarting = false,
                        StatusMessage = "AnythingLLM is ready"
                    };
                    AnythingLLMMediator.NotifyStatusUpdated(status);
                });
            }
            else
            {
                _logger.LogInformation("[STARTUP] AnythingLLM not available, trying to start...");
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var status = new AnythingLLMStatus
                    {
                        IsAvailable = false,
                        IsStarting = true,
                        StatusMessage = "Starting AnythingLLM..."
                    };
                    AnythingLLMMediator.NotifyStatusUpdated(status);
                });
                
                // Subscribe to status updates from the service
                _anythingLLMService.StatusUpdated += OnAnythingLLMStatusUpdated;
                
                try
                {
                    // Try to start AnythingLLM
                    var (success, message) = await _anythingLLMService.EnsureServiceRunningAsync();
                    
                    if (success)
                    {
                        _logger.LogInformation("[STARTUP] AnythingLLM started successfully");
                        
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var status = new AnythingLLMStatus
                            {
                                IsAvailable = true,
                                IsStarting = false,
                                StatusMessage = "AnythingLLM is ready"
                            };
                            AnythingLLMMediator.NotifyStatusUpdated(status);
                        });
                    }
                    else
                    {
                        _logger.LogWarning($"[STARTUP] Failed to start AnythingLLM: {message}");
                        
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var status = new AnythingLLMStatus
                            {
                                IsAvailable = false,
                                IsStarting = false,
                                StatusMessage = $"Failed to start AnythingLLM: {message}"
                            };
                            AnythingLLMMediator.NotifyStatusUpdated(status);
                        });
                    }
                }
                finally
                {
                    // Unsubscribe from status updates
                    _anythingLLMService.StatusUpdated -= OnAnythingLLMStatusUpdated;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[STARTUP] Error initializing AnythingLLM");
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var status = new AnythingLLMStatus
                {
                    IsAvailable = false,
                    IsStarting = false,
                    StatusMessage = $"Error: {ex.Message}"
                };
                AnythingLLMMediator.NotifyStatusUpdated(status);
            });
        }
        finally
        {
            lock (_initializationLock)
            {
                MainViewModel._anythingLLMInitializing = false;
            }
        }
    }

    /// <summary>
    /// Handles AnythingLLM status updates from mediator to keep MainViewModel properties in sync
    /// </summary>
    public void OnAnythingLLMStatusFromMediator(AnythingLLMStatus status)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_mainViewModel != null)
            {
                _mainViewModel.IsAnythingLLMAvailable = status.IsAvailable;
                _mainViewModel.IsAnythingLLMStarting = status.IsStarting;
                _mainViewModel.AnythingLLMStatusMessage = status.StatusMessage;
            }
        });
    }

    /// <summary>
    /// Handles real-time status updates from AnythingLLM service during startup
    /// </summary>
    public void OnAnythingLLMStatusUpdated(string statusMessage)
    {
        _logger.LogInformation($"AnythingLLM status updated: {statusMessage}");
        
        LlmStatus = statusMessage;
        
        // Update connection state based on status message
        IsLlmConnected = !string.IsNullOrEmpty(statusMessage) && 
                        !statusMessage.Contains("failed", StringComparison.OrdinalIgnoreCase) &&
                        !statusMessage.Contains("error", StringComparison.OrdinalIgnoreCase) &&
                        !statusMessage.Contains("disconnected", StringComparison.OrdinalIgnoreCase);
        
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var status = new AnythingLLMStatus
            {
                IsAvailable = false, // Still starting if we're getting status updates
                IsStarting = !string.IsNullOrEmpty(statusMessage) && 
                           statusMessage != "AnythingLLM — connected" && 
                           statusMessage != "AnythingLLM — disconnected",
                StatusMessage = statusMessage
            };
            AnythingLLMMediator.NotifyStatusUpdated(status);
        });
        
        _mainViewModel?.SetTransientStatus(statusMessage);
    }

    /// <summary>
    /// Set the current AnythingLLM workspace slug
    /// </summary>
    public void SetAnythingLLMWorkspaceSlug(string? workspaceSlug)
    {
        _logger.LogInformation($"Setting AnythingLLM workspace slug: {workspaceSlug}");
        
        if (_mainViewModel != null)
        {
            _mainViewModel.CurrentAnythingLLMWorkspaceSlug = workspaceSlug;
        }
        
        LlmStatus = string.IsNullOrEmpty(workspaceSlug) ? "No workspace selected" : $"Workspace: {workspaceSlug}";
    }

    /// <summary>
    /// Handle AnythingLLM connection errors
    /// </summary>
    public void HandleAnythingLLMConnectionError(Exception exception)
    {
        _logger.LogError(exception, "AnythingLLM connection error");
        
        IsLlmConnected = false;
        LlmStatus = $"Connection error: {exception.Message}";
        
        _mainViewModel?.SetTransientStatus($"AnythingLLM connection error: {exception.Message}", 5, true);
    }

    /// <summary>
    /// Prepare LLM service for use (ensure it's ready)
    /// </summary>
    public async Task PrepareLlmServiceAsync()
    {
        _logger.LogInformation("Preparing LLM service");
        
        try
        {
            LlmStatus = "Preparing LLM service...";
            
            bool isAvailable = await _anythingLLMService.IsServiceAvailableAsync();
            if (!isAvailable)
            {
                await InitializeAnythingLLMServiceAsync();
            }
            
            IsLlmConnected = true;
            LlmStatus = "LLM service ready";
            _mainViewModel?.SetTransientStatus("LLM service prepared and ready");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare LLM service");
            IsLlmConnected = false;
            LlmStatus = "Failed to prepare LLM service";
            _mainViewModel?.SetTransientStatus($"Failed to prepare LLM service: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Toggle LLM service usage on/off
    /// </summary>
    public void ToggleLlmServiceUsage()
    {
        _logger.LogInformation($"Toggling LLM service usage. Current state: {IsLlmConnected}");
        
        IsLlmConnected = !IsLlmConnected;
        LlmStatus = IsLlmConnected ? "LLM service enabled" : "LLM service disabled";
        
        _mainViewModel?.SetTransientStatus($"LLM service {(IsLlmConnected ? "enabled" : "disabled")}");
    }
}