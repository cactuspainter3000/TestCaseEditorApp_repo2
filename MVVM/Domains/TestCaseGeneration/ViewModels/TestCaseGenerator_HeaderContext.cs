using System.Collections.Generic;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    // Simple bag of values & commands the header needs. MainViewModel populates this and passes it in.
    public class TestCaseGenerator_HeaderContext
    {
        public string? WorkspaceName { get; set; }
        public IEnumerable<Requirement>? Requirements { get; set; }

        public ICommand? ImportCommand { get; set; }
        public ICommand? LoadWorkspaceCommand { get; set; }
        public ICommand? SaveWorkspaceCommand { get; set; }
        public ICommand? ReloadCommand { get; set; }
        public ICommand? ExportAllToJamaCommand { get; set; }
        public ICommand? HelpCommand { get; set; }

        public IRelayCommand? OpenRequirementsCommand { get; set; }
        public IRelayCommand? OpenWorkspaceCommand { get; set; }
        public IRelayCommand? SaveCommand { get; set; }
    }
}