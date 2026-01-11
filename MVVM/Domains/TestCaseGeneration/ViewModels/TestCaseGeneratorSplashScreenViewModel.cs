using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Events;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    public partial class TestCaseGeneratorSplashScreenViewModel : BaseDomainViewModel, IDisposable
    {
        // Domain mediator (properly typed)
        private new readonly ITestCaseGenerationMediator _mediator;

        [ObservableProperty]
        private string title = "Systems ATE APP";
        
        [ObservableProperty]
        private string description = "Generate comprehensive test cases using AI-powered analysis. Import requirements, analyze context, and automatically create detailed test scenarios to ensure thorough coverage of your application's functionality.";

        public TestCaseGeneratorSplashScreenViewModel(
            ITestCaseGenerationMediator mediator,
            ILogger<TestCaseGeneratorSplashScreenViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _logger.LogDebug("TestCaseGeneratorSplashScreenViewModel initialized");
        }

        #region Abstract Method Implementations

        protected override bool CanSave() => false; // Splash screen doesn't save
        protected override async Task SaveAsync() => await Task.CompletedTask;
        protected override bool CanRefresh() => false; // Splash screen doesn't need refresh
        protected override async Task RefreshAsync() => await Task.CompletedTask;
        protected override bool CanCancel() => false; // Splash screen doesn't cancel
        protected override void Cancel() { /* No-op */ }

        #endregion
    }
}