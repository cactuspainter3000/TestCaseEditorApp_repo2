using System.Windows.Media;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Header ViewModel used by the Test Case Creator area.
    /// Exposes visible state (source-generated via [ObservableProperty]) and
    /// command properties (ICommand/IRelayCommand) that MainViewModel wires.
    /// </summary>
    public partial class TestCaseCreatorHeaderViewModel : ObservableObject
    {
        // Visible state (source-gen will create backing fields + INotify support)
        [ObservableProperty] private bool isLlmConnected;
        [ObservableProperty] private bool isLlmBusy; // new: source-generated IsLlmBusy
        [ObservableProperty] private string? workspaceName = "Workspace";
        [ObservableProperty] private string? currentRequirementName;
        [ObservableProperty] private int requirementsWithTestCasesCount;
        [ObservableProperty] private string? statusHint;
        [ObservableProperty] private string? currentRequirementSummary;

        // Primary header action commands (typed as IRelayCommand for ergonomics)
        public IRelayCommand? OpenRequirementsCommand { get; set; }
        public IRelayCommand? OpenWorkspaceCommand { get; set; }
        public IRelayCommand? SaveCommand { get; set; }

        // File menu commands exposed as ICommand so they can accept AsyncRelayCommand / RelayCommand etc.
        public ICommand? ImportWordCommand { get; set; }
        public ICommand? LoadWorkspaceCommand { get; set; }
        public ICommand? SaveWorkspaceCommand { get; set; }
        public ICommand? ReloadCommand { get; set; }
        public ICommand? ExportAllToJamaCommand { get; set; }
        public ICommand? HelpCommand { get; set; }

        // Optional view-scoped actions
        public IRelayCommand? NewTestCaseCommand { get; set; }
        public IRelayCommand? RemoveTestCaseCommand { get; set; }

        public TestCaseCreatorHeaderViewModel() { }

        // Derived properties for the LLM indicator (bound from XAML).
        // These are computed and will update automatically via the partial change callbacks below.
        public string OllamaStatusMessage =>
            IsLlmBusy ? "LLM — busy"
            : IsLlmConnected ? "Ollama — connected"
            : "Ollama — disconnected";

        public Brush OllamaStatusColor =>
            IsLlmBusy ? Brushes.Yellow
            : IsLlmConnected ? Brushes.LimeGreen
            : Brushes.Gray;

        // Source-generator will call these partial methods when the corresponding observable property changes.
        // Use them to raise PropertyChanged for derived properties so the UI updates automatically.
        partial void OnIsLlmBusyChanged(bool oldValue, bool newValue)
        {
            OnPropertyChanged(nameof(OllamaStatusMessage));
            OnPropertyChanged(nameof(OllamaStatusColor));
        }

        partial void OnIsLlmConnectedChanged(bool oldValue, bool newValue)
        {
            OnPropertyChanged(nameof(OllamaStatusMessage));
            OnPropertyChanged(nameof(OllamaStatusColor));
        }
    }
}