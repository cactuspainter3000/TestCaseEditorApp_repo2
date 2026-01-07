using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Mediators;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// Dedicated workspace ViewModel for requirements management.
    /// Provides a focused requirements interface using the existing TestCaseGenerator_VM.
    /// </summary>
    public partial class RequirementsWorkspaceViewModel : BaseDomainViewModel
    {
        [ObservableProperty]
        private string title = "Requirements";
        
        [ObservableProperty]
        private string description = "View and manage requirements with tables and supplemental information";

        /// <summary>
        /// The TestCaseGenerator_VM that handles all the complex requirements UI
        /// </summary>
        public TestCaseGenerator_VM TestCaseGeneratorVM { get; }

        public RequirementsWorkspaceViewModel(
            ITestCaseGenerationMediator mediator, 
            TestCaseGenerator_VM testCaseGeneratorVM,
            ILogger<RequirementsWorkspaceViewModel> logger) 
            : base(mediator, logger)
        {
            // Use the provided TestCaseGenerator_VM which handles all requirements UI
            TestCaseGeneratorVM = testCaseGeneratorVM ?? throw new ArgumentNullException(nameof(testCaseGeneratorVM));
            
            logger.LogDebug("[RequirementsWorkspaceViewModel] Initialized with TestCaseGenerator_VM");
            
            // REMOVED: Automatic requirement selection on workspace load
            // This was causing unwanted jumping to first requirement when navigating to Requirements view
            // Requirements should maintain their current selection when navigating between views
        }

        // Implementation of abstract methods from BaseDomainViewModel
        protected override Task SaveAsync()
        {
            // Delegate to TestCaseGenerator_VM if it has save functionality
            return Task.CompletedTask;
        }

        protected override void Cancel()
        {
            // Clear any selections or reset state if needed
            _logger.LogDebug("[RequirementsWorkspaceViewModel] Cancel called");
        }

        protected override Task RefreshAsync()
        {
            // Refresh the requirements view
            _logger.LogDebug("[RequirementsWorkspaceViewModel] Refresh requested");
            return Task.CompletedTask;
        }

        protected override bool CanSave() => false; // No save functionality for requirements view
        protected override bool CanCancel() => true;
        protected override bool CanRefresh() => true;
    }
}