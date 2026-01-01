using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Mediators;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Events;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// Consolidated Header ViewModel for the Test Case Creator area.
    /// Merged from 5 partial files into one cohesive class.
    /// Exposes state, commands, and AnythingLLM connection tracking via mediator pattern.
    /// </summary>
    public partial class TestCaseGenerator_HeaderVM : ObservableObject, IDisposable
    {
        private readonly MainViewModel? _mainVm;
        private bool _isLoadingRequirement = false;

        // ==================== State Properties ====================
        
        [ObservableProperty] private string titleText = "Create Test Case";
        
        // AnythingLLM status tracking - follows same pattern as SideMenuViewModel
        [ObservableProperty] private bool isLlmConnected;
        [ObservableProperty] private bool isLlmBusy;
        [ObservableProperty] private string? workspaceName = "Workspace";
        [ObservableProperty] private string? currentRequirementName;
        [ObservableProperty] private int requirementsWithTestCasesCount;
        [ObservableProperty] private string? statusHint;
        [ObservableProperty] private string? currentRequirementSummary;
        
        // Project status properties
        [ObservableProperty] private string projectName = "No Project";
        [ObservableProperty] private bool isProjectLoaded = false;
        
        // Requirement fields for the current requirement
        [ObservableProperty] private string requirementDescription = string.Empty;
        [ObservableProperty] private string requirementMethod = string.Empty;
        [ObservableProperty] private VerificationMethod? requirementMethodEnum = null;
        [ObservableProperty] private string statusMessage = string.Empty;
        
        // Visual guidance flags — UI highlights input areas when true
        [ObservableProperty] private bool requirementDescriptionHighlight = false;
        [ObservableProperty] private bool requirementMethodHighlight = false;

        // ==================== Shared Verification Method Assumptions ====================
        
        /// <summary>
        /// Shared assumption items (chips) used by both Verification Method Assumptions and Clarifying Questions tabs.
        /// This allows the enabled state to persist across tab navigation.
        /// </summary>
        public ObservableCollection<DefaultItem> SuggestedDefaults { get; } = new();
        
        /// <summary>
        /// Preset definitions for quick assumption selection.
        /// </summary>
        public ObservableCollection<DefaultPreset> DefaultPresets { get; } = new();

        // ==================== Commands ====================
        
        // Primary header action commands
        public IRelayCommand? OpenRequirementsCommand { get; set; }
        public IRelayCommand? OpenWorkspaceCommand { get; set; }
        public IRelayCommand? SaveCommand { get; set; }

        // File menu commands
        public ICommand? ImportWordCommand { get; set; }
        public ICommand? LoadWorkspaceCommand { get; set; }
        public ICommand? SaveWorkspaceCommand { get; set; }
        public ICommand? UndoLastSaveCommand { get; set; }
        public ICommand? ReloadCommand { get; set; }
        public ICommand? ExportAllToJamaCommand { get; set; }
        public ICommand? HelpCommand { get; set; }
        
        // State properties for header bindings
        [ObservableProperty] private bool isDirty;
        [ObservableProperty] private bool canUndoLastSave;
        [ObservableProperty] private string? workspaceFilePath;

        // Expose this ViewModel as DataContext for XAML binding compatibility
        public object DataContext => this;

        // Optional view-scoped actions
        public IRelayCommand? NewTestCaseCommand { get; set; }
        public IRelayCommand? RemoveTestCaseCommand { get; set; }

        // ==================== Computed Properties (LLM Status) ====================
        
        public string AnythingLLMStatusMessage =>
            IsLlmBusy ? "AnythingLLM — busy"
            : IsLlmConnected ? "AnythingLLM — connected"
            : "AnythingLLM — disconnected";

        public Brush AnythingLLMStatusColor =>
            IsLlmBusy ? Brushes.Yellow
            : IsLlmConnected ? Brushes.LimeGreen
            : Brushes.Gray;
            
        // Legacy property for backward compatibility
        public string OllamaStatusMessage => AnythingLLMStatusMessage;
        public Brush OllamaStatusColor => AnythingLLMStatusColor;

        // ==================== Constructor ====================
        
        public TestCaseGenerator_HeaderVM(MainViewModel? mainVm = null) 
        {
            _mainVm = mainVm;
            
            // Subscribe to AnythingLLM status updates (follows same pattern as SideMenuViewModel)
            AnythingLLMMediator.StatusUpdated += OnAnythingLLMStatusUpdated;
            
            // Request current status in case it was already set before we subscribed
            AnythingLLMMediator.RequestCurrentStatus();
        }

        // ==================== Project Status Updates ====================
        
        /// <summary>
        /// Update project status from workspace management events
        /// This method is called by the TestCaseGenerationMediator when it receives workspace events
        /// </summary>
        public void UpdateProjectStatus(string? workspaceName, bool isProjectOpen)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_HeaderVM] UpdateProjectStatus called: workspaceName={workspaceName ?? "NULL"}, isProjectOpen={isProjectOpen}");
                
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (isProjectOpen && !string.IsNullOrWhiteSpace(workspaceName))
                {
                    ProjectName = System.IO.Path.GetFileNameWithoutExtension(workspaceName);
                    IsProjectLoaded = true;
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_HeaderVM] Project status updated: ProjectName={ProjectName}, IsProjectLoaded={IsProjectLoaded}");
                }
                else
                {
                    ProjectName = "No Project";
                    IsProjectLoaded = false;
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_HeaderVM] Project cleared: ProjectName={ProjectName}, IsProjectLoaded={IsProjectLoaded}");
                }
            });
        }

        // ==================== Property Change Handlers ====================
        
        private void OnAnythingLLMStatusUpdated(AnythingLLMStatus status)
        {
            IsLlmConnected = status.IsAvailable;
            IsLlmBusy = status.IsStarting;
            TestCaseEditorApp.Services.Logging.Log.Debug($"[HEADER] AnythingLLM status updated - Connected: {IsLlmConnected}, Busy: {IsLlmBusy}");
        }
        
        partial void OnIsLlmBusyChanged(bool oldValue, bool newValue)
        {
            OnPropertyChanged(nameof(AnythingLLMStatusMessage));
            OnPropertyChanged(nameof(AnythingLLMStatusColor));
            OnPropertyChanged(nameof(OllamaStatusMessage));
            OnPropertyChanged(nameof(OllamaStatusColor));
        }

        partial void OnIsLlmConnectedChanged(bool oldValue, bool newValue)
        {
            OnPropertyChanged(nameof(AnythingLLMStatusMessage));
            OnPropertyChanged(nameof(AnythingLLMStatusColor));
            OnPropertyChanged(nameof(OllamaStatusMessage));
            OnPropertyChanged(nameof(OllamaStatusColor));
        }

        partial void OnRequirementDescriptionChanged(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                RequirementDescriptionHighlight = false;
                if (!string.IsNullOrWhiteSpace(StatusMessage) &&
                    StatusMessage.StartsWith("Cannot ask clarifying questions", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = string.Empty;
                }
            }
            
            // Mark workspace dirty when requirement description changes
            // Skip if we're just loading a requirement (not user editing)
            if (_mainVm != null && !_isLoadingRequirement)
            {
                _mainVm.IsDirty = true;
                TestCaseEditorApp.Services.Logging.Log.Debug("[Header] Requirement description changed - marked workspace dirty");
            }
        }

        partial void OnRequirementMethodEnumChanged(VerificationMethod? value)
        {
            if (value != null)
            {
                RequirementMethodHighlight = false;
                if (!string.IsNullOrWhiteSpace(StatusMessage) &&
                    StatusMessage.StartsWith("Cannot ask clarifying questions", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = string.Empty;
                }
            }
        }

        // ==================== Initialization ====================
        
        /// <summary>
        /// Populate observable properties from a context object provided by MainViewModel.
        /// </summary>
        public void Initialize(TestCaseGenerator_HeaderContext? ctx)
        {
            WorkspaceName = ctx?.WorkspaceName ?? string.Empty;
            UpdateRequirements(ctx?.Requirements);
            StatusHint = "Test Case Creator";

            // Wire commands (MainViewModel supplies the ICommand/IRelayCommand instances)
            ImportWordCommand = ctx?.ImportCommand;
            LoadWorkspaceCommand = ctx?.LoadWorkspaceCommand;
            SaveWorkspaceCommand = ctx?.SaveWorkspaceCommand;
            ReloadCommand = ctx?.ReloadCommand;
            ExportAllToJamaCommand = ctx?.ExportAllToJamaCommand;
            HelpCommand = ctx?.HelpCommand;

            OpenRequirementsCommand = ctx?.OpenRequirementsCommand;
            OpenWorkspaceCommand = ctx?.OpenWorkspaceCommand;
            SaveCommand = ctx?.SaveCommand;
        }

        /// <summary>
        /// Update save status from workspace management mediator (follows WorkspaceHeaderViewModel pattern)
        /// </summary>
        public void UpdateSaveStatus(IWorkspaceManagementMediator? mediator)
        {
            if (mediator != null)
            {
                IsDirty = mediator.HasUnsavedChanges();
                CanUndoLastSave = mediator.CanUndoLastSave();
                // For now, we'll set this to null until we expose the current project path
                WorkspaceFilePath = null;
            }
            else
            {
                IsDirty = false;
                CanUndoLastSave = false;
                WorkspaceFilePath = null;
            }
        }

        // ==================== Update Methods ====================
        
        /// <summary>
        /// Recompute the count of requirements with test cases.
        /// </summary>
        public void UpdateRequirements(IEnumerable<Requirement>? reqs)
        {
            try
            {
                RequirementsWithTestCasesCount = reqs?.Count(r =>
                {
                    try
                    {
                        return (r != null) && 
                               ((r.GeneratedTestCases != null && r.GeneratedTestCases.Count > 0) || 
                                r.HasGeneratedTestCase);
                    }
                    catch { return false; }
                }) ?? 0;
            }
            catch
            {
                RequirementsWithTestCasesCount = 0;
            }
        }

        /// <summary>
        /// Map a Requirement to visible fields. Keeps presentation logic inside the header VM.
        /// </summary>
        public void SetCurrentRequirement(Requirement? req)
        {
            _isLoadingRequirement = true;
            try
            {
                CurrentRequirementName = req?.Name ?? string.Empty;
                CurrentRequirementSummary = req?.Description ?? string.Empty;
                RequirementDescription = req?.Description ?? string.Empty;
                RequirementMethod = req?.Method.ToString() ?? string.Empty;
                RequirementMethodEnum = req?.Method;
            }
            finally
            {
                _isLoadingRequirement = false;
            }
        }
        // ==================== Disposal ====================
        
        public void Dispose()
        {
            // Unsubscribe from AnythingLLM status updates
            AnythingLLMMediator.StatusUpdated -= OnAnythingLLMStatusUpdated;
        }
        
        /// <summary>
        /// Legacy method for backward compatibility
        /// </summary>
        public void DetachConnectionManager()
        {
            Dispose();
        }
    }
}