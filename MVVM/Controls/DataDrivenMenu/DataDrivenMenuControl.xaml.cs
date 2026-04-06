using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using TestCaseEditorApp.MVVM.Models.DataDrivenMenu;

namespace TestCaseEditorApp.MVVM.Controls.DataDrivenMenu;

/// <summary>
/// Data-driven menu control that renders menu content using templates.
/// This replaces hardcoded XAML with a clean template-based approach.
/// Supports both direct MenuItems collection and MenuSection with dropdown behavior.
/// </summary>
public partial class DataDrivenMenuControl : UserControl
{
    /// <summary>
    /// Dependency property for the collection of menu items to display.
    /// Used when displaying items directly without a dropdown header.
    /// </summary>
    public static readonly DependencyProperty MenuItemsProperty =
        DependencyProperty.Register(
            nameof(MenuItems),
            typeof(ObservableCollection<MenuContentItem>),
            typeof(DataDrivenMenuControl),
            new PropertyMetadata(null));

    /// <summary>
    /// Dependency property for a MenuSection with dropdown header and animation.
    /// Used when displaying a collapsible section like the Test Case Generator dropdown.
    /// </summary>
    public static readonly DependencyProperty MenuSectionProperty =
        DependencyProperty.Register(
            nameof(MenuSection),
            typeof(MenuSection),
            typeof(DataDrivenMenuControl),
            new PropertyMetadata(null));

    /// <summary>
    /// Collection of menu items to display in this control.
    /// Each item will be rendered using the appropriate template based on its type.
    /// Used for direct display without dropdown behavior.
    /// </summary>
    public ObservableCollection<MenuContentItem>? MenuItems
    {
        get => (ObservableCollection<MenuContentItem>?)GetValue(MenuItemsProperty);
        set => SetValue(MenuItemsProperty, value);
    }

    /// <summary>
    /// MenuSection with dropdown header and animation behavior.
    /// Used for collapsible sections like the Test Case Generator dropdown.
    /// When set, this takes precedence over MenuItems property.
    /// </summary>
    public MenuSection? MenuSection
    {
        get => (MenuSection?)GetValue(MenuSectionProperty);
        set => SetValue(MenuSectionProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the DataDrivenMenuControl.
    /// </summary>
    public DataDrivenMenuControl()
    {
        InitializeComponent();
    }
}