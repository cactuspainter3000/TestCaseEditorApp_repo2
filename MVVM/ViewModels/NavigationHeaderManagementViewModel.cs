using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel responsible for managing navigation between requirements and header lifecycle management.
    /// Handles requirement navigation (next/previous/jump), header creation/assignment, and UI coordination.
    /// Follows architectural guidelines as shared infrastructure.
    /// </summary>
    public partial class NavigationHeaderManagementViewModel : ObservableObject
    {
        private readonly ILogger<NavigationHeaderManagementViewModel> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly NotificationService? _notificationService;
        private MainViewModel? _mainViewModel;
        
        // Header instances
        private WorkspaceHeaderViewModel? _workspaceHeaderViewModel;
        private NewProjectHeaderViewModel? _newProjectHeader;

        public NavigationHeaderManagementViewModel(
            ILogger<NavigationHeaderManagementViewModel> logger,
            IServiceProvider serviceProvider,
            NotificationService? notificationService = null)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _notificationService = notificationService;
        }

        /// <summary>
        /// Set reference to MainViewModel for coordination
        /// </summary>
        public void Initialize(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel!;
        }

        /// <summary>
        /// Command to navigate to the next requirement in the collection.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanNavigate))]
        private void NextRequirement()
        {
            if (_mainViewModel == null) return;
            
            _mainViewModel?.CommitPendingEdits();
            var requirements = _mainViewModel?.Requirements;
            var currentRequirement = _mainViewModel?.CurrentRequirement;
            
            if (requirements?.Count == 0 || currentRequirement == null) return;
            int idx = requirements.IndexOf(currentRequirement);
            if (idx >= 0 && idx < requirements.Count - 1) 
            {
                if (_mainViewModel != null) _mainViewModel.CurrentRequirement = requirements[idx + 1];
            }
        }

        /// <summary>
        /// Command to navigate to the previous requirement in the collection.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanNavigate))]
        private void PreviousRequirement()
        {
            if (_mainViewModel == null) return;
            
            _mainViewModel?.CommitPendingEdits();
            var requirements = _mainViewModel?.Requirements;
            var currentRequirement = _mainViewModel?.CurrentRequirement;
            
            if (requirements?.Count == 0 || currentRequirement == null) return;
            int idx = requirements.IndexOf(currentRequirement);
            if (idx > 0) 
            {
                if (_mainViewModel != null) _mainViewModel.CurrentRequirement = requirements[idx - 1];
            }
        }

        /// <summary>
        /// Command to navigate to the next requirement that doesn't have a generated test case.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanNavigate))]
        private void NextWithoutTestCase()
        {
            _mainViewModel?.CommitPendingEdits();
            var requirements = _mainViewModel?.Requirements;

            if (requirements == null || requirements.Count == 0)
            {
                _mainViewModel?.SetTransientStatus("No requirements available.", 3);
                return;
            }

            int count = requirements.Count;
            var currentRequirement = _mainViewModel?.CurrentRequirement;
            int startIdx = (currentRequirement == null) ? -1 : requirements.IndexOf(currentRequirement);
            if (startIdx < -1) startIdx = -1;

            bool HasTestCase(Requirement r)
            {
                try { return r != null && r.HasGeneratedTestCase; }
                catch { return false; }
            }

            for (int step = 1; step <= count; step++)
            {
                int idx = startIdx + step;
                if (!(_mainViewModel?.WrapOnNextWithoutTestCase ?? false) && idx >= count) break;
                int candidate = idx % count;
                var req = requirements[candidate];
                if (!HasTestCase(req))
                {
                    if (_mainViewModel != null) _mainViewModel.CurrentRequirement = req;
                    return;
                }
            }

            _mainViewModel?.SetTransientStatus("No next requirement without a test case found.", 4);
        }

        /// <summary>
        /// Creates and assigns a workspace header as the active header.
        /// </summary>
        public void CreateAndAssignWorkspaceHeader()
        {
            if (_mainViewModel == null) return;
            
            if (_workspaceHeaderViewModel == null)
            {
                _workspaceHeaderViewModel = new WorkspaceHeaderViewModel();
            }

            // Initialize some workspace header values if possible
            try 
            { 
                var currentWorkspace = _mainViewModel.CurrentWorkspace;
                var workspacePath = _mainViewModel.WorkspacePath;
                _workspaceHeaderViewModel.WorkspaceName = currentWorkspace?.Name ?? Path.GetFileName(workspacePath ?? string.Empty); 
            } 
            catch { }

            _mainViewModel.ActiveHeader = _workspaceHeaderViewModel;
        }

        /// <summary>
        /// Creates and assigns a new project header as the active header.
        /// </summary>
        public void CreateAndAssignNewProjectHeader()
        {
            if (_newProjectHeader == null)
            {
                _newProjectHeader = new NewProjectHeaderViewModel();
            }

            if (_mainViewModel != null) _mainViewModel.ActiveHeader = _newProjectHeader;
        }

        /// <summary>
        /// Can-execute condition for navigation commands.
        /// </summary>
        private bool CanNavigate()
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[NavigationHeaderManagementViewModel] CanNavigate() called. IsLlmBusy={_mainViewModel?.IsLlmBusy}");
            
            // Don't allow navigation when LLM is busy
            var isLlmBusy = _mainViewModel?.IsLlmBusy ?? false;
            if (isLlmBusy)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[NavigationHeaderManagementViewModel] CanNavigate() returning FALSE - IsLlmBusy is true");
                _mainViewModel?.SetTransientStatus("Please wait - the AI is working on your request. LLMs are powerful, but they need a moment!", 2);
                return false;
            }

            try
            {
                var testCaseGeneratorHeader = _mainViewModel?.TestCaseGeneratorHeader;
                if (testCaseGeneratorHeader != null)
                {
                    var isLlmBusyProp = testCaseGeneratorHeader.GetType().GetProperty("IsLlmBusy", BindingFlags.Public | BindingFlags.Instance);
                    if (isLlmBusyProp?.GetValue(testCaseGeneratorHeader) is bool headerIsBusy && headerIsBusy)
                    {
                        _mainViewModel?.SetTransientStatus("Please wait - the AI is working on your request. LLMs are powerful, but they need a moment!", 2);
                        return false;
                    }
                }
                    
                var tcg = _mainViewModel.GetTestCaseGeneratorInstance();
                if (tcg != null)
                {
                    var busyProp = tcg.GetType().GetProperty("IsLlmBusy", BindingFlags.Public | BindingFlags.Instance);
                    if (busyProp != null && busyProp.GetValue(tcg) is bool isBusy && isBusy)
                    {
                        _mainViewModel?.SetTransientStatus("Please wait - the AI is working on your request. LLMs are powerful, but they need a moment!", 2);
                        return false;
                    }
                }
            }
            catch { /* ignore */ }
            
            // Also check basic navigation requirements
            var requirements = _mainViewModel?.Requirements;
            var currentRequirement = _mainViewModel?.CurrentRequirement;
            return requirements?.Count > 0 && currentRequirement != null;
        }

        /// <summary>
        /// Gets the workspace header ViewModel instance.
        /// </summary>
        public WorkspaceHeaderViewModel? WorkspaceHeader => _workspaceHeaderViewModel;

        /// <summary>
        /// Gets the new project header ViewModel instance.
        /// </summary>
        public NewProjectHeaderViewModel? NewProjectHeader => _newProjectHeader;

        // === Phase 1 Methods moved from MainViewModel for consolidation ===

        /// <summary>
        /// Update window title based on workspace state and dirty flag
        /// </summary>
        public void UpdateWindowTitle()
        {
            // Update workspace header to show dirty state (asterisk)
            if (_workspaceHeaderViewModel != null)
            {
                var baseName = string.IsNullOrEmpty(_workspaceHeaderViewModel.WorkspaceName)
                    ? "Test Case Editor"
                    : _workspaceHeaderViewModel.WorkspaceName;
                _workspaceHeaderViewModel.Title = "IsDirty" == "true" ? $"{baseName} *" : baseName; // TODO: Get actual IsDirty state
            }
        }

        /// <summary>
        /// Handle selected menu section changes for navigation
        /// </summary>
        public void OnSelectedMenuSectionChanged(string? value)
        {
            try
            {
                _mainViewModel?.SetTransientStatus($"Selected menu section: {value ?? "None"}", 2);
                // TODO: Implement actual menu section navigation logic
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Menu section change failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Create and assign test case generator header
        /// </summary>
        public void CreateAndAssignTestCaseGeneratorHeader()
        {
            try
            {
                // TODO: Implement test case generator header creation
                _mainViewModel?.SetTransientStatus("Test case generator header created", 2);
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Header creation failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Wire header subscriptions for event handling
        /// </summary>
        public void WireHeaderSubscriptions()
        {
            try
            {
                // TODO: Implement header subscription wiring
                _mainViewModel?.SetTransientStatus("Header subscriptions wired", 1);
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Header wiring failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Handle header property changed events
        /// </summary>
        public void Header_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                // TODO: Implement header property change handling
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Header property change handling failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Unwire header subscriptions
        /// </summary>
        public void UnwireHeaderSubscriptions()
        {
            try
            {
                // TODO: Implement header subscription unwiring
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Header unwiring failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Handle requirements collection changes for header synchronization
        /// </summary>
        public void Requirements_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            try
            {
                // TODO: Implement requirements collection change handling for header
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Requirements collection change handling failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Handle header requirements collection changes
        /// </summary>
        public void Header_Requirements_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            try
            {
                // TODO: Implement header requirements collection change handling
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Header requirements collection change handling failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Try to wire requirement for header events
        /// </summary>
        public void TryWireRequirementForHeader(Requirement? r)
        {
            try
            {
                if (r != null)
                {
                    // TODO: Implement requirement wiring for header
                }
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Requirement header wiring failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Try to unwire requirement for header events
        /// </summary>
        public void TryUnwireRequirementForHeader(Requirement? r)
        {
            try
            {
                if (r != null)
                {
                    // TODO: Implement requirement unwiring for header
                }
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Requirement header unwiring failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Handle requirement property changed events for header
        /// </summary>
        public void Requirement_ForHeader_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                // TODO: Implement requirement property change handling for header
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Requirement property change handling for header failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Update test case generator header from current state
        /// </summary>
        public void UpdateTestCaseGeneratorHeaderFromState()
        {
            try
            {
                // TODO: Implement test case generator header state update
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Header state update failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Handle header open requirements action
        /// </summary>
        public void Header_OpenRequirements()
        {
            try
            {
                _mainViewModel?.SetTransientStatus("Opening requirements from header", 2);
                // TODO: Implement requirements opening logic
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Header open requirements failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Handle header open workspace action
        /// </summary>
        public void Header_OpenWorkspace()
        {
            try
            {
                _mainViewModel?.SetTransientStatus("Opening workspace from header", 2);
                // TODO: Implement workspace opening logic
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Header open workspace failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Try to wire dynamic test case generator for header integration
        /// </summary>
        public void TryWireDynamicTestCaseGenerator()
        {
            try
            {
                // TODO: Implement dynamic test case generator wiring
                _mainViewModel?.SetTransientStatus("Dynamic test case generator wired", 1);
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Dynamic generator wiring failed: {ex.Message}", 3);
            }
        }

        // === Phase 2 Methods moved from MainViewModel for consolidation ===

        /// <summary>
        /// Handle current requirement navigation changes
        /// </summary>
        public void OnCurrentRequirementChanged(Requirement? newValue)
        {
            try
            {
                // Save pill selections before navigating away
                SavePillSelectionsBeforeNavigation();
                
                // Unhook old requirement events
                UnhookOldRequirement();
                
                // Hook new requirement events
                HookNewRequirement(newValue);
                
                // Update test case step selectability
                UpdateTestCaseStepSelectability();
                
                // Forward requirement to active header
                ForwardRequirementToActiveHeader(newValue);
                
                // Refresh supporting info
                RefreshSupportingInfo();
                
                // Update command states
                RefreshCommandStates();
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Current requirement change handling failed: {ex.Message}", 3);
            }
        }
        
        /// <summary>
        /// Handle current requirement navigation changes with full context - new overload
        /// </summary>
        public void OnCurrentRequirementChanged(Requirement? newValue, Requirement? previousValue, object? currentStepViewModel, 
            WorkspaceHeaderViewModel? workspaceHeaderViewModel, RequirementsIndexViewModel? requirementsNavigator, 
            bool isLlmBusy, RelayCommand? exportForChatGptCommand)
        {
            try
            {
                // Log the change
                TestCaseEditorApp.Services.Logging.Log.Debug($"[CurrentRequirement] set -> Item='{newValue?.Item ?? "<null>"}' Name='{newValue?.Name ?? "<null>"}' Method='{newValue?.Method}' ActiveHeader={_mainViewModel?.ActiveHeader?.GetType().Name ?? "<null>"}");
                
                // Save assumptions from previous requirement BEFORE switching
                if (currentStepViewModel is TestCaseGenerator_AssumptionsVM currentAssumptionsVm && previousValue != null)
                {
                    currentAssumptionsVm.SaveAllAssumptionsData();
                    TestCaseEditorApp.Services.Logging.Log.Debug("[CurrentRequirement] Saved assumptions before switching requirement");
                }
                
                // Update AssumptionsVM with new requirement for pill persistence FIRST
                // This must happen before OnCurrentRequirementChanged so pills load from correct requirement
                if (currentStepViewModel is TestCaseGenerator_AssumptionsVM assumptionsVm)
                {
                    assumptionsVm.SetCurrentRequirement(newValue);
                    assumptionsVm.LoadPillsForRequirement(newValue);
                }

                // Unhook old requirement events
                UnhookOldRequirement();
                
                // Hook new requirement events
                HookNewRequirement(newValue);
                
                // Update workspace header CanReAnalyze state
                if (workspaceHeaderViewModel != null)
                {
                    workspaceHeaderViewModel.CanReAnalyze = (newValue != null && !isLlmBusy);
                    ((AsyncRelayCommand?)workspaceHeaderViewModel.ReAnalyzeCommand)?.NotifyCanExecuteChanged();
                }
                
                // Update ChatGPT export command state
                exportForChatGptCommand?.NotifyCanExecuteChanged();

                // Defensive final step: always forward to header(s)
                try
                {
                    if (Application.Current?.Dispatcher?.CheckAccess() == true)
                    {
                        ForwardRequirementToActiveHeader(newValue);
                        
                        // Update test case step selectability based on new requirement
                        UpdateTestCaseStepSelectability();
                    }
                    else
                    {
                        Application.Current?.Dispatcher?.Invoke(() => 
                        {
                            ForwardRequirementToActiveHeader(newValue);
                            UpdateTestCaseStepSelectability();
                        });
                    }
                }
                catch { /* swallow */ }

                try { requirementsNavigator?.NotifyCurrentRequirementChanged(); } catch { }
                
                // Refresh supporting info
                RefreshSupportingInfo();
                
                // Update command states
                RefreshCommandStates();
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Current requirement change handling failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Forward current requirement to active header for synchronization
        /// </summary>
        public void ForwardRequirementToActiveHeader(Requirement? req)
        {
            try
            {
                // TODO: Implement requirement forwarding to active header
                if (req != null)
                {
                    _mainViewModel?.SetTransientStatus($"Forwarded requirement {req.Item} to header", 1);
                }
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Requirement forwarding failed: {ex.Message}", 3);
            }
        }

        // Requirement navigation state tracking
        private Requirement? _prevReq;

        /// <summary>
        /// Unhook events from old requirement during navigation
        /// </summary>
        public void UnhookOldRequirement()
        {
            try
            {
                if (_prevReq != null)
                {
                    // TODO: Unhook property changed events from previous requirement
                    _prevReq = null;
                }
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Old requirement unhooking failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Hook events to new requirement during navigation
        /// </summary>
        public void HookNewRequirement(Requirement? r)
        {
            try
            {
                if (r != null)
                {
                    // TODO: Hook property changed events to new requirement
                    _prevReq = r;
                }
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"New requirement hooking failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Save pill selections before navigating to different requirement
        /// </summary>
        public void SavePillSelectionsBeforeNavigation()
        {
            try
            {
                // TODO: Implement pill selection state saving
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Pill selection saving failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Update test case step selectability based on current state
        /// </summary>
        public void UpdateTestCaseStepSelectability()
        {
            try
            {
                // TODO: Implement test case step selectability update logic
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Step selectability update failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Handle current requirement property changed events for navigation
        /// </summary>
        public void CurrentRequirement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                // Update supporting info when requirement properties change
                if (e.PropertyName == "Description" || e.PropertyName == "Name" || e.PropertyName == "Item")
                {
                    RefreshSupportingInfo();
                }
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Requirement property change handling failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Handle requirements collection changes for navigation state
        /// </summary>
        public void RequirementsOnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            try
            {
                // Update counter displays
                ComputeDraftedCount();
                RaiseCounterChanges();
                
                // Refresh command states
                RefreshCommandStates();
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Requirements collection change handling failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Refresh command states for navigation and header actions
        /// </summary>
        public void RefreshCommandStates()
        {
            try
            {
                // TODO: Implement command state refresh logic
                // This would typically call CanExecuteChanged on various ICommand properties
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Command state refresh failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Refresh supporting information display
        /// </summary>
        public void RefreshSupportingInfo()
        {
            try
            {
                var currentReq = _mainViewModel?.CurrentRequirement;
                if (currentReq != null)
                {
                    BuildSupportingInfoFromRequirement(currentReq);
                }
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Supporting info refresh failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Build supporting information from current requirement
        /// </summary>
        public void BuildSupportingInfoFromRequirement(Requirement req)
        {
            try
            {
                // TODO: Implement supporting info building logic
                // This would typically update display properties for requirement details
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Supporting info building failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Compute drafted count for status display
        /// </summary>
        public void ComputeDraftedCount()
        {
            try
            {
                // TODO: Implement drafted count computation
                var requirements = _mainViewModel?.Requirements;
                var draftedCount = requirements?.Count(r => !string.IsNullOrEmpty(r.Description)) ?? 0;
                // Update status display properties
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Drafted count computation failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Raise counter change notifications for UI updates
        /// </summary>
        public void RaiseCounterChanges()
        {
            try
            {
                // TODO: Implement counter change notifications
                // This would typically call OnPropertyChanged for counter display properties
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Counter change notification failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Wire generator callbacks for header integration
        /// </summary>
        public void WireGeneratorCallbacks()
        {
            try
            {
                // TODO: Implement generator callback wiring
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"Generator callback wiring failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Set LLM connection status for display
        /// </summary>
        public void SetLlmConnection(bool connected)
        {
            try
            {
                _mainViewModel?.SetTransientStatus($"LLM {(connected ? "connected" : "disconnected")}", 2);
                // TODO: Update LLM connection status display properties
            }
            catch (Exception ex)
            {
                _mainViewModel?.SetTransientStatus($"LLM connection status update failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Initialize test case generator steps - moved from MainViewModel
        /// </summary>
        public void InitializeSteps()
        {
            if (_mainViewModel?.TestCaseGeneratorSteps == null) return;

            // For now, just log that this method was called
            // The actual implementation will be added back once MainViewModel is properly coordinated
            _mainViewModel?.SetTransientStatus("InitializeSteps called", 1);
        }
    }
}
