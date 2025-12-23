using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TestCaseEditorApp.MVVM.Controls
{
    /// <summary>
    /// Reusable animated chevron control for expandable menu items.
    /// Provides smooth rotation animation when expansion state changes.
    /// </summary>
    public partial class AnimatedChevron : UserControl
    {
        public AnimatedChevron()
        {
            InitializeComponent();
        }

        #region Dependency Properties

        /// <summary>
        /// Gets or sets whether the chevron should be in expanded state (rotated).
        /// </summary>
        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register("IsExpanded", typeof(bool), typeof(AnimatedChevron), 
                new PropertyMetadata(false));

        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        /// <summary>
        /// Gets or sets the chevron symbol to display (default: "▶").
        /// </summary>
        public static readonly DependencyProperty ChevronSymbolProperty =
            DependencyProperty.Register("ChevronSymbol", typeof(string), typeof(AnimatedChevron), 
                new PropertyMetadata("▶"));

        public string ChevronSymbol
        {
            get => (string)GetValue(ChevronSymbolProperty);
            set => SetValue(ChevronSymbolProperty, value);
        }

        /// <summary>
        /// Gets or sets the chevron font size (default: 12).
        /// </summary>
        public static readonly DependencyProperty ChevronSizeProperty =
            DependencyProperty.Register("ChevronSize", typeof(double), typeof(AnimatedChevron), 
                new PropertyMetadata(12.0));

        public double ChevronSize
        {
            get => (double)GetValue(ChevronSizeProperty);
            set => SetValue(ChevronSizeProperty, value);
        }

        /// <summary>
        /// Gets or sets the chevron margin (default: 8,0,0,0).
        /// </summary>
        public static readonly DependencyProperty ChevronMarginProperty =
            DependencyProperty.Register("ChevronMargin", typeof(Thickness), typeof(AnimatedChevron), 
                new PropertyMetadata(new Thickness(8, 0, 0, 0)));

        public Thickness ChevronMargin
        {
            get => (Thickness)GetValue(ChevronMarginProperty);
            set => SetValue(ChevronMarginProperty, value);
        }

        /// <summary>
        /// Gets or sets the chevron color brush.
        /// </summary>
        public static readonly DependencyProperty ChevronColorProperty =
            DependencyProperty.Register("ChevronColor", typeof(Brush), typeof(AnimatedChevron), 
                new PropertyMetadata(Brushes.White));

        public Brush ChevronColor
        {
            get => (Brush)GetValue(ChevronColorProperty);
            set => SetValue(ChevronColorProperty, value);
        }

        #endregion
    }
}