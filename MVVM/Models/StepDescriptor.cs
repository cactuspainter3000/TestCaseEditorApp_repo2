namespace TestCaseEditorApp.MVVM.Models
{
    using CommunityToolkit.Mvvm.ComponentModel;
    using System;

    // Simple descriptor for each left-menu step.
    // CreateViewModel is called with the application's IServiceProvider to produce the VM instance for the content area.
    public class StepDescriptor : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        // Called to produce the viewmodel (or view-model-like object) shown in the central ContentControl
        public Func<IServiceProvider, object>? CreateViewModel { get; set; }

        // Optional badge / status object (string/number/Icon). Raise change notifications so the UI updates.
        private object? _badge;
        public object? Badge
        {
            get => _badge;
            set => SetProperty(ref _badge, value);
        }

        // New: indicate this step offers a per-step file/menu dropdown (e.g. Requirements).
        // Observable so UI updates if you switch it at runtime.
        private bool _hasFileMenu;
        public bool HasFileMenu
        {
            get => _hasFileMenu;
            set => SetProperty(ref _hasFileMenu, value);
        }

        // Track whether the file menu is expanded
        private bool _isFileMenuExpanded;
        public bool IsFileMenuExpanded
        {
            get => _isFileMenuExpanded;
            set => SetProperty(ref _isFileMenuExpanded, value);
        }

        // Whether this step can be directly selected from the menu (false for workflow-only steps)
        private bool _isSelectable = true;
        public bool IsSelectable
        {
            get => _isSelectable;
            set => SetProperty(ref _isSelectable, value);
        }
    }
}