using System;
using System.Windows;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    // Partial: subscribe to the process-wide LlmConnectionManager and reflect its state
    // into the header VM's IsLlmConnected property. This partial MUST NOT declare or
    // re-declare IsLlmConnected or IsLlmBusy (those are already source-generated).
    public partial class TestCaseCreatorHeaderViewModel
    {
        // Call this after constructing the header VM (e.g. from MainViewModel).
        public void AttachConnectionManager()
        {
            // Initialize from current global state
            IsLlmConnected = LlmConnectionManager.IsConnected;

            // Subscribe for future changes
            LlmConnectionManager.ConnectionChanged += OnGlobalConnectionChanged;
        }

        private void OnGlobalConnectionChanged(bool connected)
        {
            // Marshal to UI thread if necessary
            var disp = Application.Current?.Dispatcher;
            void apply() => IsLlmConnected = connected;

            if (disp != null && !disp.CheckAccess())
                disp.Invoke(apply);
            else
                apply();
        }

        // Call on dispose / when the header VM is no longer used
        public void DetachConnectionManager()
        {
            try { LlmConnectionManager.ConnectionChanged -= OnGlobalConnectionChanged; } catch { }
        }
    }
}