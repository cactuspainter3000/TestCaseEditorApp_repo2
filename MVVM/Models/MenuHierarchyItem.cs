using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Data model for hierarchical menu items that automatically applies styling based on level
    /// </summary>
    public partial class MenuHierarchyItem : ObservableObject
    {
        [ObservableProperty]
        private string title = string.Empty;
        
        [ObservableProperty]
        private string icon = string.Empty;
        
        [ObservableProperty]
        private int level = 1; // 1 = Primary header, 2 = Secondary header, 3 = Tertiary header, 4 = Action item
        
        [ObservableProperty]
        private bool isExpanded = false;
        
        [ObservableProperty]
        private bool isExpandable = false;
        
        [ObservableProperty]
        private ICommand? command;
        
        [ObservableProperty]
        private ObservableCollection<MenuHierarchyItem> children = new();
        
        [ObservableProperty]
        private object? tag; // For additional data
        
        // === STATE MANAGEMENT PROPERTIES ===
        
        [ObservableProperty]
        private bool isEnabled = true; // Manual enable/disable override
        
        [ObservableProperty]
        private bool isVisible = true; // Show/hide menu items
        
        [ObservableProperty]
        private string badge = string.Empty; // For status badges (counts, alerts, etc.)
        
        [ObservableProperty]
        private string statusIcon = string.Empty; // For status indicators (✓, ⚠️, etc.)
        
        /// <summary>
        /// Computed property: Item is actually enabled if both IsEnabled=true AND Command.CanExecute=true
        /// </summary>
        public bool IsActuallyEnabled => IsEnabled && (Command?.CanExecute(null) ?? true);
        
        /// <summary>
        /// Convenience property for styling - gets the appropriate text style key based on level
        /// </summary>
        public string TextStyleKey => Level switch
        {
            1 => "Text.Header.Primary",
            2 => "Text.Header.Secondary", 
            3 => "Text.Header.Tertiary",
            _ => "Text.Body"
        };
        
        /// <summary>
        /// Convenience property for styling - gets the appropriate button style key based on level
        /// </summary>
        public string ButtonStyleKey => Level switch
        {
            1 => "ToggleButton.Primary",
            2 or 3 => "Button.Primary",
            _ => "Button.Secondary"
        };
        
        /// <summary>
        /// Automatically calculates margin based on level for indentation
        /// </summary>
        public string MarginValue => Level switch
        {
            1 => "0,10,0,0",
            2 => "20,10,0,0", 
            3 => "40,10,0,0",
            _ => "60,5,0,0"
        };
        
        /// <summary>
        /// Helper to create action items (level 4) with state
        /// </summary>
        public static MenuHierarchyItem CreateAction(string title, string icon, ICommand? command, bool isEnabled = true)
        {
            return new MenuHierarchyItem
            {
                Title = title,
                Icon = icon,
                Level = 4,
                Command = command,
                IsExpandable = false,
                IsEnabled = isEnabled
            };
        }
        
        /// <summary>
        /// Helper to create section headers with children and state
        /// </summary>
        public static MenuHierarchyItem CreateSection(string title, int level, bool isEnabled = true, params MenuHierarchyItem[] children)
        {
            var item = new MenuHierarchyItem
            {
                Title = title,
                Level = level,
                IsExpandable = children.Length > 0,
                IsEnabled = isEnabled
            };
            
            foreach (var child in children)
            {
                item.Children.Add(child);
            }
            
            return item;
        }
        
        /// <summary>
        /// Update state from mediator events
        /// </summary>
        public void UpdateState(bool? enabled = null, bool? visible = null, string? badge = null, string? statusIcon = null)
        {
            if (enabled.HasValue) IsEnabled = enabled.Value;
            if (visible.HasValue) IsVisible = visible.Value;
            if (badge != null) Badge = badge;
            if (statusIcon != null) StatusIcon = statusIcon;
        }
    }
}