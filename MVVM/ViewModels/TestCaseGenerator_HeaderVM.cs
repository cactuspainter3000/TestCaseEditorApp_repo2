using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Consolidated Header ViewModel for the Test Case Creator area.
    /// Merged from 5 partial files into one cohesive class.
    /// Exposes state, commands, and LLM connection tracking.
    /// </summary>
    public partial class TestCaseGenerator_HeaderVM : ObservableObject
    {
        // ==================== State Properties ====================
        
        [ObservableProperty] private string titleText = "Create Test Case";
        [ObservableProperty] private bool isLlmConnected;
        [ObservableProperty] private bool isLlmBusy;
        [ObservableProperty] private string? workspaceName = "Workspace";
        [ObservableProperty] private string? currentRequirementName;
        [ObservableProperty] private int requirementsWithTestCasesCount;
        [ObservableProperty] private string? statusHint;
        [ObservableProperty] private string? currentRequirementSummary;
        
        // Requirement fields for the current requirement
        [ObservableProperty] private string requirementDescription = string.Empty;
        [ObservableProperty] private string requirementMethod = string.Empty;
        [ObservableProperty] private VerificationMethod? requirementMethodEnum = null;
        [ObservableProperty] private string statusMessage = string.Empty;
        
        // Visual guidance flags — UI highlights input areas when true
        [ObservableProperty] private bool requirementDescriptionHighlight = false;
        [ObservableProperty] private bool requirementMethodHighlight = false;

        // ==================== Commands ====================
        
        // Primary header action commands
        public IRelayCommand? OpenRequirementsCommand { get; set; }
        public IRelayCommand? OpenWorkspaceCommand { get; set; }
        public IRelayCommand? SaveCommand { get; set; }

        // File menu commands
        public ICommand? ImportWordCommand { get; set; }
        public ICommand? LoadWorkspaceCommand { get; set; }
        public ICommand? SaveWorkspaceCommand { get; set; }
        public ICommand? ReloadCommand { get; set; }
        public ICommand? ExportAllToJamaCommand { get; set; }
        public ICommand? HelpCommand { get; set; }

        // Optional view-scoped actions
        public IRelayCommand? NewTestCaseCommand { get; set; }
        public IRelayCommand? RemoveTestCaseCommand { get; set; }

        // ==================== Computed Properties ====================
        
        public string OllamaStatusMessage =>
            IsLlmBusy ? "LLM — busy"
            : IsLlmConnected ? "Ollama — connected"
            : "Ollama — disconnected";

        public Brush OllamaStatusColor =>
            IsLlmBusy ? Brushes.Yellow
            : IsLlmConnected ? Brushes.LimeGreen
            : Brushes.Gray;

        // ==================== Constructor ====================
        
        public TestCaseGenerator_HeaderVM() { }

        // ==================== Property Change Handlers ====================
        
        partial void OnIsLlmBusyChanged(bool oldValue, bool newValue)
        {
            OnPropertyChanged(nameof(OllamaStatusMessage));
            OnPropertyChanged(nameof(OllamaStatusColor));
        }

        partial void OnIsLlmConnectedChanged(bool oldValue, bool newValue)
        {
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
            CurrentRequirementName = req?.Name ?? string.Empty;
            CurrentRequirementSummary = req?.Description ?? string.Empty;
            RequirementDescription = req?.Description ?? string.Empty;
            RequirementMethod = req?.Method.ToString() ?? string.Empty;
            RequirementMethodEnum = req?.Method;
        }

        // ==================== LLM Connection Management ====================
        
        /// <summary>
        /// Subscribe to the process-wide LlmConnectionManager and reflect its state.
        /// Call this after constructing the header VM (e.g., from MainViewModel).
        /// </summary>
        public void AttachConnectionManager()
        {
            // Initialize from current global state
            IsLlmConnected = LlmConnectionManager.IsConnected;

            // Subscribe for future changes
            LlmConnectionManager.ConnectionChanged += OnGlobalConnectionChanged;
        }

        private void OnGlobalConnectionChanged(bool connected)
        {
            // Marshal to UI thread if necessary
            var disp = Application.Current?.Dispatcher;
            void apply() => IsLlmConnected = connected;

            if (disp != null && !disp.CheckAccess())
                disp.Invoke(apply);
            else
                apply();
        }

        /// <summary>
        /// Call on dispose / when the header VM is no longer used.
        /// </summary>
        public void DetachConnectionManager()
        {
            try { LlmConnectionManager.ConnectionChanged -= OnGlobalConnectionChanged; } 
            catch { /* best effort */ }
        }
    }
}