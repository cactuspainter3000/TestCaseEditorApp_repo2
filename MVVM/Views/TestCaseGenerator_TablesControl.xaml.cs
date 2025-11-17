using System.Windows;
using System.Windows.Controls;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class TestCaseGenerator_TablesControl : UserControl
    {
        public TestCaseGenerator_TablesControl()
        {
            InitializeComponent();
        }

        // DP to control whether the local toolbar is shown (default true so control is standalone-friendly)
        public static readonly DependencyProperty ShowLocalToolbarProperty =
            DependencyProperty.Register(
                nameof(ShowLocalToolbar),
                typeof(bool),
                typeof(TestCaseGenerator_TablesControl),
                new PropertyMetadata(true));

        public bool ShowLocalToolbar
        {
            get => (bool)GetValue(ShowLocalToolbarProperty);
            set => SetValue(ShowLocalToolbarProperty, value);
        }
    }
}