using System.Windows;
using System.Windows.Controls;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class RequirementsTablesControl : UserControl
    {
        public RequirementsTablesControl()
        {
            InitializeComponent();
        }

        // DP to control whether the local toolbar is shown (default true so control is standalone-friendly)
        public static readonly DependencyProperty ShowLocalToolbarProperty =
            DependencyProperty.Register(
                nameof(ShowLocalToolbar),
                typeof(bool),
                typeof(RequirementsTablesControl),
                new PropertyMetadata(true));

        public bool ShowLocalToolbar
        {
            get => (bool)GetValue(ShowLocalToolbarProperty);
            set => SetValue(ShowLocalToolbarProperty, value);
        }
    }
}