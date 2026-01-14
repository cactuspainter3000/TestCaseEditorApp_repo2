using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// Abstract method implementations for ChatGptExportAnalysisViewModel.
    /// Following enhanced prevention checklist pattern from working examples.
    /// </summary>
    public partial class ChatGptExportAnalysisViewModel
    {
        #region Abstract Method Implementations

        protected override bool CanSave() => false; // Export operations don't save directly
        protected override async Task SaveAsync() => await Task.CompletedTask;
        protected override bool CanRefresh() => true;
        protected override async Task RefreshAsync()
        {
            _logger.LogDebug("Refreshing ChatGPT export analysis data");
            // Could refresh export data or regenerate exports if needed
            await Task.CompletedTask;
        }
        protected override bool CanCancel() => false; // Export operations aren't cancellable in this context
        protected override void Cancel() { /* No-op */ }

        #endregion
    }
}