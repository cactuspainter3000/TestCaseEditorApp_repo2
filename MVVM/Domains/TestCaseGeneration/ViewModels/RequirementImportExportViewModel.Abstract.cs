using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// Abstract method implementations for RequirementImportExportViewModel.
    /// Following enhanced prevention checklist pattern from working examples.
    /// </summary>
    public partial class RequirementImportExportViewModel
    {
        #region Abstract Method Implementations

        protected override bool CanSave() => _getIsDirty?.Invoke() ?? false;
        
        protected override async Task SaveAsync()
        {
            _logger.LogDebug("Saving import/export data");
            _saveSessionAuto?.Invoke();
            await Task.CompletedTask;
        }
        
        protected override bool CanRefresh() => true;
        
        protected override async Task RefreshAsync()
        {
            _logger.LogDebug("Refreshing import/export data");
            _refreshSupportingInfo?.Invoke();
            await Task.CompletedTask;
        }
        
        protected override bool CanCancel() => _getIsDirty?.Invoke() ?? false;
        
        protected override void Cancel()
        {
            _logger.LogDebug("Canceling import/export operations");
            _setIsDirty?.Invoke(false);
        }

        #endregion
    }
}