using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Tab selector for Requirements domain workspaces.
    /// Allows user to switch between Main requirements view, Cleanup editor, and Attachments search.
    /// </summary>
    public partial class RequirementsTabSelectorViewModel : ObservableObject
    {
        private readonly UnifiedRequirementsMainViewModel _mainViewModel;
        private readonly CleanupViewModel _cleanupViewModel;
        private readonly RequirementsSearchAttachmentsViewModel _attachmentsViewModel;
        private readonly RequirementsUtilitiesViewModel _utilitiesViewModel;
        private readonly ILogger<RequirementsTabSelectorViewModel> _logger;

        public enum WorkspaceTab
        {
            Main,
            Cleanup,
            Attachments,
            Utilities
        }

        [ObservableProperty]
        private WorkspaceTab selectedTab = WorkspaceTab.Main;

        [ObservableProperty]
        private object? currentContentViewModel;

        public RequirementsTabSelectorViewModel(
            UnifiedRequirementsMainViewModel mainViewModel,
            CleanupViewModel cleanupViewModel,
            RequirementsSearchAttachmentsViewModel attachmentsViewModel,
            RequirementsUtilitiesViewModel utilitiesViewModel,
            ILogger<RequirementsTabSelectorViewModel> logger)
        {
            _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
            _cleanupViewModel = cleanupViewModel ?? throw new ArgumentNullException(nameof(cleanupViewModel));
            _attachmentsViewModel = attachmentsViewModel ?? throw new ArgumentNullException(nameof(attachmentsViewModel));
            _utilitiesViewModel = utilitiesViewModel ?? throw new ArgumentNullException(nameof(utilitiesViewModel));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Set initial content
            CurrentContentViewModel = _mainViewModel;

            // Watch for tab selection changes
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SelectedTab))
                {
                    UpdateContentViewModel();
                }
            };
        }

        /// <summary>
        /// Select the Main requirements view
        /// </summary>
        [RelayCommand]
        public void SelectMainTab()
        {
            SelectedTab = WorkspaceTab.Main;
        }

        /// <summary>
        /// Select the Cleanup editor tab
        /// </summary>
        [RelayCommand]
        public void SelectCleanupTab()
        {
            SelectedTab = WorkspaceTab.Cleanup;
        }

        /// <summary>
        /// Select the Attachments search tab
        /// </summary>
        [RelayCommand]
        public void SelectAttachmentsTab()
        {
            SelectedTab = WorkspaceTab.Attachments;
        }

        /// <summary>
        /// Select the Utilities tab
        /// </summary>
        [RelayCommand]
        public void SelectUtilitiesTab()
        {
            SelectedTab = WorkspaceTab.Utilities;
        }

        /// <summary>
        /// Update the content ViewModel based on current tab selection
        /// </summary>
        private void UpdateContentViewModel()
        {
            CurrentContentViewModel = SelectedTab switch
            {
                WorkspaceTab.Main => _mainViewModel,
                WorkspaceTab.Cleanup => _cleanupViewModel,
                WorkspaceTab.Attachments => _attachmentsViewModel,
                WorkspaceTab.Utilities => _utilitiesViewModel,
                _ => _mainViewModel
            };

            _logger.LogInformation("[RequirementsTabSelectorViewModel] Switched to tab: {Tab}", SelectedTab);
        }

        /// <summary>
        /// Check if a specific tab is currently selected
        /// </summary>
        public bool IsTabSelected(WorkspaceTab tab) => SelectedTab == tab;

        /// <summary>
        /// Get display name for a tab
        /// </summary>
        public static string GetTabName(WorkspaceTab tab) => tab switch
        {
            WorkspaceTab.Main => "Requirements",
            WorkspaceTab.Cleanup => "Cleanup",
            WorkspaceTab.Attachments => "Attachments",
            _ => "Unknown"
        };
    }
}
