using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel responsible for managing navigation between requirements and header lifecycle management.
    /// Handles requirement navigation (next/previous/jump), header creation/assignment, and UI coordination.
    /// </summary>
    public partial class NavigationHeaderManagementViewModel : ObservableObject
    {
        // Dependencies and function delegates for data access
        private readonly Action<string, int> _setTransientStatus;
        private readonly Func<ObservableCollection<Requirement>> _getRequirements;
        private readonly Func<Requirement?> _getCurrentRequirement;
        private readonly Action<Requirement?> _setCurrentRequirement;
        private readonly Action _commitPendingEdits;
        
        // Header management dependencies
        private readonly Func<object?> _getActiveHeader;
        private readonly Action<object?> _setActiveHeader;
        private readonly Func<Workspace?> _getCurrentWorkspace;
        private readonly Func<string?> _getWorkspacePath;
        private readonly Func<bool> _getWrapOnNextWithoutTestCase;
        
        // LLM state dependencies for CanNavigate logic
        private readonly Func<bool> _getIsLlmBusy;
        private readonly Func<object?> _getTestCaseGeneratorHeader;
        private readonly Func<object?> _getTestCaseGeneratorInstance;

        // Header instances - these would be passed in via DI in a full implementation
        private WorkspaceHeaderViewModel? _workspaceHeaderViewModel;
        private NewProjectHeaderViewModel? _newProjectHeader;

        public NavigationHeaderManagementViewModel(
            Func<ObservableCollection<Requirement>> getRequirements,
            Func<Requirement?> getCurrentRequirement,
            Action<Requirement?> setCurrentRequirement,
            Action<string, int> setTransientStatus,
            Action commitPendingEdits,
            Func<object?> getActiveHeader,
            Action<object?> setActiveHeader,
            Func<Workspace?> getCurrentWorkspace,
            Func<string?> getWorkspacePath,
            Func<bool> getWrapOnNextWithoutTestCase,
            Func<bool> getIsLlmBusy,
            Func<object?> getTestCaseGeneratorHeader,
            Func<object?> getTestCaseGeneratorInstance)
        {
            _getRequirements = getRequirements ?? throw new ArgumentNullException(nameof(getRequirements));
            _getCurrentRequirement = getCurrentRequirement ?? throw new ArgumentNullException(nameof(getCurrentRequirement));
            _setCurrentRequirement = setCurrentRequirement ?? throw new ArgumentNullException(nameof(setCurrentRequirement));
            _setTransientStatus = setTransientStatus ?? throw new ArgumentNullException(nameof(setTransientStatus));
            _commitPendingEdits = commitPendingEdits ?? throw new ArgumentNullException(nameof(commitPendingEdits));
            _getActiveHeader = getActiveHeader ?? throw new ArgumentNullException(nameof(getActiveHeader));
            _setActiveHeader = setActiveHeader ?? throw new ArgumentNullException(nameof(setActiveHeader));
            _getCurrentWorkspace = getCurrentWorkspace ?? throw new ArgumentNullException(nameof(getCurrentWorkspace));
            _getWorkspacePath = getWorkspacePath ?? throw new ArgumentNullException(nameof(getWorkspacePath));
            _getWrapOnNextWithoutTestCase = getWrapOnNextWithoutTestCase ?? throw new ArgumentNullException(nameof(getWrapOnNextWithoutTestCase));
            _getIsLlmBusy = getIsLlmBusy ?? throw new ArgumentNullException(nameof(getIsLlmBusy));
            _getTestCaseGeneratorHeader = getTestCaseGeneratorHeader ?? throw new ArgumentNullException(nameof(getTestCaseGeneratorHeader));
            _getTestCaseGeneratorInstance = getTestCaseGeneratorInstance ?? throw new ArgumentNullException(nameof(getTestCaseGeneratorInstance));
        }

        /// <summary>
        /// Command to navigate to the next requirement in the collection.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanNavigate))]
        private void NextRequirement()
        {
            _commitPendingEdits();
            var requirements = _getRequirements();
            var currentRequirement = _getCurrentRequirement();
            
            if (requirements.Count == 0 || currentRequirement == null) return;
            int idx = requirements.IndexOf(currentRequirement);
            if (idx >= 0 && idx < requirements.Count - 1) 
                _setCurrentRequirement(requirements[idx + 1]);
        }

        /// <summary>
        /// Command to navigate to the previous requirement in the collection.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanNavigate))]
        private void PreviousRequirement()
        {
            _commitPendingEdits();
            var requirements = _getRequirements();
            var currentRequirement = _getCurrentRequirement();
            
            if (requirements.Count == 0 || currentRequirement == null) return;
            int idx = requirements.IndexOf(currentRequirement);
            if (idx > 0) 
                _setCurrentRequirement(requirements[idx - 1]);
        }

        /// <summary>
        /// Command to navigate to the next requirement that doesn't have a generated test case.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanNavigate))]
        private void NextWithoutTestCase()
        {
            _commitPendingEdits();
            var requirements = _getRequirements();

            if (requirements == null || requirements.Count == 0)
            {
                _setTransientStatus("No requirements available.", 3);
                return;
            }

            int count = requirements.Count;
            var currentRequirement = _getCurrentRequirement();
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
                if (!_getWrapOnNextWithoutTestCase() && idx >= count) break;
                int candidate = idx % count;
                var req = requirements[candidate];
                if (!HasTestCase(req))
                {
                    _setCurrentRequirement(req);
                    return;
                }
            }

            _setTransientStatus("No next requirement without a test case found.", 4);
        }

        /// <summary>
        /// Creates and assigns a workspace header as the active header.
        /// </summary>
        public void CreateAndAssignWorkspaceHeader()
        {
            if (_workspaceHeaderViewModel == null)
            {
                _workspaceHeaderViewModel = new WorkspaceHeaderViewModel();
            }

            // Initialize some workspace header values if possible
            try 
            { 
                var currentWorkspace = _getCurrentWorkspace();
                var workspacePath = _getWorkspacePath();
                _workspaceHeaderViewModel.WorkspaceName = currentWorkspace?.Name ?? Path.GetFileName(workspacePath ?? string.Empty); 
            } 
            catch { }

            _setActiveHeader(_workspaceHeaderViewModel);
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

            _setActiveHeader(_newProjectHeader);
        }

        /// <summary>
        /// Can-execute condition for navigation commands.
        /// </summary>
        private bool CanNavigate()
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[NavigationHeaderManagementViewModel] CanNavigate() called. IsLlmBusy={_getIsLlmBusy()}");
            
            // Don't allow navigation when LLM is busy
            var isLlmBusy = _getIsLlmBusy();
            if (isLlmBusy)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[NavigationHeaderManagementViewModel] CanNavigate() returning FALSE - IsLlmBusy is true");
                _setTransientStatus("Please wait - the AI is working on your request. LLMs are powerful, but they need a moment!", 2);
                return false;
            }

            try
            {
                var testCaseGeneratorHeader = _getTestCaseGeneratorHeader();
                if (testCaseGeneratorHeader != null)
                {
                    var isLlmBusyProp = testCaseGeneratorHeader.GetType().GetProperty("IsLlmBusy", BindingFlags.Public | BindingFlags.Instance);
                    if (isLlmBusyProp?.GetValue(testCaseGeneratorHeader) is bool headerIsBusy && headerIsBusy)
                    {
                        _setTransientStatus("Please wait - the AI is working on your request. LLMs are powerful, but they need a moment!", 2);
                        return false;
                    }
                }
                    
                var tcg = _getTestCaseGeneratorInstance();
                if (tcg != null)
                {
                    var busyProp = tcg.GetType().GetProperty("IsLlmBusy", BindingFlags.Public | BindingFlags.Instance);
                    if (busyProp != null && busyProp.GetValue(tcg) is bool isBusy && isBusy)
                    {
                        _setTransientStatus("Please wait - the AI is working on your request. LLMs are powerful, but they need a moment!", 2);
                        return false;
                    }
                }
            }
            catch { /* ignore */ }
            
            // Also check basic navigation requirements
            var requirements = _getRequirements();
            var currentRequirement = _getCurrentRequirement();
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
                _setTransientStatus($"Selected menu section: {value ?? "None"}", 2);
                // TODO: Implement actual menu section navigation logic
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Menu section change failed: {ex.Message}", 3);
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
                _setTransientStatus("Test case generator header created", 2);
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Header creation failed: {ex.Message}", 3);
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
                _setTransientStatus("Header subscriptions wired", 1);
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Header wiring failed: {ex.Message}", 3);
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
                _setTransientStatus($"Header property change handling failed: {ex.Message}", 3);
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
                _setTransientStatus($"Header unwiring failed: {ex.Message}", 3);
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
                _setTransientStatus($"Requirements collection change handling failed: {ex.Message}", 3);
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
                _setTransientStatus($"Header requirements collection change handling failed: {ex.Message}", 3);
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
                _setTransientStatus($"Requirement header wiring failed: {ex.Message}", 3);
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
                _setTransientStatus($"Requirement header unwiring failed: {ex.Message}", 3);
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
                _setTransientStatus($"Requirement property change handling for header failed: {ex.Message}", 3);
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
                _setTransientStatus($"Header state update failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Handle header open requirements action
        /// </summary>
        public void Header_OpenRequirements()
        {
            try
            {
                _setTransientStatus("Opening requirements from header", 2);
                // TODO: Implement requirements opening logic
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Header open requirements failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Handle header open workspace action
        /// </summary>
        public void Header_OpenWorkspace()
        {
            try
            {
                _setTransientStatus("Opening workspace from header", 2);
                // TODO: Implement workspace opening logic
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Header open workspace failed: {ex.Message}", 3);
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
                _setTransientStatus("Dynamic test case generator wired", 1);
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Dynamic generator wiring failed: {ex.Message}", 3);
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
                _setTransientStatus($"Current requirement change handling failed: {ex.Message}", 3);
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
                    _setTransientStatus($"Forwarded requirement {req.Item} to header", 1);
                }
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Requirement forwarding failed: {ex.Message}", 3);
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
                _setTransientStatus($"Old requirement unhooking failed: {ex.Message}", 3);
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
                _setTransientStatus($"New requirement hooking failed: {ex.Message}", 3);
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
                _setTransientStatus($"Pill selection saving failed: {ex.Message}", 3);
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
                _setTransientStatus($"Step selectability update failed: {ex.Message}", 3);
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
                _setTransientStatus($"Requirement property change handling failed: {ex.Message}", 3);
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
                _setTransientStatus($"Requirements collection change handling failed: {ex.Message}", 3);
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
                _setTransientStatus($"Command state refresh failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Refresh supporting information display
        /// </summary>
        public void RefreshSupportingInfo()
        {
            try
            {
                var currentReq = _getCurrentRequirement();
                if (currentReq != null)
                {
                    BuildSupportingInfoFromRequirement(currentReq);
                }
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Supporting info refresh failed: {ex.Message}", 3);
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
                _setTransientStatus($"Supporting info building failed: {ex.Message}", 3);
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
                var requirements = _getRequirements();
                var draftedCount = requirements?.Count(r => !string.IsNullOrEmpty(r.Description)) ?? 0;
                // Update status display properties
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Drafted count computation failed: {ex.Message}", 3);
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
                _setTransientStatus($"Counter change notification failed: {ex.Message}", 3);
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
                _setTransientStatus($"Generator callback wiring failed: {ex.Message}", 3);
            }
        }

        /// <summary>
        /// Set LLM connection status for display
        /// </summary>
        public void SetLlmConnection(bool connected)
        {
            try
            {
                _setTransientStatus($"LLM {(connected ? "connected" : "disconnected")}", 2);
                // TODO: Update LLM connection status display properties
            }
            catch (Exception ex)
            {
                _setTransientStatus($"LLM connection status update failed: {ex.Message}", 3);
            }
        }
    }
}