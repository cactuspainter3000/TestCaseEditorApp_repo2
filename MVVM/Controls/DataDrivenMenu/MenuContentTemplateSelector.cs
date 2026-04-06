using System.Windows;
using System.Windows.Controls;
using TestCaseEditorApp.MVVM.Models.DataDrivenMenu;

namespace TestCaseEditorApp.MVVM.Controls.DataDrivenMenu;

/// <summary>
/// Template selector that chooses the appropriate DataTemplate based on menu content type.
/// This enables the data-driven rendering system by mapping model types to templates.
/// </summary>
public class MenuContentTemplateSelector : DataTemplateSelector
{
    /// <summary>
    /// Template for MenuAction items (simple button actions).
    /// </summary>
    public DataTemplate? MenuActionTemplate { get; set; }

    /// <summary>
    /// Template for SectionHeader items (category dividers).
    /// </summary>
    public DataTemplate? SectionHeaderTemplate { get; set; }

    /// <summary>
    /// Template for ConditionalGroup items (groups with visibility conditions).
    /// </summary>
    public DataTemplate? ConditionalGroupTemplate { get; set; }

    /// <summary>
    /// Template for ComplexAction items (custom layouts).
    /// </summary>
    public DataTemplate? ComplexActionTemplate { get; set; }

    /// <summary>
    /// Selects the appropriate template based on the item's type.
    /// </summary>
    /// <param name="item">The data object for which to select a template</param>
    /// <param name="container">The data-bound object</param>
    /// <returns>The DataTemplate to use for rendering the item</returns>
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            MenuAction => MenuActionTemplate,
            SectionHeader => SectionHeaderTemplate,
            ConditionalGroup => ConditionalGroupTemplate,
            ComplexAction => ComplexActionTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }
}