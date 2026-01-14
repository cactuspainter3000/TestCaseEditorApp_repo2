using System.Collections.ObjectModel;

namespace TestCaseEditorApp.MVVM.Models.DataDrivenMenu;

/// <summary>
/// Top-level container for a menu section with header and collection of menu items.
/// Represents the entire Test Case Generator dropdown or any similar dropdown menu.
/// This replaces the hardcoded XAML structure with a clean data-driven approach.
/// </summary>
public class MenuSection : MenuContentItem
{
    private bool _isExpanded = false;
    private ObservableCollection<MenuContentItem> _items = new();
    private string _headerText = string.Empty;
    private string _headerIcon = string.Empty;

    /// <summary>
    /// Whether the menu section is currently expanded.
    /// Controls the dropdown visibility and chevron rotation.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>
    /// Collection of all menu items in this section.
    /// Can contain MenuActions, SectionHeaders, ConditionalGroups, ComplexActions, etc.
    /// </summary>
    public ObservableCollection<MenuContentItem> Items
    {
        get => _items;
        set => SetProperty(ref _items, value);
    }

    /// <summary>
    /// Text to display in the section header.
    /// Example: "Test Case Generator"
    /// </summary>
    public string HeaderText
    {
        get => _headerText;
        set => SetProperty(ref _headerText, value);
    }

    /// <summary>
    /// Icon/emoji to display in the section header.
    /// Example: "🧪" for Test Case Generator
    /// </summary>
    public string HeaderIcon
    {
        get => _headerIcon;
        set => SetProperty(ref _headerIcon, value);
    }

    /// <summary>
    /// Creates a new MenuSection with the specified properties.
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="headerIcon">Icon for the section header</param>
    /// <param name="headerText">Text for the section header</param>
    /// <param name="items">Collection of menu items (optional)</param>
    public static MenuSection Create(string id, string headerIcon, string headerText, IEnumerable<MenuContentItem>? items = null)
    {
        var section = new MenuSection
        {
            Id = id,
            HeaderIcon = headerIcon,
            HeaderText = headerText
        };

        if (items != null)
        {
            foreach (var item in items)
            {
                section.Items.Add(item);
            }
        }

        return section;
    }

    /// <summary>
    /// Adds a menu item to this section.
    /// </summary>
    /// <param name="item">Menu item to add</param>
    public void AddItem(MenuContentItem item)
    {
        Items.Add(item);
    }

    /// <summary>
    /// Adds multiple menu items to this section.
    /// </summary>
    /// <param name="items">Menu items to add</param>
    public void AddItems(IEnumerable<MenuContentItem> items)
    {
        foreach (var item in items)
        {
            Items.Add(item);
        }
    }

    /// <summary>
    /// Toggles the expanded state of this menu section.
    /// Used by the UI for expand/collapse behavior.
    /// </summary>
    public void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }
}