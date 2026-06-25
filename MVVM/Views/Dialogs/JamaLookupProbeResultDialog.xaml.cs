using System.Windows;

namespace TestCaseEditorApp.MVVM.Views.Dialogs
{
    public partial class JamaLookupProbeResultDialog : Window
    {
        public JamaLookupProbeResultDialog(string reportText)
        {
            InitializeComponent();
            ReportTextBox.Text = reportText ?? string.Empty;
            ReportTextBox.Focus();
            ReportTextBox.Select(0, 0);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(ReportTextBox.Text ?? string.Empty);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
