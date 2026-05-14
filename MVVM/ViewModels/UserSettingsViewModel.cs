using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.ComponentModel;
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
        private AppUserSettings _lastSavedSettings = AppUserSettings.Empty();
        private bool _isLoadingSettings;

        private static readonly HashSet<string> TrackedSettingPropertyNames = new(StringComparer.Ordinal)
        {
            nameof(JamaBaseUrl),
            nameof(JamaClientId),
            nameof(JamaClientSecret),
            nameof(AnythingLlmBaseUrl),
            nameof(AnythingLlmApiKey),
            nameof(SelectedChatModel),
            nameof(SelectedEmbeddingModel),
            nameof(EnableRequirementsAnalysisSnapshot)
        };

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
        private bool _enableRequirementsAnalysisSnapshot;

        [ObservableProperty]
        private bool _hasUnsavedChanges;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isStatusError;

        [ObservableProperty]
        private int _selectedSettingsTabIndex;

        public ObservableCollection<string> OllamaModels { get; } = new();
        
        public Func<Task<(bool Success, List<string> Issues)>>? ValidationCallback { get; set; }

        public event EventHandler<bool>? RequestClose;

        public UserSettingsViewModel(IUserSettingsService userSettingsService)
        {
            _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
            PropertyChanged += OnViewModelPropertyChanged;
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
                    EnsureFallbackOllamaModels();
                    StatusMessage = "Could not read Ollama models from Ollama API. Using fallback model list.";
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
                        else if (model.TryGetProperty("model", out var modelElement))
                        {
                            var name = modelElement.GetString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                OllamaModels.Add(name);
                            }
                        }
                    }
                }

                EnsureFallbackOllamaModels();

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
                EnsureFallbackOllamaModels();
                StatusMessage = $"Failed to refresh models: {ex.Message}";
                IsStatusError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void EnsureFallbackOllamaModels()
        {
            if (!OllamaModels.Any())
            {
                OllamaModels.Add("phi4-mini");
                OllamaModels.Add("nomic-embed-text");
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
        private async Task SaveAsync()
        {
            if (!Validate())
            {
                PersistDiagnosticsSettingOnly();
                StatusMessage = string.IsNullOrWhiteSpace(StatusMessage)
                    ? "Requirements analysis snapshot setting saved."
                    : $"Requirements analysis snapshot setting saved. {StatusMessage}";
                RecalculateUnsavedChanges();
                return;
            }

            var settings = BuildCurrentSettingsSnapshot();

            _userSettingsService.SaveSettings(settings);
            _userSettingsService.ApplySettingsToEnvironment(settings);
            SetLastSavedSettings(settings);

            // If there's a validation callback (from the validation gate), run it
            if (ValidationCallback != null)
            {
                try
                {
                    IsBusy = true;
                    StatusMessage = "Validating settings...";
                    IsStatusError = false;

                    var (success, issues) = await ValidationCallback();

                    if (success)
                    {
                        StatusMessage = "Settings saved and validated. Proceeding to Test Case Generator...";
                        IsStatusError = false;
                        RequestClose?.Invoke(this, true);
                    }
                    else
                    {
                        // Validation failed; show errors and keep dialog open
                        var details = string.Join("\n• ", issues);
                        StatusMessage = $"Validation failed:\n• {details}\n\nPlease fix the issues and try again.";
                        IsStatusError = true;
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Validation error: {ex.Message}";
                    IsStatusError = true;
                }
                finally
                {
                    IsBusy = false;
                }
            }
            else
            {
                // No validation callback; just close
                StatusMessage = "Settings saved and applied to this session.";
                IsStatusError = false;
                RequestClose?.Invoke(this, true);
            }
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

        private void PersistDiagnosticsSettingOnly()
        {
            try
            {
                var existingSettings = _userSettingsService.LoadSettings();
                existingSettings.EnableRequirementsAnalysisSnapshot = EnableRequirementsAnalysisSnapshot;
                _userSettingsService.SaveSettings(existingSettings);
                _userSettingsService.ApplySettingsToEnvironment(existingSettings);
                _lastSavedSettings.EnableRequirementsAnalysisSnapshot = EnableRequirementsAnalysisSnapshot;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to persist diagnostics setting: {ex.Message}";
                IsStatusError = true;
            }
        }

        private void LoadFromStoredSettings()
        {
            _isLoadingSettings = true;
            var settings = _userSettingsService.LoadSettings();
            JamaBaseUrl = settings.JamaBaseUrl;
            JamaClientId = settings.JamaClientId;
            JamaClientSecret = settings.JamaClientSecret;
            AnythingLlmBaseUrl = settings.AnythingLlmBaseUrl;
            AnythingLlmApiKey = settings.AnythingLlmApiKey;
            SelectedChatModel = settings.OllamaChatModel;
            SelectedEmbeddingModel = settings.OllamaEmbeddingModel;
            EnableRequirementsAnalysisSnapshot = settings.EnableRequirementsAnalysisSnapshot;
            SetLastSavedSettings(settings);
            _isLoadingSettings = false;

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

        private AppUserSettings BuildCurrentSettingsSnapshot()
        {
            return new AppUserSettings
            {
                JamaBaseUrl = (JamaBaseUrl ?? string.Empty).Trim(),
                JamaClientId = (JamaClientId ?? string.Empty).Trim(),
                JamaClientSecret = (JamaClientSecret ?? string.Empty).Trim(),
                AnythingLlmBaseUrl = NormalizeUrl(AnythingLlmBaseUrl),
                AnythingLlmApiKey = (AnythingLlmApiKey ?? string.Empty).Trim(),
                OllamaChatModel = (SelectedChatModel ?? string.Empty).Trim(),
                OllamaEmbeddingModel = (SelectedEmbeddingModel ?? string.Empty).Trim(),
                EnableRequirementsAnalysisSnapshot = EnableRequirementsAnalysisSnapshot
            };
        }

        private void SetLastSavedSettings(AppUserSettings settings)
        {
            _lastSavedSettings = new AppUserSettings
            {
                JamaBaseUrl = (settings.JamaBaseUrl ?? string.Empty).Trim(),
                JamaClientId = (settings.JamaClientId ?? string.Empty).Trim(),
                JamaClientSecret = (settings.JamaClientSecret ?? string.Empty).Trim(),
                AnythingLlmBaseUrl = NormalizeUrl(settings.AnythingLlmBaseUrl),
                AnythingLlmApiKey = (settings.AnythingLlmApiKey ?? string.Empty).Trim(),
                OllamaChatModel = (settings.OllamaChatModel ?? string.Empty).Trim(),
                OllamaEmbeddingModel = (settings.OllamaEmbeddingModel ?? string.Empty).Trim(),
                EnableRequirementsAnalysisSnapshot = settings.EnableRequirementsAnalysisSnapshot
            };

            HasUnsavedChanges = false;
        }

        private void RecalculateUnsavedChanges()
        {
            if (_isLoadingSettings)
            {
                return;
            }

            var current = BuildCurrentSettingsSnapshot();
            HasUnsavedChanges =
                !string.Equals(current.JamaBaseUrl, _lastSavedSettings.JamaBaseUrl, StringComparison.Ordinal) ||
                !string.Equals(current.JamaClientId, _lastSavedSettings.JamaClientId, StringComparison.Ordinal) ||
                !string.Equals(current.JamaClientSecret, _lastSavedSettings.JamaClientSecret, StringComparison.Ordinal) ||
                !string.Equals(current.AnythingLlmBaseUrl, _lastSavedSettings.AnythingLlmBaseUrl, StringComparison.Ordinal) ||
                !string.Equals(current.AnythingLlmApiKey, _lastSavedSettings.AnythingLlmApiKey, StringComparison.Ordinal) ||
                !string.Equals(current.OllamaChatModel, _lastSavedSettings.OllamaChatModel, StringComparison.Ordinal) ||
                !string.Equals(current.OllamaEmbeddingModel, _lastSavedSettings.OllamaEmbeddingModel, StringComparison.Ordinal) ||
                current.EnableRequirementsAnalysisSnapshot != _lastSavedSettings.EnableRequirementsAnalysisSnapshot;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isLoadingSettings || string.IsNullOrWhiteSpace(e.PropertyName))
            {
                return;
            }

            if (TrackedSettingPropertyNames.Contains(e.PropertyName))
            {
                RecalculateUnsavedChanges();
            }
        }

        private static string NormalizeUrl(string? value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "http://localhost:3001" : value.Trim();
            return normalized.TrimEnd('/');
        }

        partial void OnSelectedSettingsTabIndexChanged(int value)
        {
            if (IsBusy || IsStatusError)
            {
                return;
            }

            StatusMessage = value switch
            {
                0 => "Configure Jama connection settings.",
                1 => "Configure AnythingLLM endpoint and API key.",
                2 => "Select and refresh Ollama model preferences.",
                3 => "Enable or disable requirements analysis snapshot logging.",
                _ => "Configure application settings."
            };
        }
    }
}
