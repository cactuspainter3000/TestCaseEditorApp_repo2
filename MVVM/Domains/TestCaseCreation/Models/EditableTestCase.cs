using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models
{
    /// <summary>
    /// Clean test case model for editing workflow - no legacy dependencies
    /// </summary>
    public class EditableTestCase : ObservableObject
    {
        private string _id = string.Empty;
        private string _title = string.Empty;
        private string _preconditions = string.Empty;
        private string _steps = string.Empty;
        private string _expectedResults = string.Empty;
        private bool _isSelected;
        private bool _isDirty;
        private DateTime _lastModified = DateTime.Now;
        private string _modifiedBy = "User";

        /// <summary>
        /// Unique identifier for this test case
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>
        /// Test case title/name
        /// </summary>
        [Required(ErrorMessage = "Title is required")]
        [MaxLength(200, ErrorMessage = "Title cannot exceed 200 characters")]
        public string Title
        {
            get => _title;
            set
            {
                if (SetProperty(ref _title, value))
                {
                    MarkAsModified(nameof(Title));
                }
            }
        }

        /// <summary>
        /// Prerequisites and setup conditions
        /// </summary>
        [MaxLength(2000, ErrorMessage = "Preconditions cannot exceed 2000 characters")]
        public string Preconditions
        {
            get => _preconditions;
            set
            {
                if (SetProperty(ref _preconditions, value))
                {
                    MarkAsModified(nameof(Preconditions));
                }
            }
        }

        /// <summary>
        /// Test execution steps
        /// </summary>
        [Required(ErrorMessage = "Test steps are required")]
        [MaxLength(5000, ErrorMessage = "Steps cannot exceed 5000 characters")]
        public string Steps
        {
            get => _steps;
            set
            {
                if (SetProperty(ref _steps, value))
                {
                    MarkAsModified(nameof(Steps));
                }
            }
        }

        /// <summary>
        /// Expected test results
        /// </summary>
        [Required(ErrorMessage = "Expected results are required")]
        [MaxLength(2000, ErrorMessage = "Expected results cannot exceed 2000 characters")]
        public string ExpectedResults
        {
            get => _expectedResults;
            set
            {
                if (SetProperty(ref _expectedResults, value))
                {
                    MarkAsModified(nameof(ExpectedResults));
                }
            }
        }

        /// <summary>
        /// Whether this test case is currently selected in UI
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// Whether this test case has been modified since last save
        /// </summary>
        public bool IsDirty
        {
            get => _isDirty;
            private set => SetProperty(ref _isDirty, value);
        }

        /// <summary>
        /// When this test case was last modified
        /// </summary>
        public DateTime LastModified
        {
            get => _lastModified;
            private set => SetProperty(ref _lastModified, value);
        }

        /// <summary>
        /// Who last modified this test case
        /// </summary>
        public string ModifiedBy
        {
            get => _modifiedBy;
            private set => SetProperty(ref _modifiedBy, value);
        }

        /// <summary>
        /// Create new test case with default values
        /// </summary>
        public static EditableTestCase CreateNew(int sequenceNumber)
        {
            return new EditableTestCase
            {
                Id = Guid.NewGuid().ToString(),
                Title = $"TC-{sequenceNumber:000}: New Test Case",
                Preconditions = "",
                Steps = "1. ",
                ExpectedResults = ""
            };
        }

        /// <summary>
        /// Create test case from existing domain model
        /// </summary>
        public static EditableTestCase FromTestCase(TestCaseEditorApp.MVVM.Models.TestCase testCase)
        {
            var stepsText = string.Join("\n", testCase.Steps.Select((s, i) => $"{i + 1}. {s.StepAction}"));
            
            return new EditableTestCase
            {
                Id = testCase.Id ?? Guid.NewGuid().ToString(),
                Title = testCase.Name ?? "Untitled Test Case",
                Preconditions = "", // TestCase model doesn't have Preconditions
                Steps = stepsText,
                ExpectedResults = "" // TestCase model doesn't have ExpectedResults
            };
        }

        /// <summary>
        /// Convert to domain model for persistence
        /// </summary>
        public TestCaseEditorApp.MVVM.Models.TestCase ToTestCase()
        {
            var steps = ParseStepsFromText(Steps);
            
            return new TestCaseEditorApp.MVVM.Models.TestCase
            {
                Id = Id,
                Name = Title,
                Steps = steps.ToList()
            };
        }

        /// <summary>
        /// Mark test case as clean (saved)
        /// </summary>
        public void MarkAsClean()
        {
            IsDirty = false;
        }

        /// <summary>
        /// Set properties without triggering dirty flag (for loading)
        /// </summary>
        public void LoadPropertiesClean(string title, string preconditions, string steps, string expectedResults)
        {
            _title = title;
            _preconditions = preconditions;
            _steps = steps;
            _expectedResults = expectedResults;
            _isDirty = false;
            
            // Notify property changes without triggering dirty flag
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Preconditions));
            OnPropertyChanged(nameof(Steps));
            OnPropertyChanged(nameof(ExpectedResults));
            OnPropertyChanged(nameof(IsDirty));
        }

        private void MarkAsModified(string fieldName)
        {
            IsDirty = true;
            LastModified = DateTime.Now;
            ModifiedBy = "User";
            OnPropertyChanged(nameof(LastModified));
            OnPropertyChanged(nameof(ModifiedBy));
        }

        private static IEnumerable<TestCaseEditorApp.MVVM.Models.TestStep> ParseStepsFromText(string stepsText)
        {
            if (string.IsNullOrWhiteSpace(stepsText))
                yield break;
            
            var lines = stepsText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            int stepNumber = 1;
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine)) 
                    continue;
                
                // Remove step numbers if present (1., 2., etc.)
                var stepText = System.Text.RegularExpressions.Regex.Replace(trimmedLine, @"^\d+\.\s*", "");
                
                yield return new TestCaseEditorApp.MVVM.Models.TestStep
                {
                    StepNumber = stepNumber++,
                    StepAction = stepText
                };
            }
        }
    }
}