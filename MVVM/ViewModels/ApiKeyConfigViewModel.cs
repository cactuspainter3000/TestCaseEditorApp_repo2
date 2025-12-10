using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for API key configuration modal
    /// </summary>
    public partial class ApiKeyConfigViewModel : ObservableObject
    {
        private readonly NotificationService _notificationService;

        [ObservableProperty]
        private string _apiKey = string.Empty;

        [ObservableProperty]
        private bool _saveLocally = true;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isStatusError = false;

        [ObservableProperty]
        private bool _isTesting = false;

        [ObservableProperty]
        private bool _isTestSuccessful = false;

        [ObservableProperty]
        private bool _canSave = false;

        /// <summary>
        /// Event raised when API key is successfully configured
        /// </summary>
        public event EventHandler<ApiKeyConfiguredEventArgs>? ApiKeyConfigured;

        /// <summary>
        /// Event raised when user cancels configuration
        /// </summary>
        public event EventHandler? Cancelled;

        public ICommand TestConnectionCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public ApiKeyConfigViewModel(NotificationService notificationService)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            // Initialize commands
            TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !string.IsNullOrWhiteSpace(ApiKey) && !IsTesting);
            SaveCommand = new AsyncRelayCommand(SaveAsync, () => !string.IsNullOrWhiteSpace(ApiKey) && !IsTesting && IsTestSuccessful);
            CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));

            // Load existing API key if available
            LoadExistingApiKey();
        }

        private void LoadExistingApiKey()
        {
            try
            {
                var existingKey = AnythingLLMService.GetUserApiKey();
                if (!string.IsNullOrEmpty(existingKey))
                {
                    ApiKey = existingKey;
                    StatusMessage = "Found existing API key";
                    IsStatusError = false;
                }
                else
                {
                    StatusMessage = "No existing API key found";
                    IsStatusError = false;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading existing API key: {ex.Message}";
                IsStatusError = true;
            }
        }

        private async Task TestConnectionAsync()
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
                return;

            try
            {
                IsTesting = true;
                IsTestSuccessful = false;
                CanSave = false;
                StatusMessage = "Testing API key...";
                IsStatusError = false;

                // Test the API key
                var testService = new AnythingLLMService(apiKey: ApiKey.Trim());
                var (success, message) = await testService.TestConnectivityAsync();

                if (success)
                {
                    StatusMessage = "✅ API key is valid and working!";
                    IsStatusError = false;
                    IsTestSuccessful = true;
                    CanSave = true;
                }
                else
                {
                    StatusMessage = $"❌ API key test failed: {message}";
                    IsStatusError = true;
                    IsTestSuccessful = false;
                    CanSave = false;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Error testing API key: {ex.Message}";
                IsStatusError = true;
                IsTestSuccessful = false;
                CanSave = false;
            }
            finally
            {
                IsTesting = false;
                UpdateCommandStates();
            }
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
                return;

            try
            {
                var trimmedKey = ApiKey.Trim();

                if (SaveLocally)
                {
                    var saved = AnythingLLMService.SetUserApiKey(trimmedKey);
                    if (!saved)
                    {
                        StatusMessage = "⚠️ Could not save API key locally, but will use for this session.";
                        IsStatusError = false;
                    }
                    else
                    {
                        StatusMessage = "✅ API key saved successfully!";
                        IsStatusError = false;
                    }
                }
                else
                {
                    StatusMessage = "✅ API key will be used for this session only.";
                    IsStatusError = false;
                }

                // Give user time to see the status message
                await Task.Delay(1000);

                // Notify success
                ApiKeyConfigured?.Invoke(this, new ApiKeyConfiguredEventArgs(trimmedKey, SaveLocally));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving API key: {ex.Message}";
                IsStatusError = true;
                _notificationService.ShowError($"Failed to save API key: {ex.Message}");
            }
        }

        partial void OnApiKeyChanged(string value)
        {
            // Reset test status when API key changes
            IsTestSuccessful = false;
            CanSave = false;
            if (string.IsNullOrWhiteSpace(value))
            {
                StatusMessage = string.Empty;
                IsStatusError = false;
            }
            UpdateCommandStates();
        }

        private void UpdateCommandStates()
        {
            ((AsyncRelayCommand)TestConnectionCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)SaveCommand).NotifyCanExecuteChanged();
        }
    }

    public class ApiKeyConfiguredEventArgs : EventArgs
    {
        public string ApiKey { get; }
        public bool SavedLocally { get; }

        public ApiKeyConfiguredEventArgs(string apiKey, bool savedLocally)
        {
            ApiKey = apiKey;
            SavedLocally = savedLocally;
        }
    }
}