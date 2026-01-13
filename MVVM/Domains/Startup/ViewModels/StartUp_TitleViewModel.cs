using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.Startup.Mediators;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Startup.ViewModels
{
    /// <summary>
    /// StartUp TitleWorkspace ViewModel - Following AI Guide patterns
    /// </summary>
    public partial class StartUp_TitleViewModel : BaseDomainViewModel
    {
        private new readonly IStartupMediator _mediator;
        
        [ObservableProperty]
        private string title = "Systems ATE APP";
        
        [ObservableProperty]
        private string version = "v2.0";
        
        public StartUp_TitleViewModel(
            IStartupMediator mediator,
            ILogger<StartUp_TitleViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        }
        
        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====
        
        protected override async Task SaveAsync()
        {
            await Task.Delay(100);
        }
        
        protected override void Cancel()
        {
            Title = "Systems ATE APP";
        }
        
        protected override async Task RefreshAsync()
        {
            await Task.Delay(50);
        }
        
        protected override bool CanSave() => !IsBusy;
        protected override bool CanCancel() => true;
        protected override bool CanRefresh() => !IsBusy;
    }
}