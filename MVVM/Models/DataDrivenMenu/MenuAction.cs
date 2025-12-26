using System.Collections.ObjectModel;
using System.Windows.Input;

namespace TestCaseEditorApp.MVVM.Models.DataDrivenMenu;

/// <summary>
/// Represents a simple menu action button (Pattern A from analysis).
/// Examples: 🆕 New Project, 📁 Open Project, ⚡ Quick Import, etc.
/// Can also be used as a dropdown with children when IsDropdown = true.
/// </summary>
public class MenuAction : MenuContentItem
{
    private bool _isExpanded = false;
    private ObservableCollection<MenuContentItem> _children = new();

    /// <summary>
    /// Whether this dropdown action is currently expanded.
    /// Only relevant when IsDropdown = true.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>
    /// Collection of child menu items for dropdown functionality.
    /// Only used when IsDropdown = true.
    /// </summary>
    public ObservableCollection<MenuContentItem> Children
    {
        get => _children;
        set => SetProperty(ref _children, value);
    }

    /// <summary>
    /// Adds a child item to this dropdown action.
    /// Only relevant when IsDropdown = true.
    /// </summary>
    public void AddChild(MenuContentItem child)
    {
        Children.Add(child);
    }
    /// <summary>
    /// Creates a new MenuAction with the specified properties.
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="icon">Icon/emoji to display</param>
    /// <param name="text">Display text</param>
    /// <param name="command">Command to execute when clicked</param>
    /// <param name="tooltip">Tooltip text (optional)</param>
    public static MenuAction Create(string id, string icon, string text, ICommand? command = null, string? tooltip = null)
    {
        return new MenuAction
        {
            Id = id,
            Icon = icon,
            Text = text,
            Command = command,
            Tooltip = tooltip ?? string.Empty,
            IsDropdown = false // Default to simple action
        };
    }

    /// <summary>
    /// Creates a new MenuAction with command parameter.
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="icon">Icon/emoji to display</param>
    /// <param name="text">Display text</param>
    /// <param name="command">Command to execute when clicked</param>
    /// <param name="commandParameter">Parameter to pass to command</param>
    /// <param name="tooltip">Tooltip text (optional)</param>
    public static MenuAction CreateWithParameter(string id, string icon, string text, ICommand? command, object? commandParameter, string? tooltip = null)
    {
        return new MenuAction
        {
            Id = id,
            Icon = icon,
            Text = text,
            Command = command,
            CommandParameter = commandParameter,
            Tooltip = tooltip ?? text
        };
    }
}