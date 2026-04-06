using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Generic dummy NotificationWorkspace ViewModel - Reference implementation following AI Guide patterns
    /// Use this as a template for any domain's notification area
    /// </summary>
    public partial class DummyNotificationWorkspaceViewModel : ObservableObject
    {
        [ObservableProperty]
        private string sectionName = "Generic Dummy";
        
        [ObservableProperty]
        private string notificationTitle = "🔔 Notifications";
        
        [ObservableProperty]
        private string statusMessage = "All systems operational";
        
        [ObservableProperty]
        private DateTime lastUpdated = DateTime.Now;
        
        public DummyNotificationWorkspaceViewModel()
        {
            // Simple constructor - AI Guide compliant
        }
        
        partial void OnSectionNameChanged(string value)
        {
            NotificationTitle = $"🔔 {value} Notifications";
            StatusMessage = $"All {value} systems operational";
            LastUpdated = DateTime.Now;
        }
    }
}