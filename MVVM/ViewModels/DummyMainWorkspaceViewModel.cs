using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Generic dummy MainWorkspace ViewModel - Reference implementation following AI Guide patterns
    /// Use this as a template for any domain's main content area
    /// </summary>
    public partial class DummyMainWorkspaceViewModel : ObservableObject
    {
        [ObservableProperty]
        private string sectionName = "Generic Dummy";
        
        [ObservableProperty]
        private string displayText = "🎯 Main Workspace - Working Perfectly!";
        
        [ObservableProperty]
        private string statusMessage = "This is the main content area. All workspace coordination is functioning.";
        
        [ObservableProperty]
        private DateTime lastUpdated = DateTime.Now;
        
        public DummyMainWorkspaceViewModel()
        {
            // Simple constructor with no dependencies - follows AI Guide fail-fast principles
        }
        
        partial void OnSectionNameChanged(string value)
        {
            DisplayText = $"🎯 {value} Main Workspace - Working Perfectly!";
            StatusMessage = $"This is the main content area for {value}. All workspace coordination is functioning.";
            LastUpdated = DateTime.Now;
        }
    }
}