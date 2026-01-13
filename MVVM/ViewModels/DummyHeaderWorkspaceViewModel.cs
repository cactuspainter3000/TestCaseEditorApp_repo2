using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Generic dummy HeaderWorkspace ViewModel - Reference implementation following AI Guide patterns
    /// Use this as a template for any domain's header area
    /// </summary>
    public partial class DummyHeaderWorkspaceViewModel : ObservableObject
    {
        [ObservableProperty]
        private string sectionName = "Generic Dummy";
        
        [ObservableProperty]
        private string title = "📋 Context Header";
        
        [ObservableProperty]
        private string subtitle = "Context-specific header workspace";
        
        [ObservableProperty]
        private DateTime lastUpdated = DateTime.Now;
        
        public DummyHeaderWorkspaceViewModel()
        {
            // Simple constructor - no dependencies for maximum reliability
        }
        
        partial void OnSectionNameChanged(string value)
        {
            Title = $"📋 {value} Header";
            Subtitle = $"Context-specific header for {value} workflow";
            LastUpdated = DateTime.Now;
        }
    }
}