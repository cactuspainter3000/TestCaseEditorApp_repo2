using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Interface for generating synthetic training data pairs (ATP steps -> system capabilities)
    /// </summary>
    public interface ISyntheticTrainingDataGenerator
    {
        /// <summary>
        /// Generates a synthetic training dataset with ATP steps and expected capability derivations
        /// </summary>
        /// <param name="options">Configuration options for data generation</param>
        /// <returns>Complete synthetic training dataset with quality metrics</returns>
        Task<SyntheticTrainingDataset> GenerateTrainingDatasetAsync(TrainingDataGenerationOptions options);
    }
}