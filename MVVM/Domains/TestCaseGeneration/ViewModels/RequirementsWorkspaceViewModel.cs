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
            
            // When workspace loads, sync current requirement selection to populate metadata
            // This triggers metadata loading without changing the actual selection
            SyncCurrentRequirementMetadata();
        }

        /// <summary>
        /// Synchronize current requirement selection to ensure metadata is populated
        /// when Requirements workspace is loaded. This fires RequirementSelected event
        /// for the currently selected requirement (if any) without changing the selection.
        /// </summary>
        private void SyncCurrentRequirementMetadata()
        {
            try
            {
                // Check if there's a currently selected requirement in the TestCaseGenerator_VM
                var currentRequirement = TestCaseGeneratorVM.SelectedRequirement;
                if (currentRequirement != null)
                {
                    // Fire RequirementSelected event to ensure metadata gets populated
                    // This doesn't change the selection, just ensures UI is in sync
                    var mediator = _mediator as ITestCaseGenerationMediator;
                    mediator?.PublishEvent(new TestCaseGenerationEvents.RequirementSelected
                    {
                        Requirement = currentRequirement,
                        SelectedBy = "RequirementsWorkspaceSync"
                    });
                    
                    _logger.LogDebug("[RequirementsWorkspaceViewModel] Synced metadata for requirement: {RequirementId}", 
                        currentRequirement.GlobalId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RequirementsWorkspaceViewModel] Failed to sync current requirement metadata");
            }
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