using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for the Verification Method Assumptions tab.
    /// Purpose: Help the LLM focus on important clarifying questions by declaring common test conditions upfront.
    /// These assumptions reduce the number of trivial questions the LLM needs to ask.
    /// </summary>
    public partial class TestCaseGenerator_AssumptionsVM : ObservableObject, IDisposable
    {
        private readonly TestCaseGenerator_HeaderVM? _headerVm;
        private readonly MainViewModel? _mainVm;
        private bool _isApplyingDefaults = false;
        private bool _isLoadingInstructions = false;
        private Requirement? _currentRequirement;

        [ObservableProperty] private string? statusHint;
        [ObservableProperty] private Preset? selectedPreset;
        [ObservableProperty] private string customInstructions = string.Empty;

        private Dictionary<string, string> _allUserInstructions = new();

        /// <summary>
        /// Master collection of all assumption pills loaded from defaults catalog.
        /// This is the single source of truth for pill state.
        /// </summary>
        public ObservableCollection<AssumptionPill> AllPills { get; } = new();

        /// <summary>
        /// Pills visible for the current verification method.
        /// Filtered based on each pill's ApplicableMethods list.
        /// </summary>
        public IEnumerable<AssumptionPill> VisiblePills
        {
            get
            {
                var method = _headerVm?.RequirementMethodEnum;
                if (method == null) return Enumerable.Empty<AssumptionPill>();
                
                return AllPills.Where(p => p.IsVisibleForMethod(method.Value));
            }
        }

        /// <summary>
        /// Access HeaderVM's shared SuggestedDefaults collection FOR BACKWARD COMPATIBILITY ONLY.
        /// New code should use AllPills instead.
        /// </summary>
        [Obsolete("Use AllPills instead")]
        public ObservableCollection<DefaultItem> SuggestedDefaults => _headerVm?.SuggestedDefaults ?? new();
        
        /// <summary>
        /// Access HeaderVM's shared DefaultPresets collection.
        /// </summary>
        public ObservableCollection<DefaultPreset> DefaultPresets => _headerVm?.DefaultPresets ?? new();

        /// <summary>
        /// Filtered defaults for backward compatibility.
        /// </summary>
        [Obsolete("Use VisiblePills instead")]
        public IEnumerable<DefaultItem> FilteredDefaults => SuggestedDefaults;

        /// <summary>
        /// Display the current verification method from the header.
        /// </summary>
        public string RequirementMethod => _headerVm?.RequirementMethod ?? "Unassigned";

        /// <summary>
        /// Update the current requirement reference for pill persistence.
        /// </summary>
        public void SetCurrentRequirement(Requirement? requirement)
        {
            _currentRequirement = requirement;
        }

        public TestCaseGenerator_AssumptionsVM(TestCaseGenerator_HeaderVM? headerVm = null, MainViewModel? mainVm = null)
        {
            _headerVm = headerVm;
            _mainVm = mainVm;

            ResetAssumptionsCommand = new RelayCommand(ResetAssumptions);
            ClearPresetFilterCommand = new RelayCommand(ClearPresetFilter);
            SavePresetCommand = new RelayCommand(SavePreset);
            
            // Load pills from defaults catalog into AllPills collection
            LoadPillsFromCatalog();

            // Subscribe to pill changes to mark dirty
            AllPills.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (AssumptionPill pill in e.NewItems)
                    {
                        pill.PropertyChanged += OnPillChanged;
                    }
                }
            };

            // Subscribe to existing pills
            foreach (var pill in AllPills)
            {
                pill.PropertyChanged += OnPillChanged;
            }

            // Subscribe to verification method changes to refresh visible pills
            if (_headerVm != null)
            {
                _headerVm.PropertyChanged += OnHeaderVerificationMethodChanged;
            }

            // Load user instructions
            LoadUserInstructions();
            
            // NOTE: Do NOT auto-load pills here. 
            // Pills will be loaded explicitly via LoadPillsForRequirement() during navigation.
        }

        /// <summary>
        /// Mark workspace dirty when user toggles a pill.
        /// Actual save happens during navigation.
        /// </summary>
        private void OnPillChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AssumptionPill.IsEnabled) && !_isApplyingDefaults)
            {
                if (_mainVm != null)
                {
                    _mainVm.IsDirty = true;
                    TestCaseEditorApp.Services.Logging.Log.Debug("[Assumptions] Pill toggled - marked workspace dirty");
                }
            }
        }

        /// <summary>
        /// DEPRECATED: For backward compatibility only.
        /// </summary>
        [Obsolete]
        private void OnDefaultItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DefaultItem.IsEnabled) && !_isApplyingDefaults)
            {
                if (_mainVm != null)
                {
                    _mainVm.IsDirty = true;
                    TestCaseEditorApp.Services.Logging.Log.Debug("[Assumptions] Pill toggled - marked workspace dirty");
                }
            }
        }

        /// <summary>
        /// Save user's pill selections to the current requirement.
        /// Called during navigation when workspace is dirty.
        /// </summary>
        public void SaveUserAssumptionSelections()
        {
            if (_currentRequirement == null) return;

            // Get currently enabled pill keys from AllPills collection
            var enabledKeys = AllPills
                .Where(p => p.IsEnabled)
                .Select(p => p.Key)
                .ToHashSet();

            // Save to requirement object
            _currentRequirement.SelectedAssumptionKeys = enabledKeys;
            
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Assumptions] Saved {enabledKeys.Count} pill selections to requirement {_currentRequirement.Item}");
        }

        /// <summary>
        /// Save all assumptions data (pill selections + custom instructions) before navigation.
        /// </summary>
        public void SaveAllAssumptionsData()
        {
            SaveUserAssumptionSelections();
            SaveCustomInstructions();
            TestCaseEditorApp.Services.Logging.Log.Debug("[Assumptions] Saved all assumptions data");
        }

        /// <summary>
        /// When verification method changes, auto-enable relevant chips based on the method.
        /// </summary>
        private void OnHeaderVerificationMethodChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TestCaseGenerator_HeaderVM.RequirementMethodEnum))
            {
                // Refresh visible pills for new method
                OnPropertyChanged(nameof(VisiblePills));
                OnPropertyChanged(nameof(RequirementMethod)); // Update display
                
                // Load custom instructions for the new verification method
                LoadCustomInstructionsForCurrentMethod();
            }
        }

        /// <summary>
        /// Apply default pill suggestions based on verification method.
        /// Only called when requirement has no saved selections.
        /// </summary>
        private void ApplyDefaultSuggestionsForMethod(VerificationMethod? method)
        {
            if (method == null) return;

            // Enable pills that are commonly used for this method
            // This is a simple heuristic - pills marked for this method + common ones (Environment, Equipment)
            foreach (var pill in AllPills)
            {
                // Enable if applicable to this method OR if it's a common category (Environment, Equipment, Documentation)
                bool isCommon = pill.Category?.Equals("Environment", StringComparison.OrdinalIgnoreCase) == true ||
                               pill.Category?.Equals("Equipment", StringComparison.OrdinalIgnoreCase) == true ||
                               pill.Category?.Equals("Documentation", StringComparison.OrdinalIgnoreCase) == true;
                
                bool isApplicable = pill.ApplicableMethods.Count == 0 || pill.ApplicableMethods.Contains(method.Value);
                
                pill.IsEnabled = isCommon && isApplicable;
            }
            
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Assumptions] Applied default suggestions for method {method}");
        }

        /// <summary>
        /// Load user-defined custom instructions from config file.
        /// </summary>
        private void LoadUserInstructions()
        {
            _isLoadingInstructions = true;
            try
            {
                _allUserInstructions = DefaultsHelper.LoadUserInstructions();
                LoadCustomInstructionsForCurrentMethod();
            }
            finally
            {
                _isLoadingInstructions = false;
            }
        }

        /// <summary>
        /// Load custom instructions specific to the current verification method.
        /// </summary>
        private void LoadCustomInstructionsForCurrentMethod()
        {
            _isLoadingInstructions = true;
            try
            {
                var method = _headerVm?.RequirementMethodEnum?.ToString() ?? "";
                if (_allUserInstructions.ContainsKey(method))
                {
                    CustomInstructions = _allUserInstructions[method] ?? "";
                }
                else
                {
                    CustomInstructions = "";
                }
            }
            finally
            {
                _isLoadingInstructions = false;
            }
        }

        /// <summary>
        /// Save custom instructions for the current verification method.
        /// Called automatically when CustomInstructions property changes.
        /// </summary>
        /// <summary>
        /// Save custom instructions to config file.
        /// Called during navigation when workspace is dirty.
        /// </summary>
        public void SaveCustomInstructions()
        {
            var method = _headerVm?.RequirementMethodEnum?.ToString();
            if (string.IsNullOrEmpty(method)) return;

            _allUserInstructions[method] = CustomInstructions ?? "";
            DefaultsHelper.SaveUserInstructions(_allUserInstructions);
        }

        partial void OnCustomInstructionsChanged(string value)
        {
            // Mark workspace dirty when custom instructions change
            // Actual save happens during navigation
            // Skip if we're just loading instructions (not user editing)
            if (_mainVm != null && !_isLoadingInstructions)
            {
                _mainVm.IsDirty = true;
                TestCaseEditorApp.Services.Logging.Log.Debug("[Assumptions] Custom instructions changed - marked workspace dirty");
            }
        }

        /// <summary>
        /// Auto-enable chips based on the verification method.
        /// Maps verification methods to relevant assumption categories.
        /// </summary>
        private void ApplyVerificationMethodDefaults(VerificationMethod? method)
        {
            if (method == null || AllPills.Count == 0) return;

            _isApplyingDefaults = true;

            // Temporarily unsubscribe from PropertyChanged to prevent auto-save during bulk updates
            foreach (var pill in AllPills)
            {
                pill.PropertyChanged -= OnPillChanged;
            }

            try
            {
                // First, disable all chips
                foreach (var pill in AllPills.Where(d => !d.IsLlmSuggested))
                {
                    pill.IsEnabled = false;
                }

                // Check if current requirement has saved selections
                if (_currentRequirement?.SelectedAssumptionKeys != null && _currentRequirement.SelectedAssumptionKeys.Count > 0)
                {
                    // Apply requirement's saved selections
                    var savedKeys = new HashSet<string>(_currentRequirement.SelectedAssumptionKeys, StringComparer.OrdinalIgnoreCase);
                    foreach (var pill in AllPills.Where(d => savedKeys.Contains(d.Key)))
                    {
                        pill.IsEnabled = true;
                    }
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Assumptions] Applied {savedKeys.Count} saved pill selections for requirement {_currentRequirement.Item}");
                    // Don't apply default suggestions if user has saved selections, but continue to finally block
                }
                else
                {
                    // Map verification method to relevant assumption keys
                    var relevantKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            switch (method.Value)
            {
                case VerificationMethod.Test:
                    // Physical testing: equipment, environment, power, loads, samples
                    relevantKeys.Add("tools_cal_12mo");
                    relevantKeys.Add("env_ambient25");
                    relevantKeys.Add("power_regulated5pct");
                    relevantKeys.Add("load_nominal");
                    relevantKeys.Add("sample_size_3");
                    relevantKeys.Add("func_all_modes");
                    relevantKeys.Add("doc_photos");
                    relevantKeys.Add("doc_detailed_log");
                    break;

                case VerificationMethod.TestUnintendedFunction:
                    // Testing unintended functions: similar to Test but with edge cases
                    relevantKeys.Add("tools_cal_12mo");
                    relevantKeys.Add("env_ambient25");
                    relevantKeys.Add("load_min_max");
                    relevantKeys.Add("sample_size_3");
                    relevantKeys.Add("func_all_modes");
                    relevantKeys.Add("doc_photos");
                    relevantKeys.Add("doc_detailed_log");
                    relevantKeys.Add("esd_precautions");
                    break;

                case VerificationMethod.Inspection:
                    // Visual/physical inspection: visual checks, dimensions, materials, documentation
                    relevantKeys.Add("visual_inspect");
                    relevantKeys.Add("dimensional_check");
                    relevantKeys.Add("material_verify");
                    relevantKeys.Add("doc_photos");
                    relevantKeys.Add("env_ambient25");
                    break;

                case VerificationMethod.Analysis:
                    // Mathematical/engineering analysis: models, tools, worst-case
                    relevantKeys.Add("analysis_math_model");
                    relevantKeys.Add("analysis_cad_validated");
                    relevantKeys.Add("analysis_worst_case");
                    relevantKeys.Add("doc_detailed_log");
                    break;

                case VerificationMethod.Simulation:
                    // Computer simulation: validated models, corner cases
                    relevantKeys.Add("sim_validated_model");
                    relevantKeys.Add("sim_corner_cases");
                    relevantKeys.Add("analysis_worst_case");
                    relevantKeys.Add("doc_detailed_log");
                    break;

                case VerificationMethod.Demonstration:
                    // Live demonstration: user scenarios, live operation
                    relevantKeys.Add("demo_user_scenarios");
                    relevantKeys.Add("demo_live_operation");
                    relevantKeys.Add("func_all_modes");
                    relevantKeys.Add("visual_inspect");
                    relevantKeys.Add("doc_photos");
                    relevantKeys.Add("env_ambient25");
                    break;

                case VerificationMethod.ServiceHistory:
                    // Historical data review: field data, similar designs
                    relevantKeys.Add("history_field_data");
                    relevantKeys.Add("history_similar_design");
                    relevantKeys.Add("doc_detailed_log");
                    break;

                case VerificationMethod.VerifiedAtAnotherLevel:
                    // Cross-reference to other verification
                    relevantKeys.Add("doc_detailed_log");
                    break;

                case VerificationMethod.Unassigned:
                    // No preset - let user select
                    break;
            }

            // Enable matching chips
            foreach (var pill in AllPills)
            {
                if (relevantKeys.Contains(pill.Key))
                {
                    pill.IsEnabled = true;
                }
            }

            var methodName = method.Value.ToString();
            StatusHint = relevantKeys.Count > 0 
                ? $"Applied {relevantKeys.Count} default assumptions for {methodName} verification."
                : $"{methodName} verification selected. Choose relevant assumptions below.";
                }
            }
            finally
            {
                // Re-subscribe to PropertyChanged events
                foreach (var pill in AllPills)
                {
                    pill.PropertyChanged -= OnPillChanged; // Remove first to avoid duplicates
                    pill.PropertyChanged += OnPillChanged;
                }
                
                _isApplyingDefaults = false;
            }
        }

        /// <summary>
        /// Load the defaults catalog (chips/assumptions) and presets.
        /// Uses DefaultsHelper to load from Config/defaults.catalog.template.json or hardcoded fallback.
        /// </summary>
        private void LoadDefaultsCatalog()
        {
            try
            {
                var catalog = DefaultsHelper.LoadProjectDefaultsTemplate();

                // Populate AllPills from catalog Items (migrated from legacy SuggestedDefaults)
                AllPills.Clear();
                if (catalog?.Items != null)
                {
                    foreach (var item in catalog.Items)
                    {
                        var pill = DefaultsHelper.ToAssumptionPill(item);
                        AllPills.Add(pill);
                    }
                }

                // Populate DefaultPresets from catalog Presets
                DefaultPresets.Clear();
                if (catalog?.Presets != null)
                {
                    foreach (var preset in catalog.Presets)
                    {
                        DefaultPresets.Add(preset);
                    }
                }

                StatusHint = $"Loaded {AllPills.Count} assumptions and {DefaultPresets.Count} presets.";
            }
            catch (Exception ex)
            {
                StatusHint = $"Error loading defaults catalog: {ex.Message}";
            }
        }

        /// <summary>
        /// Load pills from defaults catalog into AllPills collection.
        /// This is called once at construction time.
        /// </summary>
        private void LoadPillsFromCatalog()
        {
            try
            {
                var catalog = DefaultsHelper.LoadProjectDefaultsTemplate();

                // Convert DefaultItems to AssumptionPills
                AllPills.Clear();
                if (catalog?.Items != null)
                {
                    foreach (var item in catalog.Items)
                    {
                        var pill = DefaultsHelper.ToAssumptionPill(item);
                        AllPills.Add(pill);
                    }
                }

                // Populate DefaultPresets from catalog Presets
                DefaultPresets.Clear();
                if (catalog?.Presets != null)
                {
                    foreach (var preset in catalog.Presets)
                    {
                        DefaultPresets.Add(preset);
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Debug($"[Assumptions] Loaded {AllPills.Count} pills from catalog");
                StatusHint = $"Loaded {AllPills.Count} assumptions and {DefaultPresets.Count} presets.";
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Assumptions] Error loading pills: {ex.Message}");
                StatusHint = $"Error loading defaults catalog: {ex.Message}";
            }
        }

        /// <summary>
        /// Load pill selections for a specific requirement.
        /// This is called explicitly during navigation after SetCurrentRequirement().
        /// </summary>
        public void LoadPillsForRequirement(Requirement? requirement)
        {
            if (requirement == null)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[Assumptions] LoadPillsForRequirement: requirement is null, clearing all pills");
                // Clear all pills
                _isApplyingDefaults = true;
                foreach (var pill in AllPills)
                {
                    pill.IsEnabled = false;
                }
                _isApplyingDefaults = false;
                OnPropertyChanged(nameof(VisiblePills));
                return;
            }

            _isApplyingDefaults = true;
            try
            {
                // First, disable all pills
                foreach (var pill in AllPills)
                {
                    pill.IsEnabled = false;
                }

                // Load requirement's saved selections
                // null = never configured (apply defaults), empty HashSet = user disabled all (respect it)
                if (requirement.SelectedAssumptionKeys != null)
                {
                    var savedKeys = new HashSet<string>(requirement.SelectedAssumptionKeys, StringComparer.OrdinalIgnoreCase);
                    foreach (var pill in AllPills.Where(p => savedKeys.Contains(p.Key)))
                    {
                        pill.IsEnabled = true;
                    }
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Assumptions] Loaded {savedKeys.Count} saved pill selections for requirement {requirement.Item}");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Assumptions] No saved pills for requirement {requirement.Item}, applying defaults");
                    // Apply default suggestions based on verification method
                    ApplyDefaultSuggestionsForMethod(_headerVm?.RequirementMethodEnum);
                }
                
                // Notify UI that visible pills may have changed
                OnPropertyChanged(nameof(VisiblePills));
            }
            finally
            {
                _isApplyingDefaults = false;
            }
        }

        public IRelayCommand ResetAssumptionsCommand { get; }
        public IRelayCommand ClearPresetFilterCommand { get; }
        public IRelayCommand SavePresetCommand { get; }

        private void ResetAssumptions()
        {
            foreach (var pill in AllPills)
            {
                pill.IsEnabled = false;
            }
            StatusHint = "All assumptions cleared.";
        }

        private void ClearPresetFilter()
        {
            SelectedPreset = null;
            StatusHint = "Preset filter cleared.";
        }

        private void SavePreset()
        {
            // TODO: Implement preset saving
            StatusHint = "Preset save not yet implemented.";
        }

        public void Dispose()
        {
            if (_headerVm != null)
            {
                _headerVm.PropertyChanged -= OnHeaderVerificationMethodChanged;
            }

            foreach (var pill in AllPills)
            {
                pill.PropertyChanged -= OnPillChanged;
            }
        }
    }
}
