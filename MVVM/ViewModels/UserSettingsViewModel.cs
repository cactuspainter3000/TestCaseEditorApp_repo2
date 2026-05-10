using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class UserSettingsViewModel : ObservableObject
    {
        private readonly IUserSettingsService _userSettingsService;

        [ObservableProperty]
        private bool _isRequired;

        [ObservableProperty]
        private string _jamaBaseUrl = string.Empty;

        [ObservableProperty]
        private string _jamaClientId = string.Empty;

        [ObservableProperty]
        private string _jamaClientSecret = string.Empty;

        [ObservableProperty]
        private string _anythingLlmBaseUrl = "http://localhost:3001";

        [ObservableProperty]
        private string _anythingLlmApiKey = string.Empty;

        [ObservableProperty]
        private string _selectedChatModel = "phi4-mini";

        [ObservableProperty]
        private string _selectedEmbeddingModel = "nomic-embed-text";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isStatusError;

        public ObservableCollection<string> OllamaModels { get; } = new();

        public event EventHandler<bool>? RequestClose;

        public UserSettingsViewModel(IUserSettingsService userSettingsService)
        {
            _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
            LoadFromStoredSettings();
            _ = RefreshOllamaModelsAsync();
        }

        [RelayCommand]
        private async Task RefreshOllamaModelsAsync()
        {
            try
            {
                IsBusy = true;
                IsStatusError = false;
                StatusMessage = "Refreshing Ollama models...";

                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var response = await httpClient.GetAsync("http://localhost:11434/api/tags");

                if (!response.IsSuccessStatusCode)
                {
                    StatusMessage = "Could not read Ollama models. Make sure Ollama is running.";
                    IsStatusError = true;
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(json);

                OllamaModels.Clear();
                if (document.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
                {
                    foreach (var model in models.EnumerateArray())
                    {
                        if (model.TryGetProperty("name", out var nameElement))
                        {
                            var name = nameElement.GetString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                OllamaModels.Add(name);
                            }
                        }
                    }
                }

                if (!OllamaModels.Any())
                {
                    OllamaModels.Add("phi4-mini");
                    OllamaModels.Add("nomic-embed-text");
                }

                if (!OllamaModels.Contains(SelectedChatModel))
                {
                    SelectedChatModel = OllamaModels.FirstOrDefault(m => m.Contains("phi", StringComparison.OrdinalIgnoreCase))
                        ?? OllamaModels.First();
                }

                if (!OllamaModels.Contains(SelectedEmbeddingModel))
                {
                    SelectedEmbeddingModel = OllamaModels.FirstOrDefault(m => m.Contains("embed", StringComparison.OrdinalIgnoreCase))
                        ?? "nomic-embed-text";
                }

                StatusMessage = "Ollama models refreshed.";
                IsStatusError = false;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to refresh models: {ex.Message}";
                IsStatusError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task TestJamaConnectionAsync()
        {
            try
            {
                IsBusy = true;
                IsStatusError = false;
                StatusMessage = "Testing Jama connection...";

                var jamaService = new JamaConnectService((JamaBaseUrl ?? string.Empty).Trim(), (JamaClientId ?? string.Empty).Trim(), (JamaClientSecret ?? string.Empty).Trim(), true);
                var (success, message) = await jamaService.TestConnectionAsync();

                StatusMessage = success ? $"Jama OK: {message}" : $"Jama failed: {message}";
                IsStatusError = !success;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Jama test failed: {ex.Message}";
                IsStatusError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task TestAnythingLlmConnectionAsync()
        {
            try
            {
                IsBusy = true;
                IsStatusError = false;
                StatusMessage = "Testing AnythingLLM connection...";

                var llmService = new AnythingLLMService(baseUrl: NormalizeUrl(AnythingLlmBaseUrl), apiKey: (AnythingLlmApiKey ?? string.Empty).Trim());
                var (success, message) = await llmService.TestConnectivityAsync();

                StatusMessage = success ? $"AnythingLLM OK: {message}" : $"AnythingLLM failed: {message}";
                IsStatusError = !success;
            }
            catch (Exception ex)
            {
                StatusMessage = $"AnythingLLM test failed: {ex.Message}";
                IsStatusError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void Save()
        {
            if (!Validate())
            {
                return;
            }

            var settings = new AppUserSettings
            {
                JamaBaseUrl = (JamaBaseUrl ?? string.Empty).Trim(),
                JamaClientId = (JamaClientId ?? string.Empty).Trim(),
                JamaClientSecret = (JamaClientSecret ?? string.Empty).Trim(),
                AnythingLlmBaseUrl = NormalizeUrl(AnythingLlmBaseUrl),
                AnythingLlmApiKey = (AnythingLlmApiKey ?? string.Empty).Trim(),
                OllamaChatModel = (SelectedChatModel ?? string.Empty).Trim(),
                OllamaEmbeddingModel = (SelectedEmbeddingModel ?? string.Empty).Trim()
            };

            _userSettingsService.SaveSettings(settings);
            _userSettingsService.ApplySettingsToEnvironment(settings);

            StatusMessage = "Settings saved and applied to this session.";
            IsStatusError = false;
            RequestClose?.Invoke(this, true);
        }

        [RelayCommand]
        private void Cancel()
        {
            if (IsRequired)
            {
                StatusMessage = "Settings are required to use Jama and AnythingLLM features.";
                IsStatusError = true;
                return;
            }

            RequestClose?.Invoke(this, false);
        }

        private void LoadFromStoredSettings()
        {
            var settings = _userSettingsService.LoadSettings();
            JamaBaseUrl = settings.JamaBaseUrl;
            JamaClientId = settings.JamaClientId;
            JamaClientSecret = settings.JamaClientSecret;
            AnythingLlmBaseUrl = settings.AnythingLlmBaseUrl;
            AnythingLlmApiKey = settings.AnythingLlmApiKey;
            SelectedChatModel = settings.OllamaChatModel;
            SelectedEmbeddingModel = settings.OllamaEmbeddingModel;

            if (string.IsNullOrWhiteSpace(StatusMessage))
            {
                StatusMessage = settings.HasRequiredConfiguration()
                    ? "Loaded saved settings."
                    : "Complete all fields for first-time setup.";
            }
        }

        private bool Validate()
        {
            if (string.IsNullOrWhiteSpace(JamaBaseUrl)
                || string.IsNullOrWhiteSpace(JamaClientId)
                || string.IsNullOrWhiteSpace(JamaClientSecret)
                || string.IsNullOrWhiteSpace(AnythingLlmBaseUrl)
                || string.IsNullOrWhiteSpace(AnythingLlmApiKey)
                || string.IsNullOrWhiteSpace(SelectedChatModel)
                || string.IsNullOrWhiteSpace(SelectedEmbeddingModel))
            {
                StatusMessage = "All fields are required.";
                IsStatusError = true;
                return false;
            }

            return true;
        }

        private static string NormalizeUrl(string? value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "http://localhost:3001" : value.Trim();
            return normalized.TrimEnd('/');
        }
    }
}
