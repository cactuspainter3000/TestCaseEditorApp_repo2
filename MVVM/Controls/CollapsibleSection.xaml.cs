using System.Windows;
using System.Windows.Controls;

namespace TestCaseEditorApp.MVVM.Controls
{
    /// <summary>
    /// Reusable collapsible section control that combines header + animated content.
    /// Unifies the repetitive pattern of ToggleButton header with AnimatedChevron + animated content area.
    /// </summary>
    public partial class CollapsibleSection : UserControl
    {
        public CollapsibleSection()
        {
            InitializeComponent();
        }

        #region Dependency Properties

        /// <summary>
        /// Gets or sets the header text to display.
        /// </summary>
        public static readonly DependencyProperty HeaderTextProperty =
            DependencyProperty.Register("HeaderText", typeof(string), typeof(CollapsibleSection), 
                new PropertyMetadata("Section Header"));

        public string HeaderText
        {
            get => (string)GetValue(HeaderTextProperty);
            set => SetValue(HeaderTextProperty, value);
        }

        /// <summary>
        /// Gets or sets whether the section is expanded.
        /// </summary>
        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register("IsExpanded", typeof(bool), typeof(CollapsibleSection), 
                new PropertyMetadata(false));

        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        /// <summary>
        /// Gets or sets the content to display when expanded.
        /// </summary>
        public static readonly DependencyProperty SectionContentProperty =
            DependencyProperty.Register("SectionContent", typeof(object), typeof(CollapsibleSection), 
                new PropertyMetadata(null));

        public object SectionContent
        {
            get => GetValue(SectionContentProperty);
            set => SetValue(SectionContentProperty, value);
        }

        /// <summary>
        /// Gets or sets the header font size (default: 16).
        /// </summary>
        public static readonly DependencyProperty HeaderFontSizeProperty =
            DependencyProperty.Register("HeaderFontSize", typeof(double), typeof(CollapsibleSection), 
                new PropertyMetadata(16.0));

        public double HeaderFontSize
        {
            get => (double)GetValue(HeaderFontSizeProperty);
            set => SetValue(HeaderFontSizeProperty, value);
        }

        /// <summary>
        /// Gets or sets the header font weight (default: Normal).
        /// </summary>
        public static readonly DependencyProperty HeaderFontWeightProperty =
            DependencyProperty.Register("HeaderFontWeight", typeof(FontWeight), typeof(CollapsibleSection), 
                new PropertyMetadata(FontWeights.Normal));

        public FontWeight HeaderFontWeight
        {
            get => (FontWeight)GetValue(HeaderFontWeightProperty);
            set => SetValue(HeaderFontWeightProperty, value);
        }

        /// <summary>
        /// Gets or sets the chevron size (default: 14).
        /// </summary>
        public static readonly DependencyProperty ChevronSizeProperty =
            DependencyProperty.Register("ChevronSize", typeof(double), typeof(CollapsibleSection), 
                new PropertyMetadata(14.0));

        public double ChevronSize
        {
            get => (double)GetValue(ChevronSizeProperty);
            set => SetValue(ChevronSizeProperty, value);
        }

        /// <summary>
        /// Gets or sets the chevron margin (default: 8,0,4,0).
        /// </summary>
        public static readonly DependencyProperty ChevronMarginProperty =
            DependencyProperty.Register("ChevronMargin", typeof(Thickness), typeof(CollapsibleSection), 
                new PropertyMetadata(new Thickness(8, 0, 4, 0)));

        public Thickness ChevronMargin
        {
            get => (Thickness)GetValue(ChevronMarginProperty);
            set => SetValue(ChevronMarginProperty, value);
        }

        /// <summary>
        /// Gets or sets the header margin (default: 0,6,0,0).
        /// </summary>
        public static readonly DependencyProperty HeaderMarginProperty =
            DependencyProperty.Register("HeaderMargin", typeof(Thickness), typeof(CollapsibleSection), 
                new PropertyMetadata(new Thickness(0, 6, 0, 0)));

        public Thickness HeaderMargin
        {
            get => (Thickness)GetValue(HeaderMarginProperty);
            set => SetValue(HeaderMarginProperty, value);
        }

        /// <summary>
        /// Gets or sets the content margin (default: 0,4,0,0).
        /// </summary>
        public static readonly DependencyProperty ContentMarginProperty =
            DependencyProperty.Register("ContentMargin", typeof(Thickness), typeof(CollapsibleSection), 
                new PropertyMetadata(new Thickness(0, 4, 0, 0)));

        public Thickness ContentMargin
        {
            get => (Thickness)GetValue(ContentMarginProperty);
            set => SetValue(ContentMarginProperty, value);
        }

        #endregion
    }
}