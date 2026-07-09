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
    /// LAZY LOADING: ViewModels are created on-demand when tabs are selected to avoid UI freeze on startup.
    /// </summary>
    public partial class RequirementsTabSelectorViewModel : ObservableObject
    {
        // Factories for lazy instantiation
        private readonly Func<UnifiedRequirementsMainViewModel> _mainViewModelFactory;
        private readonly Func<CleanupViewModel> _cleanupViewModelFactory;
        private readonly Func<RequirementsSearchAttachmentsViewModel> _attachmentsViewModelFactory;
        private readonly Func<RequirementsUtilitiesViewModel> _utilitiesViewModelFactory;
        private readonly ILogger<RequirementsTabSelectorViewModel> _logger;

        // Cached instances (lazy-loaded)
        private UnifiedRequirementsMainViewModel? _mainViewModel;
        private CleanupViewModel? _cleanupViewModel;
        private RequirementsSearchAttachmentsViewModel? _attachmentsViewModel;
        private RequirementsUtilitiesViewModel? _utilitiesViewModel;

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
            Func<UnifiedRequirementsMainViewModel> mainViewModelFactory,
            Func<CleanupViewModel> cleanupViewModelFactory,
            Func<RequirementsSearchAttachmentsViewModel> attachmentsViewModelFactory,
            Func<RequirementsUtilitiesViewModel> utilitiesViewModelFactory,
            ILogger<RequirementsTabSelectorViewModel> logger)
        {
            _mainViewModelFactory = mainViewModelFactory ?? throw new ArgumentNullException(nameof(mainViewModelFactory));
            _cleanupViewModelFactory = cleanupViewModelFactory ?? throw new ArgumentNullException(nameof(cleanupViewModelFactory));
            _attachmentsViewModelFactory = attachmentsViewModelFactory ?? throw new ArgumentNullException(nameof(attachmentsViewModelFactory));
            _utilitiesViewModelFactory = utilitiesViewModelFactory ?? throw new ArgumentNullException(nameof(utilitiesViewModelFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Start with no content loaded to avoid UI freeze
            // ViewModels are created on-demand when user selects a tab
            CurrentContentViewModel = null;

            // Watch for tab selection changes
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SelectedTab))
                {
                    UpdateContentViewModel();
                }
            };

            _logger.LogInformation("[RequirementsTabSelectorViewModel] Lazy-loading tab selector ready (no content until tab selected)");
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
        /// Update the content ViewModel based on current tab selection, creating ViewModels on-demand
        /// </summary>
        private void UpdateContentViewModel()
        {
            CurrentContentViewModel = SelectedTab switch
            {
                WorkspaceTab.Main => GetOrCreateMainViewModel(),
                WorkspaceTab.Cleanup => GetOrCreateCleanupViewModel(),
                WorkspaceTab.Attachments => GetOrCreateAttachmentsViewModel(),
                WorkspaceTab.Utilities => GetOrCreateUtilitiesViewModel(),
                _ => GetOrCreateMainViewModel()
            };

            _logger.LogInformation("[RequirementsTabSelectorViewModel] Switched to tab: {Tab}", SelectedTab);
        }

        private UnifiedRequirementsMainViewModel GetOrCreateMainViewModel()
        {
            if (_mainViewModel == null)
            {
                _logger.LogInformation("[RequirementsTabSelectorViewModel] Creating Main ViewModel (lazy)");
                _mainViewModel = _mainViewModelFactory();
            }
            return _mainViewModel;
        }

        private CleanupViewModel GetOrCreateCleanupViewModel()
        {
            if (_cleanupViewModel == null)
            {
                _logger.LogInformation("[RequirementsTabSelectorViewModel] Creating Cleanup ViewModel (lazy)");
                _cleanupViewModel = _cleanupViewModelFactory();
            }
            return _cleanupViewModel;
        }

        private RequirementsSearchAttachmentsViewModel GetOrCreateAttachmentsViewModel()
        {
            if (_attachmentsViewModel == null)
            {
                _logger.LogInformation("[RequirementsTabSelectorViewModel] Creating Attachments ViewModel (lazy)");
                _attachmentsViewModel = _attachmentsViewModelFactory();
            }
            return _attachmentsViewModel;
        }

        private RequirementsUtilitiesViewModel GetOrCreateUtilitiesViewModel()
        {
            if (_utilitiesViewModel == null)
            {
                _logger.LogInformation("[RequirementsTabSelectorViewModel] Creating Utilities ViewModel (lazy)");
                _utilitiesViewModel = _utilitiesViewModelFactory();
            }
            return _utilitiesViewModel;
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
