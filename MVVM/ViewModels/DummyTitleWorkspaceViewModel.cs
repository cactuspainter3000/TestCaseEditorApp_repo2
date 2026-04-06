using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Generic dummy TitleWorkspace ViewModel - Reference implementation following AI Guide patterns
    /// Use this as a template for any domain's title area
    /// </summary>
    public partial class DummyTitleWorkspaceViewModel : ObservableObject
    {
        [ObservableProperty]
        private string sectionName = "Generic Dummy";
        
        [ObservableProperty]
        private string pageTitle = "✨ Generic Section";
        
        [ObservableProperty]
        private string breadcrumb = "Home > Section";
        
        [ObservableProperty]
        private DateTime lastUpdated = DateTime.Now;
        
        public DummyTitleWorkspaceViewModel()
        {
            // Simple constructor - AI Guide compliant
        }
        
        partial void OnSectionNameChanged(string value)
        {
            PageTitle = $"✨ {value}";
            Breadcrumb = $"Home > {value}";
            LastUpdated = DateTime.Now;
        }
    }
}