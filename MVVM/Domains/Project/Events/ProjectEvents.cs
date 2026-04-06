using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.Project.Events
{
    /// <summary>
    /// Project domain events following AI Guide patterns
    /// All domain-specific events for Project functionality
    /// </summary>
    public static class ProjectEvents
    {
        /// <summary>
        /// Event fired when a project is opened or loaded
        /// </summary>
        public class ProjectOpened
        {
            public string ProjectName { get; }
            public string ProjectPath { get; }
            public Workspace? Workspace { get; }
            
            public ProjectOpened(string projectName, string projectPath, Workspace? workspace = null)
            {
                ProjectName = projectName;
                ProjectPath = projectPath;
                Workspace = workspace;
            }
        }
        
        /// <summary>
        /// Event fired when a project is saved
        /// </summary>
        public class ProjectSaved
        {
            public string ProjectName { get; }
            public string ProjectPath { get; }
            public bool IsSuccessful { get; }
            
            public ProjectSaved(string projectName, string projectPath, bool isSuccessful = true)
            {
                ProjectName = projectName;
                ProjectPath = projectPath;
                IsSuccessful = isSuccessful;
            }
        }
        
        /// <summary>
        /// Event fired when a project is closed
        /// </summary>
        public class ProjectClosed
        {
            public string? ProjectName { get; }
            public bool HasUnsavedChanges { get; }
            
            public ProjectClosed(string? projectName = null, bool hasUnsavedChanges = false)
            {
                ProjectName = projectName;
                HasUnsavedChanges = hasUnsavedChanges;
            }
        }
        
        /// <summary>
        /// Event fired when project settings are updated
        /// </summary>
        public class ProjectSettingsChanged
        {
            public string SettingName { get; }
            public object? OldValue { get; }
            public object? NewValue { get; }
            
            public ProjectSettingsChanged(string settingName, object? oldValue, object? newValue)
            {
                SettingName = settingName;
                OldValue = oldValue;
                NewValue = newValue;
            }
        }
        
        /// <summary>
        /// Event fired when AnythingLLM connection status changes
        /// </summary>
        public class LLMConnectionStatusChanged
        {
            public bool IsConnected { get; }
            public string StatusMessage { get; }
            
            public LLMConnectionStatusChanged(bool isConnected, string statusMessage)
            {
                IsConnected = isConnected;
                StatusMessage = statusMessage;
            }
        }
    }
}