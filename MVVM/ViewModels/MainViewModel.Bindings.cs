using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    // Partial extension to provide small binding surface expected by the MainWindow XAML.
    public partial class MainViewModel : ObservableObject
    {
        // Title shown in Window title binding (MainWindow.Title -> DisplayName)
        [ObservableProperty]
        private string displayName = "Test Case Editor";

        // Collection expected by a ListBox in the main view for test flow steps.
        // Use object for now; replace with your concrete FlowStep model if available.
        public ObservableCollection<object> TestFlowSteps { get; } = new();

        // Selected item binding for the ListBox
        [ObservableProperty]
        private object? selectedFlowStep;

        // SAP status text shown in the UI
        [ObservableProperty]
        private string sapStatus = string.Empty;

        // Foreground brush bound to the status text to reflect state (green/orange/transparent).
        // This is a read-only computed property; we raise change notifications when SapStatus changes.
        public Brush SapForegroundStatus
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SapStatus)) return Brushes.Transparent;
                return string.Equals(SapStatus, "OK", StringComparison.OrdinalIgnoreCase) || string.Equals(SapStatus, "Connected", StringComparison.OrdinalIgnoreCase)
                    ? Brushes.LightGreen
                    : Brushes.Orange;
            }
        }

        // The ObservableProperty source generator will create a partial method we can implement to react to changes.
        partial void OnSapStatusChanged(string value)
        {
            // Notify the computed brush property changed so bindings update when SapStatus changes.
            OnPropertyChanged(nameof(SapForegroundStatus));
        }
    }
}