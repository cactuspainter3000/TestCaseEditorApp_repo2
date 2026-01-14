namespace TestCaseEditorApp.MVVM.Models.DataDrivenMenu;

/// <summary>
/// Represents a section header/divider in the menu.
/// Examples: 📚 Learning Workflow, 🎓 Advanced Training, ⚡ Quick Commands, 📤 Export Options
/// These use DropdownSectionHeader styling with orange accent theming.
/// </summary>
public class SectionHeader : MenuContentItem
{
    private string _accentColor = "#FF8C00"; // Orange accent from analysis
    
    /// <summary>
    /// Accent color for the section header styling.
    /// Default is orange (#FF8C00) to match DropdownSectionHeader theming.
    /// </summary>
    public string AccentColor
    {
        get => _accentColor;
        set => SetProperty(ref _accentColor, value);
    }

    /// <summary>
    /// Creates a new SectionHeader with the specified properties.
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="icon">Icon/emoji to display</param>
    /// <param name="text">Header text</param>
    /// <param name="accentColor">Accent color (optional, defaults to orange)</param>
    public static SectionHeader Create(string id, string icon, string text, string? accentColor = null)
    {
        return new SectionHeader
        {
            Id = id,
            Icon = icon,
            Text = text,
            AccentColor = accentColor ?? "#FF8C00", // Use default orange if null
            IsDropdown = false // SectionHeaders are never dropdowns
        };
    }
}