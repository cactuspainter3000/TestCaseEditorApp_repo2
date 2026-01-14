using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Header ViewModel for Requirements domain.
    /// Provides contextual header information and quick stats.
    /// Following architectural guide patterns for header ViewModels.
    /// </summary>
    public partial class Requirements_HeaderViewModel : ObservableObject
    {
        private readonly IRequirementsMediator _mediator;
        private readonly ILogger<Requirements_HeaderViewModel> _logger;

        [ObservableProperty]
        private int totalRequirements;

        [ObservableProperty]
        private int analyzedRequirements;

        [ObservableProperty]
        private int pendingRequirements;

        [ObservableProperty]
        private string importSource = "None";

        [ObservableProperty]
        private bool hasRequirements;

        [ObservableProperty]
        private string analysisProgress = "0%";

        public Requirements_HeaderViewModel(
            IRequirementsMediator mediator,
            ILogger<Requirements_HeaderViewModel> logger)
        {
            _mediator = mediator ?? throw new System.ArgumentNullException(nameof(mediator));
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));

            // Subscribe to requirements events for real-time updates
            _mediator.Subscribe<RequirementsEvents.RequirementsImported>(OnRequirementsImported);
            _mediator.Subscribe<RequirementsEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
            _mediator.Subscribe<RequirementsEvents.RequirementAnalyzed>(OnRequirementAnalyzed);

            // Initialize values
            UpdateStatistics();

            _logger.LogDebug("Requirements_HeaderViewModel initialized");
        }

        private void OnRequirementsImported(RequirementsEvents.RequirementsImported e)
        {
            ImportSource = System.IO.Path.GetFileName(e.SourceFile) ?? "Unknown";
            UpdateStatistics();
        }

        private void OnRequirementsCollectionChanged(RequirementsEvents.RequirementsCollectionChanged e)
        {
            UpdateStatistics();
        }

        private void OnRequirementAnalyzed(RequirementsEvents.RequirementAnalyzed e)
        {
            UpdateStatistics();
        }

        private void UpdateStatistics()
        {
            TotalRequirements = _mediator.Requirements.Count;
            AnalyzedRequirements = _mediator.Requirements.Count(r => r.Analysis != null);
            PendingRequirements = TotalRequirements - AnalyzedRequirements;
            HasRequirements = TotalRequirements > 0;

            // Calculate analysis progress percentage
            if (TotalRequirements > 0)
            {
                var percentage = (double)AnalyzedRequirements / TotalRequirements * 100;
                AnalysisProgress = $"{percentage:F0}%";
            }
            else
            {
                AnalysisProgress = "0%";
            }

            _logger.LogDebug("Requirements statistics updated: {Total} total, {Analyzed} analyzed",
                TotalRequirements, AnalyzedRequirements);
        }

        // Computed properties for display
        public string RequirementsStatus =>
            TotalRequirements switch
            {
                0 => "No requirements loaded",
                1 => "1 requirement loaded",
                _ => $"{TotalRequirements} requirements loaded"
            };

        public string AnalysisStatus =>
            AnalyzedRequirements switch
            {
                0 when TotalRequirements > 0 => "Analysis not started",
                var count when count == TotalRequirements && TotalRequirements > 0 => "Analysis complete",
                var count when count > 0 => $"{count} of {TotalRequirements} analyzed",
                _ => ""
            };

        public List<(string Label, string Value)> GetQuickStats()
        {
            return new List<(string, string)>
            {
                ("Total", TotalRequirements.ToString()),
                ("Analyzed", AnalyzedRequirements.ToString()),
                ("Pending", PendingRequirements.ToString()),
                ("Progress", AnalysisProgress),
                ("Source", ImportSource)
            };
        }

        // Notify when computed properties change
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.PropertyName == nameof(TotalRequirements) || 
                e.PropertyName == nameof(AnalyzedRequirements) || 
                e.PropertyName == nameof(PendingRequirements))
            {
                OnPropertyChanged(nameof(RequirementsStatus));
                OnPropertyChanged(nameof(AnalysisStatus));
            }
        }
    }
}