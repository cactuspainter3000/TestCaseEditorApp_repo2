using System;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Services
{
    /// <summary>
    /// Focused contract for core requirement quality analysis.
    /// This is the engine-facing slice of the larger analysis service.
    /// </summary>
    public interface IRequirementAnalyzer
    {
        /// <summary>
        /// Analyze a requirement with streaming/progress support.
        /// </summary>
        Task<RequirementAnalysis> AnalyzeRequirementWithStreamingAsync(
            Requirement requirement,
            Action<string>? onPartialResult = null,
            Action<string>? onProgressUpdate = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generate the full prompt used for inspection/debugging.
        /// </summary>
        string GeneratePromptForInspection(Requirement requirement);

        /// <summary>
        /// Cache statistics for status reporting.
        /// </summary>
        RequirementAnalysisCache.CacheStatistics? CacheStatistics { get; }
    }
}