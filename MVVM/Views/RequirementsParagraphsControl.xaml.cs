using System.Windows;
using System.Windows.Controls;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class RequirementsParagraphsControl : UserControl
    {
        public RequirementsParagraphsControl()
        {
            InitializeComponent();
        }

        // DP to control whether the local toolbar is shown (default true so control is standalone-friendly)
        public static readonly DependencyProperty ShowLocalToolbarProperty =
            DependencyProperty.Register(
                nameof(ShowLocalToolbar),
                typeof(bool),
                typeof(RequirementsParagraphsControl),
                new PropertyMetadata(true));

        public bool ShowLocalToolbar
        {
            get => (bool)GetValue(ShowLocalToolbarProperty);
            set => SetValue(ShowLocalToolbarProperty, value);
        }
    }
}