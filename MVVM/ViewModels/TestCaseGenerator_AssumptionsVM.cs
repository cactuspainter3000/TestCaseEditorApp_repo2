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
    /// ViewModel for the Test Assumptions tab.
    /// Purpose: Help the LLM focus on important clarifying questions by declaring common test conditions upfront.
    /// These assumptions reduce the number of trivial questions the LLM needs to ask.
    /// </summary>
    public partial class TestCaseGenerator_AssumptionsVM : ObservableObject, IDisposable
    {
        private readonly TestCaseGenerator_HeaderVM? _headerVm;

        [ObservableProperty] private string? statusHint;
        [ObservableProperty] private Preset? selectedPreset;
        [ObservableProperty] private string customInstructions = string.Empty;

        private Dictionary<string, string> _allUserInstructions = new();

        /// <summary>
        /// Access HeaderVM's shared SuggestedDefaults collection.
        /// This ensures assumptions persist across tabs.
        /// </summary>
        public ObservableCollection<DefaultItem> SuggestedDefaults => _headerVm?.SuggestedDefaults ?? new();
        
        /// <summary>
        /// Access HeaderVM's shared DefaultPresets collection.
        /// </summary>
        public ObservableCollection<DefaultPreset> DefaultPresets => _headerVm?.DefaultPresets ?? new();

        public IEnumerable<DefaultItem> FilteredDefaults => SuggestedDefaults;

        /// <summary>
        /// Display the current verification method from the header.
        /// </summary>
        public string RequirementMethod => _headerVm?.RequirementMethod ?? "Unassigned";

        public TestCaseGenerator_AssumptionsVM(TestCaseGenerator_HeaderVM? headerVm = null)
        {
            _headerVm = headerVm;

            // Subscribe to verification method changes to auto-enable relevant chips
            if (_headerVm != null)
            {
                _headerVm.PropertyChanged += OnHeaderVerificationMethodChanged;
            }

            ResetAssumptionsCommand = new RelayCommand(ResetAssumptions);
            ClearPresetFilterCommand = new RelayCommand(ClearPresetFilter);
            SavePresetCommand = new RelayCommand(SavePreset);
            
            // Load defaults catalog if HeaderVM collections are empty
            if (SuggestedDefaults.Count == 0)
            {
                LoadDefaultsCatalog();
            }

            // Subscribe to changes in SuggestedDefaults to auto-save selections
            SuggestedDefaults.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (DefaultItem item in e.NewItems)
                    {
                        item.PropertyChanged += OnDefaultItemChanged;
                    }
                }
            };

            // Subscribe to existing items
            foreach (var item in SuggestedDefaults)
            {
                item.PropertyChanged += OnDefaultItemChanged;
            }

            // Load user instructions
            LoadUserInstructions();
            
            // Apply initial verification method defaults
            if (_headerVm?.RequirementMethodEnum != null)
            {
                ApplyVerificationMethodDefaults(_headerVm.RequirementMethodEnum);
            }
        }

        /// <summary>
        /// Auto-save when user toggles a pill.
        /// </summary>
        private void OnDefaultItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DefaultItem.IsEnabled))
            {
                SaveUserAssumptionSelections();
            }
        }

        /// <summary>
        /// Save user's pill selections to config file.
        /// </summary>
        private void SaveUserAssumptionSelections()
        {
            // TODO: Implement saving user's assumption selections per verification method
            System.Diagnostics.Debug.WriteLine("[Assumptions] User selections auto-saved");
        }

        /// <summary>
        /// When verification method changes, auto-enable relevant chips based on the method.
        /// </summary>
        private void OnHeaderVerificationMethodChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TestCaseGenerator_HeaderVM.RequirementMethodEnum))
            {
                ApplyVerificationMethodDefaults(_headerVm?.RequirementMethodEnum);
                OnPropertyChanged(nameof(RequirementMethod)); // Update display
                
                // Load custom instructions for the new verification method
                LoadCustomInstructionsForCurrentMethod();
            }
        }

        /// <summary>
        /// Load user-defined custom instructions from config file.
        /// </summary>
        private void LoadUserInstructions()
        {
            _allUserInstructions = DefaultsHelper.LoadUserInstructions();
            LoadCustomInstructionsForCurrentMethod();
        }

        /// <summary>
        /// Load custom instructions specific to the current verification method.
        /// </summary>
        private void LoadCustomInstructionsForCurrentMethod()
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

        /// <summary>
        /// Save custom instructions for the current verification method.
        /// Called automatically when CustomInstructions property changes.
        /// </summary>
        private void SaveCustomInstructions()
        {
            var method = _headerVm?.RequirementMethodEnum?.ToString();
            if (string.IsNullOrEmpty(method)) return;

            _allUserInstructions[method] = CustomInstructions ?? "";
            DefaultsHelper.SaveUserInstructions(_allUserInstructions);
        }

        partial void OnCustomInstructionsChanged(string value)
        {
            // Auto-save when user types (with slight delay to avoid excessive saves)
            SaveCustomInstructions();
        }

        /// <summary>
        /// Auto-enable chips based on the verification method.
        /// Maps verification methods to relevant assumption categories.
        /// </summary>
        private void ApplyVerificationMethodDefaults(VerificationMethod? method)
        {
            if (method == null || SuggestedDefaults.Count == 0) return;

            // First, disable all non-user-selected chips (preserve user choices)
            foreach (var item in SuggestedDefaults.Where(d => !d.IsLlmSuggested))
            {
                item.IsEnabled = false;
            }

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
            foreach (var item in SuggestedDefaults)
            {
                if (relevantKeys.Contains(item.Key))
                {
                    item.IsEnabled = true;
                }
            }

            var methodName = method.Value.ToString();
            StatusHint = relevantKeys.Count > 0 
                ? $"Applied {relevantKeys.Count} default assumptions for {methodName} verification."
                : $"{methodName} verification selected. Choose relevant assumptions below.";
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

                // Populate SuggestedDefaults from catalog Items
                SuggestedDefaults.Clear();
                if (catalog?.Items != null)
                {
                    foreach (var item in catalog.Items)
                    {
                        SuggestedDefaults.Add(item);
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

                StatusHint = $"Loaded {SuggestedDefaults.Count} assumptions and {DefaultPresets.Count} presets.";
            }
            catch (Exception ex)
            {
                StatusHint = $"Error loading defaults catalog: {ex.Message}";
            }
        }

        public IRelayCommand ResetAssumptionsCommand { get; }
        public IRelayCommand ClearPresetFilterCommand { get; }
        public IRelayCommand SavePresetCommand { get; }

        private void ResetAssumptions()
        {
            foreach (var item in SuggestedDefaults)
            {
                item.IsEnabled = false;
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
        }
    }
}
