using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace TestCaseEditorApp.MVVM.ViewModels;

/// <summary>
/// Manages LLM service initialization, status, and coordination
/// </summary>
public partial class LLMServiceManagementViewModel : ObservableObject
{
    private readonly ILogger<LLMServiceManagementViewModel> _logger;
    private MainViewModel? _mainViewModel;

    [ObservableProperty]
    private bool _isLlmConnected;

    [ObservableProperty]
    private string _llmStatus = "Disconnected";

    public LLMServiceManagementViewModel(ILogger<LLMServiceManagementViewModel> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Set reference to MainViewModel for coordination
    /// </summary>
    public void Initialize(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    /// <summary>
    /// Initialize RAG (Retrieval Augmented Generation) for the current workspace
    /// </summary>
    public async Task InitializeRagForWorkspaceAsync()
    {
        _logger.LogInformation("Initializing RAG for workspace");
        
        try
        {
            // TODO: Implement actual RAG initialization logic
            LlmStatus = "Initializing RAG...";
            await Task.Delay(1000); // Simulate initialization time
            
            LlmStatus = "RAG initialized";
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
    /// Initialize AnythingLLM service
    /// </summary>
    public async Task InitializeAnythingLLMAsync()
    {
        _logger.LogInformation("Initializing AnythingLLM");
        
        try
        {
            LlmStatus = "Initializing AnythingLLM...";
            
            // TODO: Implement actual AnythingLLM initialization logic
            await Task.Delay(1000); // Simulate initialization time
            
            IsLlmConnected = true;
            LlmStatus = "AnythingLLM connected";
            _mainViewModel?.SetTransientStatus("AnythingLLM initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize AnythingLLM");
            IsLlmConnected = false;
            LlmStatus = "AnythingLLM connection failed";
            _mainViewModel?.SetTransientStatus($"AnythingLLM initialization failed: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Handle AnythingLLM status updates from mediator
    /// </summary>
    public void OnAnythingLLMStatusFromMediator(object status)
    {
        _logger.LogInformation($"Received AnythingLLM status from mediator: {status}");
        
        // Update status based on mediator message
        LlmStatus = status?.ToString() ?? "Unknown status";
        
        // Update connection state based on status
        IsLlmConnected = !string.IsNullOrEmpty(LlmStatus) && 
                        !LlmStatus.Contains("failed", StringComparison.OrdinalIgnoreCase) &&
                        !LlmStatus.Contains("error", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Handle AnythingLLM status string updates
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
        
        _mainViewModel?.SetTransientStatus(statusMessage);
    }

    /// <summary>
    /// Setup LLM workspace configuration
    /// </summary>
    public void SetupLlmWorkspace()
    {
        _logger.LogInformation("Setting up LLM workspace");
        
        try
        {
            LlmStatus = "Setting up LLM workspace...";
            
            // TODO: Implement actual LLM workspace setup logic
            
            LlmStatus = "LLM workspace ready";
            _mainViewModel?.SetTransientStatus("LLM workspace setup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to setup LLM workspace");
            LlmStatus = "LLM workspace setup failed";
            _mainViewModel?.SetTransientStatus($"LLM workspace setup failed: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Set LLM connection status manually
    /// </summary>
    public void SetLlmConnection(bool connected)
    {
        _logger.LogInformation($"Setting LLM connection status: {connected}");
        
        IsLlmConnected = connected;
        LlmStatus = connected ? "Connected" : "Disconnected";
        
        _mainViewModel?.SetTransientStatus($"LLM {(connected ? "connected" : "disconnected")}");
    }
}