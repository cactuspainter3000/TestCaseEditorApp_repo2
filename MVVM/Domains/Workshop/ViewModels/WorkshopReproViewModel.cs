using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Models;
using RequirementAnalysis = TestCaseEditorApp.MVVM.Models.RequirementAnalysis;

namespace TestCaseEditorApp.MVVM.Domains.Workshop.ViewModels
{
    public enum RequirementLifecycleStage
    {
        Edit,
        StagedForCommit,
        Committed
    }

    public partial class WorkshopReproViewModel : ObservableObject
    {
        private readonly IRequirementsMediator _mediator;

        // Per-requirement lifecycle state — stored here, not on the model
        private readonly Dictionary<string, RequirementLifecycleStage> _lifecycleStates = new();

        [ObservableProperty]
        private Requirement? currentRequirement;

        [ObservableProperty]
        private string requirementNameDisplay = "Requirement: (Name (not set))";

        [ObservableProperty]
        private int editCount;

        [ObservableProperty]
        private int stagedCount;

        [ObservableProperty]
        private int committedCount;

        [ObservableProperty]
        private bool isAnalyzing;

        [ObservableProperty]
        private string analysisStatusText = string.Empty;

        [ObservableProperty]
        private bool isAnalysisModalOpen;

        [ObservableProperty]
        private RequirementAnalysis? analysisResults;

        public int TotalCount => _mediator.Requirements.Count;

        public string CommitStagedButtonText => $"Commit Staged ({StagedCount})";

        public ObservableCollection<Requirement> Requirements => _mediator.Requirements;

        public Requirement? SelectedRequirement
        {
            get => CurrentRequirement;
            set
            {
                if (value != null && value != CurrentRequirement)
                    _mediator.SelectRequirement(value);
            }
        }

        public WorkshopReproViewModel(IRequirementsMediator mediator)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

            _mediator.Subscribe<RequirementsEvents.RequirementSelected>(OnRequirementSelected);
            _mediator.Requirements.CollectionChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(TotalCount));
                RefreshLifecycleCounts();
                NotifyNavigationCanExecute();
            };

            // Restore existing current requirement if the mediator already has one loaded
            if (_mediator.CurrentRequirement != null)
                ApplyCurrentRequirement(_mediator.CurrentRequirement);
        }

        // ===== Event handlers =====

        private void OnRequirementSelected(RequirementsEvents.RequirementSelected e)
        {
            ApplyCurrentRequirement(e.Requirement);
        }

        private void ApplyCurrentRequirement(Requirement? req)
        {
            CurrentRequirement = req;
            RequirementNameDisplay = req != null
                ? $"Requirement: {(!string.IsNullOrWhiteSpace(req.Name) ? req.Name : req.Item ?? "(not set)")}"
                : "Requirement: (Name (not set))";

            NotifyNavigationCanExecute();
            OnPropertyChanged(nameof(SelectedRequirement));
        }

        // ===== Commands =====

        [RelayCommand(CanExecute = nameof(CanAnalyze))]
        private async Task LlmAnalyzeRequirement(CancellationToken ct)
        {
            if (CurrentRequirement == null) return;
            try
            {
                IsAnalyzing = true;
                AnalysisStatusText = "Analyzing…";
                IsAnalysisModalOpen = true;
                
                var success = await _mediator.AnalyzeRequirementAsync(CurrentRequirement);
                
                // Capture analysis results from the requirement object
                AnalysisResults = CurrentRequirement.Analysis;
                
                AnalysisStatusText = success ? "Analysis complete." : "Analysis completed with warnings.";
            }
            catch (Exception ex)
            {
                AnalysisStatusText = $"Analysis failed: {ex.Message}";
                AnalysisResults = null;
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private bool CanAnalyze() => CurrentRequirement != null && !IsAnalyzing;

        [RelayCommand(CanExecute = nameof(CanStage))]
        private void Stage()
        {
            if (CurrentRequirement == null) return;
            var key = GetKey(CurrentRequirement);
            _lifecycleStates[key] = RequirementLifecycleStage.StagedForCommit;
            RefreshLifecycleCounts();
        }

        private bool CanStage() =>
            CurrentRequirement != null &&
            GetCurrentStage(CurrentRequirement) == RequirementLifecycleStage.Edit;

        [RelayCommand(CanExecute = nameof(CanUnstage))]
        private void Unstage()
        {
            if (CurrentRequirement == null) return;
            var key = GetKey(CurrentRequirement);
            _lifecycleStates[key] = RequirementLifecycleStage.Edit;
            RefreshLifecycleCounts();
        }

        private bool CanUnstage() =>
            CurrentRequirement != null &&
            GetCurrentStage(CurrentRequirement) == RequirementLifecycleStage.StagedForCommit;

        [RelayCommand(CanExecute = nameof(CanCommitStaged))]
        private void CommitStaged()
        {
            // Stub — Jama write integration will be wired here
            foreach (var key in new List<string>(_lifecycleStates.Keys))
            {
                if (_lifecycleStates[key] == RequirementLifecycleStage.StagedForCommit)
                    _lifecycleStates[key] = RequirementLifecycleStage.Committed;
            }
            RefreshLifecycleCounts();
        }

        private bool CanCommitStaged() => StagedCount > 0;

        [RelayCommand]
        private void CloseAnalysisModal()
        {
            IsAnalysisModalOpen = false;
            AnalysisResults = null;
            AnalysisStatusText = string.Empty;
        }

        [RelayCommand(CanExecute = nameof(CanNavigatePrevious))]
        private void PreviousRequirement()
        {
            _mediator.NavigateToPrevious();
        }

        private bool CanNavigatePrevious() =>
            _mediator.Requirements.Count > 0 && _mediator.GetCurrentRequirementIndex() > 0;

        [RelayCommand(CanExecute = nameof(CanNavigateNext))]
        private void NextRequirement()
        {
            _mediator.NavigateToNext();
        }

        private bool CanNavigateNext()
        {
            var idx = _mediator.GetCurrentRequirementIndex();
            return idx >= 0 && idx < _mediator.Requirements.Count - 1;
        }

        // ===== Helpers =====

        private static string GetKey(Requirement req) =>
            req.GlobalId ?? req.Item ?? req.GetHashCode().ToString();

        private RequirementLifecycleStage GetCurrentStage(Requirement? req)
        {
            if (req == null) return RequirementLifecycleStage.Edit;
            var key = GetKey(req);
            return _lifecycleStates.TryGetValue(key, out var stage) ? stage : RequirementLifecycleStage.Edit;
        }

        private void RefreshLifecycleCounts()
        {
            int edit = 0, staged = 0, committed = 0;
            foreach (var req in _mediator.Requirements)
            {
                switch (GetCurrentStage(req))
                {
                    case RequirementLifecycleStage.StagedForCommit: staged++; break;
                    case RequirementLifecycleStage.Committed: committed++; break;
                    default: edit++; break;
                }
            }
            EditCount = edit;
            StagedCount = staged;
            CommittedCount = committed;
            OnPropertyChanged(nameof(CommitStagedButtonText));
            ((RelayCommand)StageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)UnstageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CommitStagedCommand).NotifyCanExecuteChanged();
        }

        private void NotifyNavigationCanExecute()
        {
            ((RelayCommand)PreviousRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)NextRequirementCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)LlmAnalyzeRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)StageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)UnstageCommand).NotifyCanExecuteChanged();
        }

        partial void OnIsAnalyzingChanged(bool value) =>
            ((AsyncRelayCommand)LlmAnalyzeRequirementCommand).NotifyCanExecuteChanged();
    }
}
