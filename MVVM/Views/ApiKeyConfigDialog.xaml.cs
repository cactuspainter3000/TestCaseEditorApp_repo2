using System;
using System.Threading.Tasks;
using System.Windows;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class ApiKeyConfigDialog : Window
    {
        public string? ApiKey { get; private set; }
        public bool SaveLocally { get; private set; }

        public ApiKeyConfigDialog()
        {
            InitializeComponent();
            
            // Check if there's an existing API key
            var existingKey = AnythingLLMService.GetUserApiKey();
            if (!string.IsNullOrEmpty(existingKey))
            {
                ApiKeyTextBox.Text = existingKey;
            }
            
            ApiKeyTextBox.Focus();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = ApiKeyTextBox.Text.Trim();
            if (string.IsNullOrEmpty(apiKey))
            {
                ShowStatus("Please enter an API key first.", true);
                return;
            }

            try
            {
                TestConnectionButton.IsEnabled = false;
                TestConnectionButton.Content = "Testing...";
                ShowStatus("Testing API key...", false);

                // Test the API key
                var testService = new AnythingLLMService(apiKey: apiKey);
                var (success, message) = await testService.TestConnectivityAsync();

                if (success)
                {
                    ShowStatus("✅ API key is valid and working!", false);
                    SaveButton.IsEnabled = true;
                }
                else
                {
                    ShowStatus($"❌ API key test failed: {message}", true);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ Error testing API key: {ex.Message}", true);
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
                TestConnectionButton.Content = "Test Connection";
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = ApiKeyTextBox.Text.Trim();
            if (string.IsNullOrEmpty(apiKey))
            {
                ShowStatus("Please enter an API key.", true);
                return;
            }

            try
            {
                ApiKey = apiKey;
                SaveLocally = SaveLocallyCheckBox.IsChecked == true;

                if (SaveLocally)
                {
                    var saved = AnythingLLMService.SetUserApiKey(apiKey);
                    if (!saved)
                    {
                        ShowStatus("⚠️ Could not save API key locally, but will use for this session.", false);
                    }
                    else
                    {
                        ShowStatus("✅ API key saved successfully!", false);
                    }
                    
                    // Give user time to see the status message
                    await Task.Delay(1000);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ Error saving API key: {ex.Message}", true);
            }
        }

        private void ShowStatus(string message, bool isError)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                isError ? System.Windows.Media.Colors.IndianRed : System.Windows.Media.Colors.LightGreen);
            StatusTextBlock.Visibility = Visibility.Visible;
        }

        private void ApiKeyTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Hide status when user starts typing
            StatusTextBlock.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(ApiKeyTextBox.Text);
        }

        private void ApiKeyTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            // Event handler wired up in XAML
        }
    }
}