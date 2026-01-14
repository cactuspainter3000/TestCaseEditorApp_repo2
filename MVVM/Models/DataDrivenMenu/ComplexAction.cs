using System.Windows.Input;

namespace TestCaseEditorApp.MVVM.Models.DataDrivenMenu;

/// <summary>
/// Layout types for complex menu actions.
/// </summary>
public enum ActionLayout
{
    /// <summary>
    /// Simple horizontal layout (icon + text)
    /// </summary>
    Horizontal,
    
    /// <summary>
    /// DockPanel layout with separated icon and text
    /// Example: 📝 + "Open Export" (DockPanel layout from analysis)
    /// </summary>
    DockPanel,
    
    /// <summary>
    /// Text with trailing indicator
    /// Example: "Export for ChatGPT" + ✔ (conditional checkmark)
    /// </summary>
    TextWithTrailingIndicator
}

/// <summary>
/// Represents a menu action with custom layout and styling (Pattern B from analysis).
/// Examples: 📝 + "Open Export" (DockPanel), "Export for ChatGPT" + ✔ (trailing indicator)
/// </summary>
public class ComplexAction : MenuContentItem
{
    private ActionLayout _layout = ActionLayout.Horizontal;
    private string _trailingIndicator = string.Empty;
    private bool _showTrailingIndicator = false;
    private string _iconSeparation = "4"; // Default spacing

    /// <summary>
    /// Layout type for this complex action.
    /// </summary>
    public ActionLayout Layout
    {
        get => _layout;
        set => SetProperty(ref _layout, value);
    }

    /// <summary>
    /// Trailing indicator text/symbol (e.g., ✔ for "Export for ChatGPT").
    /// </summary>
    public string TrailingIndicator
    {
        get => _trailingIndicator;
        set => SetProperty(ref _trailingIndicator, value);
    }

    /// <summary>
    /// Whether to show the trailing indicator.
    /// This can be bound to conditional state (e.g., export status).
    /// </summary>
    public bool ShowTrailingIndicator
    {
        get => _showTrailingIndicator;
        set => SetProperty(ref _showTrailingIndicator, value);
    }

    /// <summary>
    /// Spacing between icon and text (in pixels).
    /// Default is 4px based on analysis.
    /// </summary>
    public string IconSeparation
    {
        get => _iconSeparation;
        set => SetProperty(ref _iconSeparation, value);
    }

    /// <summary>
    /// Creates a DockPanel layout complex action.
    /// Example: 📝 + "Open Export"
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="icon">Icon/emoji to display</param>
    /// <param name="text">Display text</param>
    /// <param name="command">Command to execute when clicked</param>
    /// <param name="iconSeparation">Spacing between icon and text</param>
    /// <param name="tooltip">Tooltip text (optional)</param>
    public static ComplexAction CreateDockPanel(string id, string icon, string text, ICommand? command = null, string iconSeparation = "4", string? tooltip = null)
    {
        return new ComplexAction
        {
            Id = id,
            Icon = icon,
            Text = text,
            Command = command,
            Layout = ActionLayout.DockPanel,
            IconSeparation = iconSeparation,
            Tooltip = tooltip ?? text
        };
    }

    /// <summary>
    /// Creates a text with trailing indicator complex action.
    /// Example: "Export for ChatGPT" + ✔
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="text">Display text</param>
    /// <param name="trailingIndicator">Trailing indicator symbol</param>
    /// <param name="command">Command to execute when clicked</param>
    /// <param name="showIndicator">Whether to show the indicator initially</param>
    /// <param name="tooltip">Tooltip text (optional)</param>
    public static ComplexAction CreateWithTrailingIndicator(string id, string text, string trailingIndicator, ICommand? command = null, bool showIndicator = false, string? tooltip = null)
    {
        return new ComplexAction
        {
            Id = id,
            Text = text,
            TrailingIndicator = trailingIndicator,
            Command = command,
            Layout = ActionLayout.TextWithTrailingIndicator,
            ShowTrailingIndicator = showIndicator,
            Tooltip = tooltip ?? text
        };
    }
}