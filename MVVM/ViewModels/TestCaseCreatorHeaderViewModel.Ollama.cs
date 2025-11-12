using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    // Partial extension: Ollama/LLM indicator state for the header.
    public partial class TestCaseCreatorHeaderViewModel : ObservableObject
    {
        private bool _isLlmBusy;
        public bool IsLlmBusy
        {
            get => _isLlmBusy;
            set
            {
                if (SetProperty(ref _isLlmBusy, value))
                {
                    OnPropertyChanged(nameof(OllamaStatusMessage));
                    OnPropertyChanged(nameof(OllamaStatusColor));
                }
            }
        }

        private bool _isLlmConnected;
        public bool IsLlmConnected
        {
            get => _isLlmConnected;
            set
            {
                if (SetProperty(ref _isLlmConnected, value))
                {
                    OnPropertyChanged(nameof(OllamaStatusMessage));
                    OnPropertyChanged(nameof(OllamaStatusColor));
                }
            }
        }

        // Friendly text for the small status label
        public string OllamaStatusMessage =>
            IsLlmBusy ? "LLM — busy"
            : IsLlmConnected ? "Ollama — connected"
            : "Ollama — disconnected";

        // Fill brush for the Ellipse; must be a Brush because Fill expects it.
        public Brush OllamaStatusColor =>
            IsLlmBusy ? Brushes.Yellow
            : IsLlmConnected ? Brushes.LimeGreen
            : Brushes.Gray;
    }
}