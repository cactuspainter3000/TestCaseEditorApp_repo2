using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.OpenProject.Models
{
    public partial class RecentProject : ObservableObject
    {
        public string FilePath { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public DateTime LastOpened { get; set; }
        
        // Metadata properties
        public int RequirementsCount { get; set; } = 0;
        public int AnalyzedCount { get; set; } = 0;
        public int TestCasesGeneratedCount { get; set; } = 0;
        
        // Calculated properties
        public bool FileExists => System.IO.File.Exists(FilePath);
        public string DisplayName => !string.IsNullOrWhiteSpace(ProjectName) ? ProjectName : System.IO.Path.GetFileNameWithoutExtension(FilePath);
        public string RelativeTime => GetRelativeTime(LastOpened);
        
        public int AnalyzedPercentage => RequirementsCount > 0 ? (int)Math.Round((double)AnalyzedCount / RequirementsCount * 100) : 0;
        public int TestCasesPercentage => RequirementsCount > 0 ? (int)Math.Round((double)TestCasesGeneratedCount / RequirementsCount * 100) : 0;
        
        // UI State - needs to be set from ViewModel
        [ObservableProperty]
        private bool isActiveProject = false;
        
        private static string GetRelativeTime(DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;
            
            if (timeSpan.TotalDays > 7)
                return dateTime.ToString("MMM dd, yyyy");
            if (timeSpan.TotalDays >= 1)
                return $"{(int)timeSpan.TotalDays} day{(timeSpan.TotalDays >= 2 ? "s" : "")} ago";
            if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours} hour{(timeSpan.TotalHours >= 2 ? "s" : "")} ago";
            if (timeSpan.TotalMinutes >= 1)
                return $"{(int)timeSpan.TotalMinutes} minute{(timeSpan.TotalMinutes >= 2 ? "s" : "")} ago";
            
            return "Just now";
        }
    }
}