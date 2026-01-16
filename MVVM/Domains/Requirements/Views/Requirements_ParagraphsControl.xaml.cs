using System.Windows;
using System.Windows.Controls;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Views
{
    public partial class Requirements_ParagraphsControl : UserControl
    {
        public Requirements_ParagraphsControl()
        {
            InitializeComponent();
        }

        // DP to control whether the local toolbar is shown (default true so control is standalone-friendly)
        public static readonly DependencyProperty ShowLocalToolbarProperty =
            DependencyProperty.Register(
                nameof(ShowLocalToolbar),
                typeof(bool),
                typeof(Requirements_ParagraphsControl),
                new PropertyMetadata(true));

        public bool ShowLocalToolbar
        {
            get => (bool)GetValue(ShowLocalToolbarProperty);
            set => SetValue(ShowLocalToolbarProperty, value);
        }
    }
}