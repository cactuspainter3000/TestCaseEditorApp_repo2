using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using System.Windows;
using System.IO;
using System.Linq;

namespace TestCaseEditorApp.MVVM.ViewModels;

/// <summary>
/// Manages LLM service initialization, status, and coordination
/// </summary>
public partial class LLMServiceManagementViewModel : ObservableObject
{
    private readonly ILogger<LLMServiceManagementViewModel> _logger;
    private readonly AnythingLLMService _anythingLLMService;
    private MainViewModel? _mainViewModel;
    
    // Static initialization tracking across all instances
    private static bool _anythingLLMInitializing = false;
    private static readonly object _initializationLock = new();

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
            // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus("RAG initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RAG");
            LlmStatus = "RAG initialization failed";
            // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus($"RAG initialization failed: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Initialize RAG workspace for a specific project workspace - full implementation from MainViewModel
    /// </summary>
    public async Task InitializeRagForWorkspaceAsync(object? currentWorkspace, string workspacePath, WorkspaceHeaderViewModel? workspaceHeaderViewModel, Action<string, int, bool>? setTransientStatus)
    {
        try
        {
            // Generate workspace name based on the current workspace
            var workspaceName = Path.GetFileNameWithoutExtension(workspacePath) ?? "Requirements Workspace";
            
            // Update header with RAG status
            if (workspaceHeaderViewModel != null)
            {
                workspaceHeaderViewModel.IsRagInitializing = true;
                workspaceHeaderViewModel.RagStatusMessage = "Initializing RAG workspace...";
                workspaceHeaderViewModel.RagWorkspaceName = workspaceName;
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Initializing RAG for workspace: {workspaceName}");

            // Check if AnythingLLM service is available
            if (!await _anythingLLMService.IsServiceAvailableAsync())
            {
                if (workspaceHeaderViewModel != null)
                {
                    workspaceHeaderViewModel.RagStatusMessage = "AnythingLLM service not available";
                    workspaceHeaderViewModel.IsRagInitializing = false;
                }
                TestCaseEditorApp.Services.Logging.Log.Warn("[RAG] AnythingLLM service not available for RAG initialization");
                return;
            }

            // Check if workspace already exists
            var existingWorkspaces = await _anythingLLMService.GetWorkspacesAsync();
            var existingWorkspace = existingWorkspaces.FirstOrDefault(w => 
                string.Equals(w.Name, workspaceName, StringComparison.OrdinalIgnoreCase));

            if (existingWorkspace != null)
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Found existing workspace: {existingWorkspace.Name}");
                if (workspaceHeaderViewModel != null)
                {
                    workspaceHeaderViewModel.RagStatusMessage = "RAG workspace ready";
                    workspaceHeaderViewModel.IsRagInitializing = false;
                }
            }
            else
            {
                // Create new workspace
                if (workspaceHeaderViewModel != null)
                {
                    workspaceHeaderViewModel.RagStatusMessage = "Creating RAG workspace...";
                }

                var newWorkspace = await _anythingLLMService.CreateWorkspaceAsync(workspaceName);
                if (newWorkspace != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Created new workspace: {newWorkspace.Name}");
                    if (workspaceHeaderViewModel != null)
                    {
                        workspaceHeaderViewModel.RagStatusMessage = "RAG workspace created successfully";
                    }
                    
                    // Show success message in status
                    // TODO: Replace with proper domain UI coordinator: setTransientStatus?.Invoke($"RAG workspace '{workspaceName}' initialized for enhanced AI analysis", 5, false);
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Failed to create workspace: {workspaceName}");
                    if (workspaceHeaderViewModel != null)
                    {
                        workspaceHeaderViewModel.RagStatusMessage = "Failed to create RAG workspace";
                    }
                }
                
                if (workspaceHeaderViewModel != null)
                {
                    workspaceHeaderViewModel.IsRagInitializing = false;
                }
            }
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Error(ex, "[RAG] Error during RAG workspace initialization");
            if (workspaceHeaderViewModel != null)
            {
                workspaceHeaderViewModel.RagStatusMessage = "RAG initialization failed";
                workspaceHeaderViewModel.IsRagInitializing = false;
            }
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
            if (_anythingLLMInitializing == true)
            {
                _logger.LogInformation("[STARTUP] AnythingLLM initialization already in progress, skipping duplicate call");
                return;
            }
            _anythingLLMInitializing = true;
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
                _anythingLLMInitializing = false;
            }
        }
    }

    /// <summary>
    /// Initialize AnythingLLM service and update status
    /// </summary>
    public async Task InitializeAnythingLLMAsync()
    {
        _logger.LogInformation("Initializing AnythingLLM service");
        
        try
        {
            bool isAvailable = await _anythingLLMService.IsServiceAvailableAsync();
            
            if (isAvailable)
            {
                _logger.LogInformation("AnythingLLM is available and connected");
                IsLlmConnected = true;
                LlmStatus = "Connected";
            }
            else
            {
                _logger.LogInformation("AnythingLLM not available, trying to start...");
                var (success, message) = await _anythingLLMService.EnsureServiceRunningAsync();
                
                if (success)
                {
                    _logger.LogInformation("AnythingLLM started successfully");
                    IsLlmConnected = true;
                    LlmStatus = "Connected";
                }
                else
                {
                    _logger.LogWarning("Failed to start AnythingLLM: {Message}", message);
                    IsLlmConnected = false;
                    LlmStatus = $"Failed: {message}";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing AnythingLLM");
            IsLlmConnected = false;
            LlmStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Handle LLM status updates from mediator
    /// </summary>
    public void OnAnythingLLMStatusFromMediator(AnythingLLMStatus status)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            IsLlmConnected = status.IsAvailable;
            LlmStatus = status.StatusMessage;
            _logger.LogDebug("LLM status updated: {Status}", status.StatusMessage);
        });
    }

    /// <summary>
    /// Handle real-time status updates from AnythingLLM service
    /// </summary>
    public void OnAnythingLLMStatusUpdated(string statusMessage)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            LlmStatus = statusMessage;
            IsLlmConnected = statusMessage.Contains("connected");
            _logger.LogDebug("LLM service status: {Status}", statusMessage);
        });
    }

    /// <summary>
    /// Set LLM connection status
    /// </summary>
    public void SetLlmConnection(bool connected)
    {
        IsLlmConnected = connected;
        LlmStatus = connected ? "Connected" : "Disconnected";
        _logger.LogInformation("LLM connection set to: {Connected}", connected);
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
        
        // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus($"AnythingLLM connection error: {exception.Message}", 5, true);
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
            // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus("LLM service prepared and ready");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare LLM service");
            IsLlmConnected = false;
            LlmStatus = "Failed to prepare LLM service";
            // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus($"Failed to prepare LLM service: {ex.Message}", 5, true);
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
        
        // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus($"LLM service {(IsLlmConnected ? "enabled" : "disabled")}");
    }

    /// <summary>
    /// Sets up integrated LLM workspace for streamlined communication with standardized formats.
    /// </summary>
    public void SetupLlmWorkspace()
    {
        try
        {
            _logger.LogInformation("Setting up LLM workspace configuration");
            // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus("🔧 LLM workspace setup functionality coming soon...", 3);
            
            // TODO: Implement full LLM workspace setup logic here
            // This method was extracted from MainViewModel and needs to be properly implemented
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to setup LLM workspace: {Message}", ex.Message);
            // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus("❌ Failed to generate workspace setup", 3);
        }
    }
}