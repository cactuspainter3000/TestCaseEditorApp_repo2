using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.ViewModels;

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

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Only toggle if the click is directly on the border or its immediate children,
            // not on buttons or other interactive elements
            if (e.OriginalSource is Button)
            {
                // Don't handle button clicks - let them execute their commands
                return;
            }
            
            // Toggle the IsSelected property of the table when border is clicked
            if (sender is FrameworkElement element && element.DataContext is LooseTableViewModel tableVM)
            {
                tableVM.IsSelected = !tableVM.IsSelected;
                e.Handled = true; // Prevent the click from bubbling up
            }
        }
    }
}