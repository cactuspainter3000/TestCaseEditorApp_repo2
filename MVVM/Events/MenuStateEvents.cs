using System;
using System.Collections.Generic;

namespace TestCaseEditorApp.MVVM.Events
{
    /// <summary>
    /// Events for menu state management across the application
    /// </summary>
    public static class MenuStateEvents
    {
        /// <summary>
        /// Update the state of specific menu items
        /// </summary>
        public class UpdateMenuItemState
        {
            public required string MenuItemId { get; set; }
            public bool? IsEnabled { get; set; }
            public bool? IsVisible { get; set; }
            public string? Badge { get; set; }
            public string? StatusIcon { get; set; }
        }
        
        /// <summary>
        /// Batch update multiple menu items at once
        /// </summary>
        public class BatchUpdateMenuState
        {
            public required Dictionary<string, MenuItemStateUpdate> Updates { get; set; }
        }
        
        /// <summary>
        /// Request current state of a menu item (for when ViewModel needs to sync)
        /// </summary>
        public class RequestMenuItemState
        {
            public required string MenuItemId { get; set; }
        }
        
        /// <summary>
        /// Global state changes that affect multiple menus
        /// </summary>
        public class GlobalStateChanged
        {
            public bool IsProjectLoaded { get; set; }
            public bool IsAnalyzing { get; set; }
            public bool HasRequirements { get; set; }
            public int UnanalyzedCount { get; set; }
        }
    }
    
    public class MenuItemStateUpdate
    {
        public bool? IsEnabled { get; set; }
        public bool? IsVisible { get; set; }
        public string? Badge { get; set; }
        public string? StatusIcon { get; set; }
    }
}