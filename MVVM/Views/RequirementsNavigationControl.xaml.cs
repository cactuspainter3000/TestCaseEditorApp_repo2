using System.Windows;
using System.Windows.Controls;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class RequirementsNavigationControl : UserControl
    {
        public RequirementsNavigationControl()
        {
            InitializeComponent();

            // Attach Loaded handler in code to ensure the right signature is bound.
            this.Loaded += RequirementsNavigationControl_Loaded;
        }

        // Correct signature for Loaded event handler
        private void RequirementsNavigationControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Put your initialization code here.
        }
    }
}