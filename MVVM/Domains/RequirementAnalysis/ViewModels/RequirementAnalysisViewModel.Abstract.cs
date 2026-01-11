using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// Abstract method implementations for RequirementAnalysisViewModel.
    /// Following enhanced prevention checklist pattern from working examples.
    /// </summary>
    public partial class RequirementAnalysisViewModel
    {
        #region Abstract Method Implementations

        protected override bool CanSave() => false; // Analysis doesn't save directly
        protected override async Task SaveAsync() => await Task.CompletedTask;
        protected override bool CanRefresh() => true;
        protected override async Task RefreshAsync()
        {
            _logger.LogDebug("Refreshing requirement analysis data");
            // Could trigger re-analysis here if needed
            await Task.CompletedTask;
        }
        protected override bool CanCancel() => false; // Analysis operations aren't cancellable in this context
        protected override void Cancel() { /* No-op */ }

        #endregion
    }
}