using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services;
using TestCaseEditorApp.MVVM.Models;
using ValidationResult = TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult;

namespace TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Mediators
{
    /// <summary>
    /// Interface for Training Data Validation domain mediator
    /// </summary>
    public interface ITrainingDataValidationMediator
    {
        /// <summary>
        /// Starts a new validation workflow session
        /// </summary>
        Task StartValidationSessionAsync(SyntheticTrainingDataset dataset);

        /// <summary>
        /// Records a validation result for an example
        /// </summary>
        Task RecordValidationAsync(ValidationResult validationResult);

        /// <summary>
        /// Completes the current validation session
        /// </summary>
        Task CompleteValidationSessionAsync(string sessionId);

        /// <summary>
        /// Gets the current validation progress
        /// </summary>
        ValidationProgress GetCurrentProgress();
    }

    /// <summary>
    /// Represents validation progress information
    /// </summary>
    public class ValidationProgress
    {
        public int TotalExamples { get; set; }
        public int ValidatedExamples { get; set; }
        public int ApprovedExamples { get; set; }
        public int RejectedExamples { get; set; }
        public double CompletionPercentage => TotalExamples > 0 ? (double)ValidatedExamples / TotalExamples * 100 : 0;
    }
}