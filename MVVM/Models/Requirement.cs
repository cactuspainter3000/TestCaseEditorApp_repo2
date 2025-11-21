using CommunityToolkit.Mvvm.ComponentModel;
using DocumentFormat.OpenXml;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace TestCaseEditorApp.MVVM.Models
{
    public enum VerificationMethod
    {
        Analysis,
        Simulation,
        Demonstration,
        Inspection,
        ServiceHistory,
        Test,
        TestUnintendedFunction,
        Unassigned,
        VerifiedAtAnotherLevel
    }

    public partial class Requirement
    {
        /// <summary>
        /// Keys of assumption pills selected for this requirement.
        /// Persisted in the workspace so selections survive reloads.
        /// </summary>
        public HashSet<string> SelectedAssumptionKeys { get; set; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Clarifying questions asked for this requirement.
        /// Persisted in the workspace so questions/answers survive reloads.
        /// </summary>
        public List<ClarifyingQuestionData> ClarifyingQuestions { get; set; }
            = new List<ClarifyingQuestionData>();

        /// <summary>
        /// LLM-powered quality analysis of this requirement.
        /// Includes quality score, identified issues, recommendations, and freeform feedback.
        /// Persisted in the workspace so analysis results survive reloads.
        /// </summary>
        public RequirementAnalysis? Analysis { get; set; }
    }

    /// <summary>
    /// Serializable data class for persisting clarifying questions in workspace JSON.
    /// </summary>
    public class ClarifyingQuestionData
    {
        public string Text { get; set; } = string.Empty;
        public string? Answer { get; set; }
        public string? Category { get; set; }
        public string Severity { get; set; } = "OPTIONAL";
        public string? Rationale { get; set; }
        public bool MarkedAsAssumption { get; set; }
        public List<string> Options { get; set; } = new List<string>();
    }

    public partial class Requirement : ObservableObject
    {
        public Requirement()
        {
            _verificationMethodsCore.CollectionChanged += (_, __) => OnPropertyChanged(nameof(VerificationMethods));
            _validationMethodsCore.CollectionChanged += (_, __) => OnPropertyChanged(nameof(ValidationMethods));
        }

        // ===== Add this property for generated test cases =====
        public ObservableCollection<TestCase> GeneratedTestCases { get; set; } = new ObservableCollection<TestCase>();

        // ===== Identification & linkage =====
        [ObservableProperty] private string globalId = string.Empty;
        [ObservableProperty] private string apiId = string.Empty;
        [ObservableProperty] private string itemType = string.Empty;
        [ObservableProperty] private string doorsId = string.Empty;
        [ObservableProperty] private string itemPath = string.Empty;

        // ===== Project / release =====
        [ObservableProperty] private string project = string.Empty;
        [ObservableProperty] private string projectDefined = string.Empty;
        [ObservableProperty] private string release = string.Empty;
        [ObservableProperty] private string relationshipStatus = string.Empty;
        [ObservableProperty] private string doorsRelationship = string.Empty;

        // ===== Versioning & activity =====
        [ObservableProperty] private string version = string.Empty;
        [ObservableProperty] private string currentVersion = string.Empty;
        [ObservableProperty] private string lastActivityDateRaw = string.Empty;
        [ObservableProperty] private DateTime? lastActivityDate;
        [ObservableProperty] private string modifiedDateRaw = string.Empty;
        [ObservableProperty] private DateTime? modifiedDate;
        [ObservableProperty] private string createdDateRaw = string.Empty;
        [ObservableProperty] private DateTime? createdDate;

        // ===== Locking =====
        [ObservableProperty] private string locked = string.Empty;
        [ObservableProperty] private string lastLockedRaw = string.Empty;
        [ObservableProperty] private DateTime? lastLocked;
        [ObservableProperty] private string lastLockedBy = string.Empty;

        // ===== People =====
        [ObservableProperty] private string createdBy = string.Empty;
        [ObservableProperty] private string modifiedBy = string.Empty;

        // ===== Compliance / classification =====
        [ObservableProperty] private string derivedRequirement = string.Empty;
        [ObservableProperty] private string exportControlled = string.Empty;
        [ObservableProperty] private string customerId = string.Empty;
        [ObservableProperty] private string fdal = string.Empty;
        [ObservableProperty] private string keyCharacteristics = string.Empty;

        // ===== Heading & rationale =====
        [ObservableProperty] private string heading = string.Empty;
        [ObservableProperty] private string rationale = string.Empty;
        [ObservableProperty] private string complianceRationale = string.Empty;
        [ObservableProperty] private string changeDriver = string.Empty;
        [ObservableProperty] private string allocations = string.Empty;

        // ===== Tags (kept in sync) =====
        [ObservableProperty]
        private string tags = string.Empty;

        public bool HasGeneratedTestCase => !string.IsNullOrWhiteSpace(CurrentResponse?.Output);

        private List<string> _tagList = new();
        public List<string> TagList
        {
            get => _tagList;
            set
            {
                _tagList = value ?? new();
                Tags = string.Join(";", _tagList.Where(t => !string.IsNullOrWhiteSpace(t)));
                OnPropertyChanged();
            }
        }

        // ===== Relationship text blocks =====
        [ObservableProperty] private string upstreamRelationshipsText = string.Empty;
        [ObservableProperty] private string relationshipsText = string.Empty;
        [ObservableProperty] private string synchronizedItemsText = string.Empty;
        [ObservableProperty] private string commentsText = string.Empty;

        // ===== Counts =====
        [ObservableProperty] private int numberOfDownstreamRelationships;
        [ObservableProperty] private int numberOfUpstreamRelationships;
        [ObservableProperty] private int connectedUsers;
        [ObservableProperty] private int numberOfAttachments;
        [ObservableProperty] private int numberOfComments;
        [ObservableProperty] private int numberOfLinks;

        // ===== Verification =====
        [JsonIgnore] private ObservableCollection<VerificationMethod> _verificationMethodsCore = new();
        public List<VerificationMethod> VerificationMethods
        {
            get => new(_verificationMethodsCore);
            set
            {
                _verificationMethodsCore = new(value ?? new());
                OnPropertyChanged(nameof(VerificationMethods));
            }
        }
        [ObservableProperty] private VerificationMethod method = VerificationMethod.Unassigned;
        [ObservableProperty] private string verificationMethodRaw = string.Empty;
        [ObservableProperty] private string verificationMethodText = string.Empty;

        public void AddVerificationMethod(VerificationMethod m) => _verificationMethodsCore.Add(m);
        public bool RemoveVerificationMethod(VerificationMethod m) => _verificationMethodsCore.Remove(m);
        public void SetVerificationMethods(IEnumerable<VerificationMethod> methods) =>
            VerificationMethods = methods?.ToList() ?? new();

        // ===== Validation =====
        // (Assumes you have a ValidationMethod enum)
        [JsonIgnore] private ObservableCollection<ValidationMethod> _validationMethodsCore = new();
        public List<ValidationMethod> ValidationMethods
        {
            get => new(_validationMethodsCore);
            set
            {
                _validationMethodsCore = new(value ?? new());
                OnPropertyChanged(nameof(ValidationMethods));
            }
        }
        [ObservableProperty] private string validationMethodRaw = string.Empty;
        [ObservableProperty] private string validationMethodText = string.Empty;
        [ObservableProperty] private string validationEvidence = string.Empty;
        [ObservableProperty] private string validationConclusion = string.Empty;

        // ---- Core requirement text ----
        [ObservableProperty] private string item = string.Empty;        // e.g., C1XMA2405-REQ_RC-108
        [ObservableProperty] private string name = string.Empty;        // short title
        [ObservableProperty] private string description = string.Empty; // main requirement text

        // ---- Jama/meta fields ----
        [ObservableProperty] private string status = string.Empty;
        [ObservableProperty] private string requirementType = string.Empty;
        [ObservableProperty] private string safetyRequirement = string.Empty;
        [ObservableProperty] private string safetyRationale = string.Empty;
        [ObservableProperty] private string securityRequirement = string.Empty;
        [ObservableProperty] private string securityRationale = string.Empty;
        [ObservableProperty] private string statementOfCompliance = string.Empty;
        [ObservableProperty] private string robustRequirement = string.Empty;
        [ObservableProperty] private string robustRationale = string.Empty;
        [ObservableProperty] private string upstreamCrossInstanceRelationships = string.Empty;
        [ObservableProperty] private string downstreamCrossInstanceRelationships = string.Empty;

        // ---- Context captured from Jama export (folder/set) ----
        [ObservableProperty] private string? folderPath;
        [ObservableProperty] private string? setName;
        [ObservableProperty] private string? setId;

        // ---- Editor/test-generation bookkeeping ----
        [ObservableProperty] private List<RequirementTable> tables = new();
        [ObservableProperty] private RequirementLooseContent looseContent = new();

        // ===== SINGLE saved LLM response =====
        public class LlmDraft
        {
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
            public string Output { get; set; } = string.Empty;
            public string? Model { get; set; }
            public string? Notes { get; set; }
        }

        [ObservableProperty] private LlmDraft? currentResponse;

        // ---- Transient (not persisted) ----
        [JsonIgnore] public List<OpenXmlElement> ConsumedElements { get; } = new();
        [JsonIgnore] public string? LastAppendedHash { get; set; }

        // ===== Helpers =====
        public void SaveResponse(string output, string? model = null, string? notes = null)
        {
            CurrentResponse = new LlmDraft
            {
                Timestamp = DateTime.UtcNow,
                Output = output ?? string.Empty,
                Model = model,
                Notes = notes
            };
            // No need to set HasGeneratedTestCase; it's computed.
        }

        partial void OnTagsChanged(string value)
        {
            _tagList = (value ?? string.Empty)
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
            OnPropertyChanged(nameof(TagList));
        }

        partial void OnCurrentResponseChanged(Requirement.LlmDraft? value)
        {
            OnPropertyChanged(nameof(HasGeneratedTestCase));
        }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Item) && !string.IsNullOrWhiteSpace(Name))
                return $"{Item} — {Name}";
            if (!string.IsNullOrWhiteSpace(Item))
                return Item;
            return Name ?? base.ToString();
        }

    }
}