using System.Collections.ObjectModel;

namespace TestCaseEditorApp.MVVM.Models.DataDrivenMenu;

/// <summary>
/// Represents a group of menu items that show/hide based on step selection.
/// Examples: project → New/Open/Save/Close buttons, requirements → Import/Analyze buttons
/// This replaces the conditional StackPanel visibility logic from the original implementation.
/// </summary>
public class ConditionalGroup : MenuContentItem
{
    private string _visibilityCondition = string.Empty;
    private ObservableCollection<MenuContentItem> _children = new();

    /// <summary>
    /// Condition that determines when this group should be visible.
    /// Examples: "project", "requirements", "llm-learning", "testcase-creation"
    /// This replaces the {Binding Id} conditional visibility from original XAML.
    /// </summary>
    public string VisibilityCondition
    {
        get => _visibilityCondition;
        set => SetProperty(ref _visibilityCondition, value);
    }

    /// <summary>
    /// Collection of menu items within this conditional group.
    /// </summary>
    public ObservableCollection<MenuContentItem> Children
    {
        get => _children;
        set => SetProperty(ref _children, value);
    }

    /// <summary>
    /// Creates a new ConditionalGroup with the specified properties.
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="visibilityCondition">Condition for when group is visible</param>
    /// <param name="children">Collection of child menu items (optional)</param>
    public static ConditionalGroup Create(string id, string visibilityCondition, IEnumerable<MenuContentItem>? children = null)
    {
        var group = new ConditionalGroup
        {
            Id = id,
            VisibilityCondition = visibilityCondition,
            IsDropdown = true // ConditionalGroups are always dropdowns
        };

        if (children != null)
        {
            foreach (var child in children)
            {
                group.Children.Add(child);
            }
        }

        return group;
    }

    /// <summary>
    /// Adds a child menu item to this group.
    /// </summary>
    /// <param name="child">Menu item to add</param>
    public void AddChild(MenuContentItem child)
    {
        Children.Add(child);
    }

    /// <summary>
    /// Adds multiple child menu items to this group.
    /// </summary>
    /// <param name="children">Menu items to add</param>
    public void AddChildren(IEnumerable<MenuContentItem> children)
    {
        foreach (var child in children)
        {
            Children.Add(child);
        }
    }
}