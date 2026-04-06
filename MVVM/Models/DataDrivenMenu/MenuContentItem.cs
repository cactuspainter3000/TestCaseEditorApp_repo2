using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace TestCaseEditorApp.MVVM.Models.DataDrivenMenu;

/// <summary>
/// Base class for all menu content items in the data-driven menu system.
/// Provides common properties and ObservableObject functionality.
/// </summary>
public abstract class MenuContentItem : ObservableObject
{
    private string _id = string.Empty;
    private bool _isVisible = true;
    private bool _isDropdown = false;
    private int _level = 0;

    /// <summary>
    /// Whether this item should render as a dropdown with expandable content.
    /// When true, shows header with chevron and expandable children.
    /// When false, shows item directly without dropdown behavior.
    /// </summary>
    public bool IsDropdown
    {
        get => _isDropdown;
        set => SetProperty(ref _isDropdown, value);
    }
    private bool _isSelected = false;
    private string _text = string.Empty;
    private string _icon = string.Empty;
    private string _tooltip = string.Empty;

    /// <summary>
    /// Unique identifier for this menu item. Used for conditional visibility and styling.
    /// </summary>
    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    /// <summary>
    /// Whether this menu item should be visible in the UI.
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    /// <summary>
    /// Hierarchical level of this menu item. 0 = main level, 1 = sub-level, etc.
    /// Used for styling differences between main and sub-level items.
    /// </summary>
    public int Level
    {
        get => _level;
        set => SetProperty(ref _level, value);
    }

    /// <summary>
    /// Whether this menu item is currently selected.
    /// Used for selection highlighting with left accent stripe.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Display text for the menu item.
    /// </summary>
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    /// <summary>
    /// Icon/emoji to display with the menu item.
    /// </summary>
    public string Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    /// <summary>
    /// Tooltip text to show on hover.
    /// </summary>
    public string Tooltip
    {
        get => _tooltip;
        set => SetProperty(ref _tooltip, value);
    }

    /// <summary>
    /// Command to execute when this menu item is activated (for actionable items).
    /// </summary>
    public ICommand? Command { get; set; }

    /// <summary>
    /// Command parameter to pass to the Command (for actionable items).
    /// </summary>
    public object? CommandParameter { get; set; }
}