namespace TestCaseEditorApp.MVVM.Models
{
    using CommunityToolkit.Mvvm.ComponentModel;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Represents a single assumption pill that can be toggled on/off.
    /// Loaded from defaults catalog at startup and maintained in memory.
    /// Visibility filtered by ApplicableMethods for current verification method.
    /// </summary>
    public sealed class AssumptionPill : ObservableObject
    {
        /// <summary>
        /// Unique identifier for this pill (e.g., "env_ambient25")
        /// </summary>
        public string Key { get; init; } = "";

        /// <summary>
        /// Display name shown in UI (e.g., "Ambient 25 °C")
        /// </summary>
        public string Name { get; init; } = "";

        /// <summary>
        /// Category for grouping (e.g., "Environment", "Equipment", "Test")
        /// </summary>
        public string? Category { get; init; }

        /// <summary>
        /// Detailed description of what this assumption means
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// The actual text that goes into the LLM prompt
        /// </summary>
        public string? ContentLine { get; init; }

        /// <summary>
        /// Which verification methods should show this pill.
        /// Empty list = show for all methods.
        /// </summary>
        public List<VerificationMethod> ApplicableMethods { get; init; } = new();

        /// <summary>
        /// User's current toggle state for this pill.
        /// This is the state that gets saved to Requirement.SelectedAssumptionKeys.
        /// </summary>
        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// True if this pill was auto-suggested by LLM (renders with orange accent).
        /// This is UI state only, not persisted.
        /// </summary>
        private bool _isLlmSuggested;
        [JsonIgnore]
        public bool IsLlmSuggested
        {
            get => _isLlmSuggested;
            set => SetProperty(ref _isLlmSuggested, value);
        }

        /// <summary>
        /// The text that goes into the prompt (falls back to Name if ContentLine not specified)
        /// </summary>
        public string PromptLine => string.IsNullOrWhiteSpace(ContentLine) ? Name : ContentLine!;

        /// <summary>
        /// Check if this pill should be visible for the given verification method.
        /// </summary>
        public bool IsVisibleForMethod(VerificationMethod method)
        {
            // Empty list means show for all methods
            return ApplicableMethods.Count == 0 || ApplicableMethods.Contains(method);
        }
    }
}
