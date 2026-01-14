using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Generic dummy NavigationWorkspace ViewModel - Reference implementation following AI Guide patterns
    /// Use this as a template for any domain's navigation area
    /// </summary>
    public partial class DummyNavigationWorkspaceViewModel : ObservableObject
    {
        [ObservableProperty]
        private string sectionName = "Generic Dummy";
        
        [ObservableProperty]
        private string navigationTitle = "🧭 Navigation";
        
        [ObservableProperty]
        private string currentStep = "Active Section";
        
        [ObservableProperty]
        private DateTime lastUpdated = DateTime.Now;
        
        public DummyNavigationWorkspaceViewModel()
        {
            // Simple constructor following AI Guide patterns
        }
        
        partial void OnSectionNameChanged(string value)
        {
            NavigationTitle = $"🧭 {value} Navigation";
            CurrentStep = $"Active: {value} workflow";
            LastUpdated = DateTime.Now;
        }
    }
}