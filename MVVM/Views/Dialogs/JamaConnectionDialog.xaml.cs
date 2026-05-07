using System.Windows;

namespace TestCaseEditorApp.MVVM.Views.Dialogs
{
    /// <summary>
    /// Dialog for capturing Jama Connect credentials
    /// </summary>
    public partial class JamaConnectionDialog : Window
    {
        public string BaseUrl => BaseUrlTextBox.Text?.Trim() ?? "";
        public string ApiToken => ApiTokenBox.Password?.Trim() ?? "";

        public JamaConnectionDialog()
        {
            InitializeComponent();
            
            // Focus on the base URL field
            Loaded += (s, e) => BaseUrlTextBox.Focus();
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(BaseUrl))
            {
                MessageBox.Show("Please enter a valid Jama base URL.", "Missing URL", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                BaseUrlTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(ApiToken))
            {
                MessageBox.Show("Please enter your API token.", "Missing Token", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ApiTokenBox.Focus();
                return;
            }

            // URL validation
            if (!BaseUrl.StartsWith("http://") && !BaseUrl.StartsWith("https://"))
            {
                MessageBox.Show("Base URL must start with http:// or https://", "Invalid URL", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                BaseUrlTextBox.Focus();
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}