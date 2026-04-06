using System;

namespace TestCaseEditorApp.MVVM.Domains.Dummy.Events
{
    /// <summary>
    /// Dummy domain events for type-safe communication within the domain.
    /// Use this as a template for any domain's event definitions
    /// </summary>
    public class DummyEvents
    {
        public class DummyDataUpdated
        {
            public string PropertyName { get; set; } = string.Empty;
            public object? NewValue { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        public class DummyWorkspaceChanged
        {
            public string WorkspaceName { get; set; } = string.Empty;
            public string NewContent { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        public class DummyStatusChanged
        {
            public string Status { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        public class ButtonClicked
        {
            public string WorkspaceName { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
            
            public ButtonClicked(string workspaceName)
            {
                WorkspaceName = workspaceName;
            }
        }
    }
}