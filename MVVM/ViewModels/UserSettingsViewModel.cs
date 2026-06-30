using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.ComponentModel;
using System.Net;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.Services;
using System.Diagnostics.CodeAnalysis;
using System.Windows;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class UserSettingsViewModel : ObservableObject
    {
        private static readonly TimeSpan JamaRelationshipProbeTimeout = TimeSpan.FromMinutes(3);
        private readonly IUserSettingsService _userSettingsService;
        private AppUserSettings _lastSavedSettings = AppUserSettings.Empty();
        private bool _isLoadingSettings;

        private static readonly HashSet<string> TrackedSettingPropertyNames = new(StringComparer.Ordinal)
        {
            nameof(JamaBaseUrl),
            nameof(JamaClientId),
            nameof(JamaClientSecret),
            nameof(JamaProjectId),
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
        private string _jamaProjectId = string.Empty;

        [ObservableProperty]
        private string _anythingLlmBaseUrl = "http://localhost:3001";

        [ObservableProperty]
        private string _anythingLlmApiKey = string.Empty;

        [ObservableProperty]
        private string _selectedChatModel = "phi4-mini:latest";

        [ObservableProperty]
        private string _selectedEmbeddingModel = "nomic-embed-text:latest";

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

        [ObservableProperty]
        private string _ollamaDebugInfo = "No Ollama diagnostics yet. Click 'Test selected models'.";

        [ObservableProperty]
        private string _lastJamaExportReport = "No Jama export report yet. Run the export button to generate one.";

        [ObservableProperty]
        private string _lastJamaProbeReport = "No Jama relationship probe report yet. Run the probe button to generate one.";

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
                var (success, response, endpoint, error) = await TryGetOllamaTagsResponseAsync(httpClient);

                if (!success || response == null)
                {
                    EnsureFallbackOllamaModels();
                    StatusMessage = string.IsNullOrWhiteSpace(error)
                        ? "Could not read Ollama models from Ollama API. Using fallback model list."
                        : $"Could not connect to Ollama ({error}). Using fallback model list.";
                    IsStatusError = true;
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(json);

                // Snapshot selections BEFORE clearing — WPF two-way binding will null them when OllamaModels.Clear() fires
                var savedChat = SelectedChatModel;
                var savedEmbed = SelectedEmbeddingModel;

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

                // Restore selections that were wiped by the two-way binding when OllamaModels.Clear() fired
                if (!string.IsNullOrWhiteSpace(savedChat))
                    SelectedChatModel = savedChat;
                if (!string.IsNullOrWhiteSpace(savedEmbed))
                    SelectedEmbeddingModel = savedEmbed;

                var warnings = new List<string>();
                if (!string.IsNullOrWhiteSpace(SelectedChatModel) && !OllamaModels.Contains(SelectedChatModel))
                {
                    warnings.Add($"Saved chat model '{SelectedChatModel}' is not currently installed.");
                }

                if (!string.IsNullOrWhiteSpace(SelectedEmbeddingModel) && !OllamaModels.Contains(SelectedEmbeddingModel))
                {
                    warnings.Add($"Saved embedding model '{SelectedEmbeddingModel}' is not currently installed.");
                }

                if (warnings.Count > 0)
                {
                    StatusMessage = $"Ollama models refreshed from {endpoint}. " + string.Join(" ", warnings);
                    IsStatusError = false;
                }
                else
                {
                    StatusMessage = $"Ollama models refreshed from {endpoint}.";
                    IsStatusError = false;
                }

                System.Diagnostics.Trace.WriteLine($"[UserSettingsViewModel] SelectedChatModel after refresh: {SelectedChatModel}");
                System.Diagnostics.Trace.WriteLine($"[UserSettingsViewModel] SelectedEmbeddingModel after refresh: {SelectedEmbeddingModel}");
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

        [RelayCommand]
        private async Task TestSelectedOllamaModelsAsync()
        {
            var diagnostics = new StringBuilder();

            void AddDiag(string line)
            {
                var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
                diagnostics.AppendLine(stamped);
                System.Diagnostics.Trace.WriteLine($"[UserSettingsViewModel] {stamped}");
            }

            try
            {
                IsBusy = true;
                IsStatusError = false;
                StatusMessage = "Testing selected Ollama models...";

                var selectedChat = (SelectedChatModel ?? string.Empty).Trim();
                var selectedEmbedding = (SelectedEmbeddingModel ?? string.Empty).Trim();

                AddDiag($"Starting Ollama viability test. Chat='{selectedChat}', Embedding='{selectedEmbedding}'");

                if (string.IsNullOrWhiteSpace(selectedChat) || string.IsNullOrWhiteSpace(selectedEmbedding))
                {
                    AddDiag("Validation failed: one or both selected models are empty.");
                    OllamaDebugInfo = diagnostics.ToString().Trim();
                    StatusMessage = "Ollama test failed: select both chat and embedding models first.";
                    IsStatusError = true;
                    return;
                }

                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };

                var (tagsSuccess, tagsResponse, tagsEndpoint, tagsError) = await TryGetOllamaTagsResponseAsync(httpClient);
                if (!tagsSuccess || tagsResponse == null)
                {
                    AddDiag($"Model list check failed. Endpoint='{tagsEndpoint}', Error='{tagsError}'");
                    OllamaDebugInfo = diagnostics.ToString().Trim();
                    StatusMessage = $"Ollama test failed: cannot connect to Ollama tags API ({tagsError}).";
                    IsStatusError = true;
                    return;
                }

                var tagsJson = await tagsResponse.Content.ReadAsStringAsync();
                AddDiag($"Model list endpoint reachable: {tagsEndpoint}");

                var installedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var tagsDoc = JsonDocument.Parse(tagsJson))
                {
                    if (tagsDoc.RootElement.TryGetProperty("models", out var modelsElement) && modelsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var model in modelsElement.EnumerateArray())
                        {
                            if (model.TryGetProperty("name", out var nameElement))
                            {
                                var modelName = nameElement.GetString();
                                if (!string.IsNullOrWhiteSpace(modelName))
                                {
                                    installedModels.Add(modelName);
                                }
                            }
                            else if (model.TryGetProperty("model", out var modelElement))
                            {
                                var modelName = modelElement.GetString();
                                if (!string.IsNullOrWhiteSpace(modelName))
                                {
                                    installedModels.Add(modelName);
                                }
                            }
                        }
                    }
                }

                AddDiag($"Installed model count: {installedModels.Count}");

                if (!installedModels.Contains(selectedChat))
                {
                    AddDiag($"Selected chat model not installed: {selectedChat}");
                    OllamaDebugInfo = diagnostics.ToString().Trim();
                    StatusMessage = $"Ollama test failed: chat model '{selectedChat}' is not installed.";
                    IsStatusError = true;
                    return;
                }

                if (!installedModels.Contains(selectedEmbedding))
                {
                    AddDiag($"Selected embedding model not installed: {selectedEmbedding}");
                    OllamaDebugInfo = diagnostics.ToString().Trim();
                    StatusMessage = $"Ollama test failed: embedding model '{selectedEmbedding}' is not installed.";
                    IsStatusError = true;
                    return;
                }

                var baseEndpoint = tagsEndpoint.EndsWith("/api/tags", StringComparison.OrdinalIgnoreCase)
                    ? tagsEndpoint[..^"/api/tags".Length]
                    : tagsEndpoint.TrimEnd('/');

                var chatCheck = await TestOllamaChatModelAsync(httpClient, baseEndpoint, selectedChat, AddDiag);
                var embeddingCheck = await TestOllamaEmbeddingModelAsync(httpClient, baseEndpoint, selectedEmbedding, AddDiag);

                AddDiag($"Chat check success: {chatCheck}");
                AddDiag($"Embedding check success: {embeddingCheck}");

                var overallSuccess = chatCheck && embeddingCheck;
                StatusMessage = overallSuccess
                    ? "Ollama model test passed: chat and embedding models are viable."
                    : "Ollama model test failed: see diagnostics for endpoint/status details.";
                IsStatusError = !overallSuccess;

                OllamaDebugInfo = diagnostics.ToString().Trim();
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"[{DateTime.Now:HH:mm:ss}] Unexpected exception: {ex}");
                OllamaDebugInfo = diagnostics.ToString().Trim();
                StatusMessage = $"Ollama test failed: {ex.Message}";
                IsStatusError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static async Task<bool> TestOllamaChatModelAsync(HttpClient httpClient, string baseEndpoint, string model, Action<string> addDiag)
        {
            var url = baseEndpoint.TrimEnd('/') + "/api/generate";
            var payload = new
            {
                model,
                prompt = "Respond with exactly: OK",
                stream = false
            };

            addDiag($"POST {url} (chat viability)");

            using var response = await httpClient.PostAsJsonAsync(url, payload);
            var body = await response.Content.ReadAsStringAsync();
            var snippet = body.Length > 300 ? body[..300] + "..." : body;

            addDiag($"Chat viability response: {(int)response.StatusCode} {response.StatusCode}");
            addDiag($"Chat viability body: {snippet}");

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("response", out var responseElement))
                {
                    var content = responseElement.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        addDiag($"Chat output preview: {content.Trim()}");
                    }
                }
            }
            catch
            {
                // Keep test tolerant even if response shape changes.
            }

            return true;
        }

        private static async Task<bool> TestOllamaEmbeddingModelAsync(HttpClient httpClient, string baseEndpoint, string model, Action<string> addDiag)
        {
            var embedUrl = baseEndpoint.TrimEnd('/') + "/api/embed";
            var embedPayload = new
            {
                model,
                input = "embedding viability probe"
            };

            addDiag($"POST {embedUrl} (embedding viability)");

            using var embedResponse = await httpClient.PostAsJsonAsync(embedUrl, embedPayload);
            var embedBody = await embedResponse.Content.ReadAsStringAsync();
            var embedSnippet = embedBody.Length > 300 ? embedBody[..300] + "..." : embedBody;

            addDiag($"Embedding response (/api/embed): {(int)embedResponse.StatusCode} {embedResponse.StatusCode}");
            addDiag($"Embedding body (/api/embed): {embedSnippet}");

            if (embedResponse.IsSuccessStatusCode)
            {
                return true;
            }

            if (embedResponse.StatusCode != HttpStatusCode.NotFound)
            {
                return false;
            }

            var legacyUrl = baseEndpoint.TrimEnd('/') + "/api/embeddings";
            var legacyPayload = new
            {
                model,
                prompt = "embedding viability probe"
            };

            addDiag($"POST {legacyUrl} (legacy embedding fallback)");

            using var legacyResponse = await httpClient.PostAsJsonAsync(legacyUrl, legacyPayload);
            var legacyBody = await legacyResponse.Content.ReadAsStringAsync();
            var legacySnippet = legacyBody.Length > 300 ? legacyBody[..300] + "..." : legacyBody;

            addDiag($"Embedding response (/api/embeddings): {(int)legacyResponse.StatusCode} {legacyResponse.StatusCode}");
            addDiag($"Embedding body (/api/embeddings): {legacySnippet}");

            return legacyResponse.IsSuccessStatusCode;
        }

        private void EnsureFallbackOllamaModels()
        {
            if (!OllamaModels.Any())
            {
                OllamaModels.Add("phi4-mini:latest");
                OllamaModels.Add("nomic-embed-text:latest");
            }
        }

        private static async Task<(bool Success, HttpResponseMessage? Response, string Endpoint, string Error)> TryGetOllamaTagsResponseAsync(HttpClient httpClient)
        {
            string lastError = string.Empty;

            foreach (var endpoint in GetOllamaTagEndpoints())
            {
                try
                {
                    var response = await httpClient.GetAsync(endpoint);
                    if (response.IsSuccessStatusCode)
                    {
                        return (true, response, endpoint, string.Empty);
                    }

                    lastError = $"HTTP {(int)response.StatusCode} at {endpoint}";
                    response.Dispose();
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }

            return (false, null, string.Empty, lastError);
        }

        private static IEnumerable<string> GetOllamaTagEndpoints()
        {
            var endpoints = new List<string>();

            AddEndpoint(endpoints, "http://127.0.0.1:11434");
            AddEndpoint(endpoints, "http://localhost:11434");

            var envHost = Environment.GetEnvironmentVariable("OLLAMA_HOST");
            if (!string.IsNullOrWhiteSpace(envHost))
            {
                AddEndpoint(endpoints, envHost.Trim());
            }

            return endpoints;
        }

        private static void AddEndpoint(List<string> endpoints, string baseOrFullValue)
        {
            if (string.IsNullOrWhiteSpace(baseOrFullValue))
            {
                return;
            }

            var value = baseOrFullValue.Trim();
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = "http://" + value;
            }

            string endpoint;
            if (value.EndsWith("/api/tags", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = value;
            }
            else
            {
                endpoint = value.TrimEnd('/') + "/api/tags";
            }

            if (!endpoints.Contains(endpoint, StringComparer.OrdinalIgnoreCase))
            {
                endpoints.Add(endpoint);
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
        private async Task ExportJamaRequirementFieldDictionaryAsync()
        {
            try
            {
                IsBusy = true;
                IsStatusError = false;
                StatusMessage = "Exporting Jama item type 193 field dictionary...";
                LastJamaExportReport = "Jama export in progress...";

                var outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    outputDirectory = AppContext.BaseDirectory;
                }

                var jamaService = new JamaConnectService((JamaBaseUrl ?? string.Empty).Trim(), (JamaClientId ?? string.Empty).Trim(), (JamaClientSecret ?? string.Empty).Trim(), true);
                var (success, message, outputPath) = await jamaService.ExportRequirementItemType193FieldDictionaryAsync(outputDirectory);

                if (success)
                {
                    StatusMessage = string.IsNullOrWhiteSpace(outputPath)
                        ? $"Jama export complete: {message}"
                        : $"Jama export complete. Output: {outputPath}";
                    LastJamaExportReport = string.IsNullOrWhiteSpace(outputPath)
                        ? $"[JAMA EXPORT SUCCESS]\nTimestamp: {DateTime.Now:O}\nMessage: {message}"
                        : $"[JAMA EXPORT SUCCESS]\nTimestamp: {DateTime.Now:O}\nOutput: {outputPath}\nMessage: {message}";
                    IsStatusError = false;
                }
                else
                {
                    LastJamaExportReport = $"[JAMA EXPORT ERROR]\nTimestamp: {DateTime.Now:O}\nBase URL: {(JamaBaseUrl ?? string.Empty).Trim()}\nMessage: {message}";
                    StatusMessage = $"Jama export failed: {message}";
                    IsStatusError = true;
                }
            }
            catch (Exception ex)
            {
                LastJamaExportReport = $"[JAMA EXPORT EXCEPTION]\nTimestamp: {DateTime.Now:O}\nBase URL: {(JamaBaseUrl ?? string.Empty).Trim()}\nException: {ex.Message}\n\nStack Trace:\n{ex}";
                StatusMessage = $"Jama export failed: {ex.Message}";
                IsStatusError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void CopyJamaExportReport()
        {
            if (string.IsNullOrWhiteSpace(LastJamaExportReport))
            {
                StatusMessage = "No Jama export report available to copy yet.";
                IsStatusError = true;
                return;
            }

            try
            {
                Clipboard.SetText(LastJamaExportReport);
                StatusMessage = "Jama export report copied to clipboard.";
                IsStatusError = false;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not copy Jama export report: {ex.Message}";
                IsStatusError = true;
            }
        }

        [RelayCommand]
        private async Task RunJamaRelationshipProbeAsync()
        {
            try
            {
                IsBusy = true;
                IsStatusError = false;
                StatusMessage = "Running Jama relationship capability probe...";
                LastJamaProbeReport = "Jama relationship capability probe in progress...";

                TestCaseEditorApp.Services.Logging.Log.Info("[JamaProbe] Starting Jama relationship capability probe.");

                var baseUrl = (JamaBaseUrl ?? string.Empty).Trim();
                var clientId = (JamaClientId ?? string.Empty).Trim();
                var clientSecret = (JamaClientSecret ?? string.Empty).Trim();
                var projectIdRaw = (JamaProjectId ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn("[JamaProbe] Probe aborted because required Jama settings are incomplete.");
                    StatusMessage = "Please fill Jama Base URL, Client ID, and Client Secret before running the probe.";
                    IsStatusError = true;
                    return;
                }

                if (!int.TryParse(projectIdRaw, out var projectId) || projectId <= 0)
                {
                    var mediatorObj = App.ServiceProvider?.GetService(typeof(TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator));
                    if (mediatorObj is TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator requirementsMediator)
                    {
                        // CurrentProjectId only works for numeric workspace IDs. Use async resolver for project keys.
                        var resolvedProjectId = requirementsMediator.CurrentProjectId;
                        if (resolvedProjectId <= 0)
                        {
                            resolvedProjectId = await requirementsMediator.GetCurrentProjectIdAsync();
                        }

                        if (resolvedProjectId > 0)
                        {
                            projectId = resolvedProjectId;
                        }
                    }

                    if (projectId > 0)
                    {
                        JamaProjectId = projectId.ToString();
                        StatusMessage = $"Running Jama relationship capability probe for loaded project {projectId}...";
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn("[JamaProbe] Probe aborted because no valid Jama project ID was available.");
                        StatusMessage = "Please enter a valid Jama Project ID before running the probe.";
                        IsStatusError = true;
                        return;
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaProbe] Using BaseUrl='{baseUrl}', ProjectId={projectId}, ClientIdLength={clientId.Length}, ClientSecretLength={clientSecret.Length}.");

                try
                {
                    var host = new Uri(baseUrl).Host;
                    var addresses = await System.Net.Dns.GetHostAddressesAsync(host);
                    var addressSummary = string.Join(", ", addresses.Select(address => address.ToString()));
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaProbe] DNS preflight succeeded for host '{host}': {addressSummary}");
                }
                catch (Exception dnsEx)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error(dnsEx, $"[JamaProbe] DNS preflight failed for BaseUrl '{baseUrl}'.");
                }

                var scriptPath = FindProbeScriptPath();
                if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
                {
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaProbe] Probe script not found. Resolved path='{scriptPath ?? "<null>"}'.");
                    StatusMessage = "Could not find probe-jama-relationship-capabilities.ps1 in the project root.";
                    IsStatusError = true;
                    return;
                }

                var outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                {
                    outputDirectory = AppContext.BaseDirectory;
                }

                var reportPath = Path.Combine(outputDirectory, $"jama-relationship-capability-report-{DateTime.Now:yyyyMMdd-HHmmss}.md");
                var workingDirectory = Path.GetDirectoryName(scriptPath) ?? Directory.GetCurrentDirectory();

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaProbe] Launching script '{scriptPath}' from '{workingDirectory}'. ReportPath='{reportPath}'. TimeoutMinutes={JamaRelationshipProbeTimeout.TotalMinutes:0}.");

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ProjectId {projectId} -ReportPath \"{reportPath}\"",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                startInfo.Environment["JAMA_BASE_URL"] = baseUrl;
                startInfo.Environment["JAMA_CLIENT_ID"] = clientId;
                startInfo.Environment["JAMA_CLIENT_SECRET"] = clientSecret;
                startInfo.Environment["JAMA_PROJECT_ID"] = projectId.ToString();

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error("[JamaProbe] Failed to start PowerShell process for probe.");
                    StatusMessage = "Failed to start PowerShell process for Jama probe.";
                    IsStatusError = true;
                    return;
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaProbe] PowerShell process started. PID={process.Id}.");

                var dispatcher = Application.Current?.Dispatcher;
                var standardOutput = new StringBuilder();
                var standardError = new StringBuilder();
                var liveReport = new StringBuilder();
                var outputCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var errorCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                void PublishProbeReport(string text)
                {
                    if (dispatcher != null)
                    {
                        _ = dispatcher.InvokeAsync(() => LastJamaProbeReport = text);
                    }
                    else
                    {
                        LastJamaProbeReport = text;
                    }
                }

                void AppendProbeReportLine(string prefix, string line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        return;
                    }

                    var stampedLine = $"[{DateTime.Now:HH:mm:ss}] {prefix} {line.Trim()}";
                    lock (liveReport)
                    {
                        if (liveReport.Length > 0)
                        {
                            liveReport.AppendLine();
                        }

                        liveReport.AppendLine(stampedLine);
                    }

                    PublishProbeReport(liveReport.ToString().TrimEnd());
                }

                void UpdateStatusFromProbeLine(string line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        return;
                    }

                    var message = line.Trim();
                    if (message.Length > 180)
                    {
                        message = message[..180] + "...";
                    }

                    if (dispatcher != null)
                    {
                        _ = dispatcher.InvokeAsync(() => StatusMessage = message);
                    }
                    else
                    {
                        StatusMessage = message;
                    }
                }

                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaProbe] Standard output stream completed.");
                        outputCompleted.TrySetResult(true);
                        return;
                    }

                    lock (standardOutput)
                    {
                        standardOutput.AppendLine(e.Data);
                    }

                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaProbe][stdout] {e.Data}");
                    AppendProbeReportLine("[OUT]", e.Data);
                    UpdateStatusFromProbeLine(e.Data);
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaProbe] Standard error stream completed.");
                        errorCompleted.TrySetResult(true);
                        return;
                    }

                    lock (standardError)
                    {
                        standardError.AppendLine(e.Data);
                    }

                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaProbe][stderr] {e.Data}");
                    AppendProbeReportLine("[ERR]", e.Data);
                    UpdateStatusFromProbeLine(e.Data);
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                PublishProbeReport("Jama relationship capability probe in progress...\n\nWaiting for probe output...");

                var exitTask = process.WaitForExitAsync();
                var completedTask = await Task.WhenAny(exitTask, Task.Delay(JamaRelationshipProbeTimeout));

                if (completedTask != exitTask)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaProbe] Probe timed out after {JamaRelationshipProbeTimeout.TotalMinutes:0} minutes. Killing PID={process.Id}.");
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                        // If the process is already gone or cannot be killed, continue with the timeout report.
                    }

                    try
                    {
                        await exitTask;
                    }
                    catch
                    {
                        // Ignore secondary exit errors after a forced kill.
                    }

                    await Task.WhenAll(outputCompleted.Task, errorCompleted.Task);

                    var timeoutOutput = standardOutput.ToString();
                    var timeoutError = standardError.ToString();

                    var timeoutReport =
                        $"[JAMA RELATIONSHIP PROBE TIMEOUT]{Environment.NewLine}" +
                        $"Timestamp: {DateTime.Now:O}{Environment.NewLine}" +
                        $"Project ID: {projectId}{Environment.NewLine}" +
                        $"Timeout: {JamaRelationshipProbeTimeout.TotalMinutes:0} minutes{Environment.NewLine}{Environment.NewLine}" +
                        $"Standard Output:{Environment.NewLine}{timeoutOutput}{Environment.NewLine}{Environment.NewLine}" +
                        $"Standard Error:{Environment.NewLine}{timeoutError}";

                    if (dispatcher != null)
                    {
                        await dispatcher.InvokeAsync(() => LastJamaProbeReport = timeoutReport);
                    }
                    else
                    {
                        LastJamaProbeReport = timeoutReport;
                    }

                    StatusMessage = $"Jama relationship probe timed out after {JamaRelationshipProbeTimeout.TotalMinutes:0} minutes.";
                    IsStatusError = true;
                    return;
                }

                await Task.WhenAll(exitTask, outputCompleted.Task, errorCompleted.Task);

                var standardOutputText = standardOutput.ToString();
                var standardErrorText = standardError.ToString();

                var reportExists = File.Exists(reportPath);
                var exitCode = process.ExitCode;

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaProbe] Probe process exited. ExitCode={exitCode}, ReportExists={reportExists}, ReportPath='{reportPath}'.");

                if (exitCode == 0 && reportExists)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[JamaProbe] Probe completed successfully.");
                    var successReport =
                        $"[JAMA RELATIONSHIP PROBE SUCCESS]{Environment.NewLine}" +
                        $"Timestamp: {DateTime.Now:O}{Environment.NewLine}" +
                        $"Project ID: {projectId}{Environment.NewLine}" +
                        $"Report: {reportPath}{Environment.NewLine}{Environment.NewLine}" +
                        $"Standard Output:{Environment.NewLine}{standardOutputText}";

                    if (dispatcher != null)
                    {
                        await dispatcher.InvokeAsync(() => LastJamaProbeReport = successReport);
                    }
                    else
                    {
                        LastJamaProbeReport = successReport;
                    }

                    StatusMessage = $"Jama relationship probe completed. Report: {reportPath}";
                    IsStatusError = false;
                    return;
                }

                var errorReport =
                    $"[JAMA RELATIONSHIP PROBE ERROR]{Environment.NewLine}" +
                    $"Timestamp: {DateTime.Now:O}{Environment.NewLine}" +
                    $"Project ID: {projectId}{Environment.NewLine}" +
                    $"Exit Code: {exitCode}{Environment.NewLine}" +
                    $"Report Path: {reportPath}{Environment.NewLine}" +
                    $"Report Exists: {reportExists}{Environment.NewLine}{Environment.NewLine}" +
                    $"Standard Output:{Environment.NewLine}{standardOutputText}{Environment.NewLine}{Environment.NewLine}" +
                    $"Standard Error:{Environment.NewLine}{standardErrorText}";

                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() => LastJamaProbeReport = errorReport);
                }
                else
                {
                    LastJamaProbeReport = errorReport;
                }

                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaProbe] Probe failed with exit code {exitCode}. See captured stdout/stderr in app log.");
                StatusMessage = $"Jama relationship probe failed (exit code {exitCode}). See probe report box for details.";
                IsStatusError = true;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[JamaProbe] Probe execution threw an exception.");
                var exceptionReport =
                    $"[JAMA RELATIONSHIP PROBE EXCEPTION]{Environment.NewLine}" +
                    $"Timestamp: {DateTime.Now:O}{Environment.NewLine}" +
                    $"Exception: {ex.Message}{Environment.NewLine}{Environment.NewLine}{ex}";

                if (Application.Current?.Dispatcher != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => LastJamaProbeReport = exceptionReport);
                }
                else
                {
                    LastJamaProbeReport = exceptionReport;
                }

                StatusMessage = $"Jama relationship probe failed: {ex.Message}";
                IsStatusError = true;
            }
            finally
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[JamaProbe] Probe execution finished.");
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
                // Persist entered values (including Jama Project ID) even when required validation fails.
                // This keeps user progress and allows retry without re-entering values.
                var currentSettings = BuildCurrentSettingsSnapshot();
                _userSettingsService.SaveSettings(currentSettings);
                _userSettingsService.ApplySettingsToEnvironment(currentSettings);
                SetLastSavedSettings(currentSettings);

                StatusMessage = string.IsNullOrWhiteSpace(StatusMessage)
                    ? "Settings saved, but required fields are still missing."
                    : $"Settings saved, but required fields are still missing. {StatusMessage}";
                IsStatusError = true;
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
            JamaProjectId = settings.JamaProjectId;
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
            var hasMissingRequiredField = string.IsNullOrWhiteSpace(JamaBaseUrl)
                || string.IsNullOrWhiteSpace(JamaClientId)
                || string.IsNullOrWhiteSpace(JamaClientSecret)
                || string.IsNullOrWhiteSpace(AnythingLlmBaseUrl)
                || string.IsNullOrWhiteSpace(AnythingLlmApiKey)
                || string.IsNullOrWhiteSpace(SelectedChatModel)
                || string.IsNullOrWhiteSpace(SelectedEmbeddingModel);

            if (hasMissingRequiredField && IsRequired)
            {
                StatusMessage = "All fields are required.";
                IsStatusError = true;
                return false;
            }

            // Allow any model in the OllamaModels list, but warn if not recommended
            var recommendedChatModels = new[] { "phi4-mini:latest", "phi4-mini" };
            var recommendedEmbedModels = new[] { "nomic-embed-text:latest", "nomic-embed-text" };
            var warnings = new List<string>();

            if (!OllamaModels.Contains(SelectedChatModel))
            {
                warnings.Add($"Chat model '{SelectedChatModel}' is not currently installed in Ollama.");
            }

            if (!OllamaModels.Contains(SelectedEmbeddingModel))
            {
                warnings.Add($"Embedding model '{SelectedEmbeddingModel}' is not currently installed in Ollama.");
            }

            // Show a non-blocking warning if not using recommended models
            if (!recommendedChatModels.Contains(SelectedChatModel) || !recommendedEmbedModels.Contains(SelectedEmbeddingModel))
            {
                warnings.Add("You are not using the recommended model pair ('phi4-mini:latest' and 'nomic-embed-text:latest').");
            }

            if (hasMissingRequiredField)
            {
                warnings.Add("Some settings fields are still empty.");
            }

            if (warnings.Count > 0)
            {
                StatusMessage = "Saved with warnings: " + string.Join(" ", warnings);
                IsStatusError = false;
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
                JamaProjectId = (JamaProjectId ?? string.Empty).Trim(),
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
                JamaProjectId = (settings.JamaProjectId ?? string.Empty).Trim(),
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
                !string.Equals(current.JamaProjectId, _lastSavedSettings.JamaProjectId, StringComparison.Ordinal) ||
                !string.Equals(current.AnythingLlmBaseUrl, _lastSavedSettings.AnythingLlmBaseUrl, StringComparison.Ordinal) ||
                !string.Equals(current.AnythingLlmApiKey, _lastSavedSettings.AnythingLlmApiKey, StringComparison.Ordinal) ||
                !string.Equals(current.OllamaChatModel, _lastSavedSettings.OllamaChatModel, StringComparison.Ordinal) ||
                !string.Equals(current.OllamaEmbeddingModel, _lastSavedSettings.OllamaEmbeddingModel, StringComparison.Ordinal) ||
                current.EnableRequirementsAnalysisSnapshot != _lastSavedSettings.EnableRequirementsAnalysisSnapshot;
        }

        private static string? FindProbeScriptPath()
        {
            var candidates = new[]
            {
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory
            };

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var dir = new DirectoryInfo(candidate);
                while (dir != null)
                {
                    var scriptPath = Path.Combine(dir.FullName, "probe-jama-relationship-capabilities.ps1");
                    if (File.Exists(scriptPath))
                    {
                        return scriptPath;
                    }

                    dir = dir.Parent;
                }
            }

            return null;
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
