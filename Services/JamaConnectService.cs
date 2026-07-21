using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for integrating with Jama Connect REST API
    /// Provides direct access to requirements and test case management
    /// Implements IJamaConnectService following Architectural Guide AI patterns
    /// </summary>
    public partial class JamaConnectService : IJamaConnectService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string? _username;
        private readonly string? _password;
        private readonly string? _apiToken;
        private readonly string? _clientId;
        private readonly string? _clientSecret;
        private readonly bool _isOAuth;
        private string? _accessToken;
        private JsonDocument? _lastApiResponseJson;
        private List<JsonDocument> _allApiResponses = new List<JsonDocument>();
        private static readonly SemaphoreSlim _rateLimitSemaphore = new(10, 10); // 10 requests per second
        private static DateTime _lastRequest = DateTime.MinValue;
        private const int MinRequestInterval = 100; // milliseconds
        private IOCRService? _ocrService; // Optional OCR service for image text extraction
        
        // Session-based authentication for attachments
        private readonly HttpClient _sessionClient;
        private bool _sessionAuthenticated = false;
        private readonly CookieContainer _cookieContainer;
        private readonly Dictionary<string, Dictionary<string, object?>> _requirementFieldDefaultsCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, int> _requirementItemTypeCache = new();
        private readonly Dictionary<int, int> _requirementPlacementContainerCache = new();
        private readonly Dictionary<int, int?> _projectRelationshipTypeCache = new();
        
        public bool IsConfigured => !string.IsNullOrEmpty(_baseUrl) && 
            (!string.IsNullOrEmpty(_apiToken) || 
             (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password)) ||
             (!string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_clientSecret)));

        /// <summary>
        /// Initialize with API token (recommended)
        /// </summary>
        public JamaConnectService(string baseUrl, string apiToken)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _apiToken = apiToken;
            _httpClient = new HttpClient();
            
            // Initialize session client with cookie container for attachment downloads
            _cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler()
            {
                CookieContainer = _cookieContainer
            };
            _sessionClient = new HttpClient(handler);
        }

        /// <summary>
        /// Initialize with username/password
        /// </summary>
        public JamaConnectService(string baseUrl, string username, string password)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _username = username;
            _password = password;
            _httpClient = new HttpClient();
            
            // Initialize session client with cookie container for attachment downloads
            _cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler()
            {
                CookieContainer = _cookieContainer
            };
            _sessionClient = new HttpClient(handler);
        }

        /// <summary>
        /// Initialize with OAuth client credentials
        /// </summary>
        public JamaConnectService(string baseUrl, string clientId, string clientSecret, bool isOAuth)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _clientId = clientId;
            _clientSecret = clientSecret;
            _isOAuth = isOAuth;
            _httpClient = new HttpClient();
            
            // Initialize session client with cookie container for attachment downloads
            _cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler()
            {
                CookieContainer = _cookieContainer
            };
            _sessionClient = new HttpClient(handler);
            
            // Debug logging
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth constructor - Base URL: {_baseUrl}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth constructor - Client ID: {_clientId}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth constructor - Client Secret: {(!string.IsNullOrEmpty(_clientSecret) ? "SET" : "NOT SET")}");
        }

        /// <summary>
        /// Initialize from configuration (will read from environment or config)
        /// </summary>
        public static JamaConnectService FromConfiguration()
        {
            var baseUrl = Environment.GetEnvironmentVariable("JAMA_BASE_URL") ?? "";
            var apiToken = Environment.GetEnvironmentVariable("JAMA_API_TOKEN");
            var username = Environment.GetEnvironmentVariable("JAMA_USERNAME");
            var password = Environment.GetEnvironmentVariable("JAMA_PASSWORD");
            var clientId = Environment.GetEnvironmentVariable("JAMA_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("JAMA_CLIENT_SECRET");

            // Debug logging
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] FromConfiguration called");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Base URL: {baseUrl}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] API Token: {(!string.IsNullOrEmpty(apiToken) ? "SET" : "NOT SET")}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Client ID: {clientId}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Client Secret: {(!string.IsNullOrEmpty(clientSecret) ? "SET" : "NOT SET")}");

            // Handle common Jama path variations
            if (!string.IsNullOrEmpty(baseUrl) && !baseUrl.EndsWith("/rest"))
            {
                // Check if we need to add /contour for certain Jama instances
                if (baseUrl.Contains("rockwellcollins.com") && !baseUrl.Contains("/contour"))
                {
                    baseUrl = baseUrl.TrimEnd('/') + "/contour";
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Added /contour to URL: {baseUrl}");
                }
            }

            if (!string.IsNullOrEmpty(apiToken))
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Creating service with API Token");
                return new JamaConnectService(baseUrl, apiToken);
            }
            else if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Creating service with OAuth");
                return new JamaConnectService(baseUrl, clientId, clientSecret, true);
            }
            else if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Creating service with username/password");
                return new JamaConnectService(baseUrl, username, password);
            }
            else
            {
                throw new InvalidOperationException(
                    "Jama Connect not configured. Set environment variables: " +
                    "JAMA_BASE_URL and either JAMA_API_TOKEN, (JAMA_CLIENT_ID + JAMA_CLIENT_SECRET), or (JAMA_USERNAME + JAMA_PASSWORD)");
            }
        }

        /// <summary>
        /// Set the OCR service for image text extraction (optional)
        /// </summary>
        public void SetOCRService(IOCRService ocrService)
        {
            _ocrService = ocrService;
            TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] OCR service configured for image text extraction");
            
            // Test OCR availability asynchronously (fire and forget for startup performance)
            Task.Run(async () =>
            {
                try
                {
                    var isAvailable = await ocrService.IsOCRAvailableAsync();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OCR engine availability: {(isAvailable ? "Available" : "Not Available")}");
                    
                    if (!isAvailable)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn("[JamaConnect] OCR engine not available - image text extraction will be skipped. Please ensure Tesseract OCR is installed.");
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed to check OCR availability: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Ensure we have a valid access token
        /// 🚨 CRITICAL OAUTH METHOD - DO NOT MODIFY WITHOUT EXPLICIT CONFIRMATION 🚨
        /// </summary>
        private async Task<bool> EnsureAccessTokenAsync()
        {
            if (string.IsNullOrEmpty(_accessToken))
                return await GetOAuthTokenAsync();
                
            return true;
        }

        /// <summary>
        /// Clear expired token and get a new one - called when "token is expired" error occurs
        /// </summary>
        public async Task<bool> RefreshExpiredTokenAsync()
        {
            TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Clearing expired token and getting new one");
            _accessToken = null; // Clear expired token
            _httpClient.DefaultRequestHeaders.Authorization = null; // Clear auth header
            return await GetOAuthTokenAsync();
        }

        /// <summary>
        /// Get OAuth access token
        /// 🚨 CRITICAL OAUTH METHOD - DO NOT MODIFY WITHOUT EXPLICIT CONFIRMATION 🚨
        /// This method has been fixed multiple times due to token acquisition failures.
        /// The JSON deserialization and error handling are critical for project loading.
        /// </summary>
        private async Task<bool> GetOAuthTokenAsync()
        {
            var tokenUrl = $"{_baseUrl}/rest/oauth/token";
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Getting OAuth token from: {tokenUrl}");
            
            var authBytes = Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}");
            var authHeader = Convert.ToBase64String(authBytes);

            var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
            
            var form = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("scope", "token_information")
            ]);
            
            request.Content = form;
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Sending OAuth request...");
            var response = await _httpClient.SendAsync(request);
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth response: {response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] OAuth response payload: {TruncateForLog(content, 400)}");
                
                try
                {
                    var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(content);
                    
                    // 🚨 STEEL TRAP: Validate token response structure
                    if (tokenResponse == null)
                    {
                        TestCaseEditorApp.Services.Logging.Log.ValidationFailure("OAuth token deserialization", "Returned null - check OAuthTokenResponse JsonPropertyName attributes");
                        return false;
                    }
                    
                    if (!tokenResponse.IsValid)
                    {
                        TestCaseEditorApp.Services.Logging.Log.ValidationFailure("OAuth token response", $"AccessToken: '{tokenResponse.AccessToken ?? "NULL"}', Raw response: {content}");
                        return false;
                    }
                    
                    _accessToken = tokenResponse?.AccessToken;
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
                    
                    var hasToken = !string.IsNullOrEmpty(_accessToken);
                    if (hasToken)
                    {
                        var tokenPreview = _accessToken?.Substring(0, Math.Min(20, _accessToken.Length));
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth token obtained successfully ({tokenPreview}...)");
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] ✅ OAuth steel trap validation passed!");
                    }
                    return hasToken;
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Exception(ex, "[JamaConnect] Failed to deserialize OAuth response");
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] 🚨 STEEL TRAP: Check OAuthTokenResponse class structure!");
                    return false;
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth failed: {TruncateForLog(errorContent, 400)}");
            }
            
            return false;
        }

        /// <summary>
        /// Test connection to Jama Connect API
        /// </summary>
        public async Task<(bool Success, string Message)> TestConnectionAsync()
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] TestConnection called - IsConfigured: {IsConfigured}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Base URL: {_baseUrl}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Client ID: {_clientId}");
            
            if (!IsConfigured)
            {
                var msg = "Service not configured. Missing base URL or credentials.";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] {msg}");
                return (false, msg);
            }

            // Get OAuth token first
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Getting OAuth token...");
            var tokenSuccess = await EnsureAccessTokenAsync();
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Token success: {tokenSuccess}");
            
            if (!tokenSuccess)
            {
                var msg = "Failed to authenticate with OAuth credentials";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] {msg}");
                return (false, msg);
            }
            
            // Test projects API
            var testUrl = $"{_baseUrl}/rest/v1/projects";
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Testing projects API: {testUrl}");
            
            var response = await _httpClient.GetAsync(testUrl);
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Response status: {response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Response content length: {content.Length}");
                
                var projects = JsonSerializer.Deserialize<JamaProjectsResponse>(content, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                var projectCount = projects?.Data?.Count ?? 0;
                var successMsg = $"Successfully connected to Jama Connect. Found {projectCount} projects.";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] {successMsg}");
                return (true, successMsg);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Error response: {errorContent}");
                
                if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError &&
                    (errorContent.Contains("Index") && errorContent.Contains("out of bounds")))
                {
                    var msg = $"OAuth scope issue: Client '{_clientId}' has 'token_information' scope but needs 'read' scope for data access.";
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] {msg}");
                    return (false, msg);
                }
                
                var failMsg = $"Connection failed: {response.StatusCode} - {errorContent}";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] {failMsg}");
                return (false, failMsg);
            }
        }

        /// <summary>
        /// Export item type 193 field dictionary with constraints and picklist options to a timestamped text file.
        /// </summary>
        public async Task<(bool Success, string Message, string? OutputPath)> ExportRequirementItemType193FieldDictionaryAsync(
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return (false, "Output directory is required.", null);
            }

            try
            {
                await EnsureAccessTokenAsync();

                Directory.CreateDirectory(outputDirectory);
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var outputPath = Path.Combine(outputDirectory, $"jama-itemtype-193-field-dictionary-{timestamp}.txt");

                var endpointAttempts = new List<string>();

                async Task<(bool Success, string Url, string Body, string? Error)> TryGetFirstSuccessfulJsonAsync(IEnumerable<string> urls, string label)
                {
                    foreach (var url in urls)
                    {
                        var response = await _httpClient.GetAsync(url, cancellationToken);
                        var body = await response.Content.ReadAsStringAsync(cancellationToken);
                        endpointAttempts.Add($"[{label}] {url} -> {(int)response.StatusCode} {response.StatusCode}");

                        if (response.IsSuccessStatusCode)
                        {
                            return (true, url, body, null);
                        }
                    }

                    return (false, string.Empty, string.Empty, $"No successful {label} endpoint. Attempts: {string.Join(" | ", endpointAttempts.Where(a => a.StartsWith($"[{label}]", StringComparison.OrdinalIgnoreCase)))}");
                }

                var itemTypeCandidates = new[]
                {
                    $"{_baseUrl}/rest/v1/itemtypes/193",
                    $"{_baseUrl}/rest/latest/itemtypes/193"
                };

                var fieldsCandidates = new[]
                {
                    $"{_baseUrl}/rest/v1/itemtypes/193/fields",
                    $"{_baseUrl}/rest/latest/itemtypes/193/fields",
                    $"{_baseUrl}/rest/v1/itemtypes/193?include=fields",
                    $"{_baseUrl}/rest/latest/itemtypes/193?include=fields"
                };

                var itemTypeFetch = await TryGetFirstSuccessfulJsonAsync(itemTypeCandidates, "itemType");
                if (!itemTypeFetch.Success)
                {
                    return (false, $"Failed to fetch item type 193 metadata. {itemTypeFetch.Error}", null);
                }

                var fieldsFetch = await TryGetFirstSuccessfulJsonAsync(fieldsCandidates, "fields");

                var itemTypeUrl = itemTypeFetch.Url;
                var fieldsUrl = fieldsFetch.Url;
                var itemTypeJson = itemTypeFetch.Body;
                var fieldsJson = fieldsFetch.Body;

                if (!fieldsFetch.Success)
                {
                    using var fallbackItemTypeDoc = JsonDocument.Parse(itemTypeJson);
                    if (TryExtractFieldsPayloadFromItemType(fallbackItemTypeDoc.RootElement, out var extractedFieldsJson))
                    {
                        fieldsJson = extractedFieldsJson;
                        fieldsUrl = "embedded:itemtypes/193(fields)";
                        endpointAttempts.Add("[fields] Using embedded fields payload from item type metadata after endpoint fallback.");
                    }
                    else
                    {
                        return (false, $"Failed to fetch item type 193 fields metadata. {fieldsFetch.Error}", null);
                    }
                }

                using var itemTypeDoc = JsonDocument.Parse(itemTypeJson);
                using var fieldsDoc = JsonDocument.Parse(fieldsJson);

                var picklistIds = new HashSet<int>();
                CollectPicklistIds(fieldsDoc.RootElement, picklistIds);
                CollectPicklistIds(itemTypeDoc.RootElement, picklistIds);

                var picklistOptionPayloads = new Dictionary<int, string>();
                var picklistOptionSummaries = new Dictionary<int, List<(int Id, string Name, bool IsDefault)>>();
                foreach (var picklistId in picklistIds.OrderBy(i => i))
                {
                    var picklistUrl = $"{_baseUrl}/rest/v1/picklists/{picklistId}/options";
                    var picklistResponse = await _httpClient.GetAsync(picklistUrl, cancellationToken);
                    var picklistBody = await picklistResponse.Content.ReadAsStringAsync(cancellationToken);
                    if (!picklistResponse.IsSuccessStatusCode)
                    {
                        picklistOptionPayloads[picklistId] = $"ERROR {picklistResponse.StatusCode}: {picklistBody}";
                        continue;
                    }

                    try
                    {
                        using var picklistDoc = JsonDocument.Parse(picklistBody);
                        var picklistArray = TryGetPicklistDataArray(picklistDoc.RootElement);
                        if (picklistArray.HasValue && picklistArray.Value.ValueKind == JsonValueKind.Array)
                        {
                            var options = new List<(int Id, string Name, bool IsDefault)>();
                            foreach (var option in picklistArray.Value.EnumerateArray())
                            {
                                var id = GetIntProperty(option, "id");
                                if (!id.HasValue)
                                {
                                    continue;
                                }

                                var name = GetStringProperty(option, "name")
                                    ?? GetStringProperty(option, "display")
                                    ?? GetStringProperty(option, "value")
                                    ?? "<unnamed>";
                                var isDefault = GetBoolProperty(option, "default") ?? false;
                                options.Add((id.Value, name, isDefault));
                            }

                            picklistOptionSummaries[picklistId] = options;
                        }
                    }
                    catch
                    {
                        // Keep report generation resilient if option payload parsing changes.
                    }

                    picklistOptionPayloads[picklistId] = TryPrettyJson(picklistBody);
                }

                var (projectId, projectName, projectProbeMessage) = await TryGetFirstAccessibleProjectAsync(cancellationToken);
                var requirementDefaults = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                List<(int Id, string Name, int Score)> requirementContainerCandidates = new();
                List<(int Id, string Name)> relationshipTypeCandidates = new();
                int? preferredRelationshipTypeId = null;
                var releaseEndpointAttempts = new List<string>();
                string? releaseSample = null;

                if (projectId.HasValue)
                {
                    requirementDefaults = await GetRequirementFieldDefaultsAsync(projectId.Value, 193, cancellationToken);
                    requirementContainerCandidates = await GetRequirementContainerCandidatesAsync(projectId.Value, cancellationToken);
                    relationshipTypeCandidates = await GetRelationshipTypeCandidatesAsync(projectId.Value, cancellationToken);
                    preferredRelationshipTypeId = await GetPreferredRelationshipTypeIdAsync(projectId.Value, cancellationToken);

                    var releaseEndpoints = new[]
                    {
                        $"{_baseUrl}/rest/v1/projects/{projectId.Value}/releases",
                        $"{_baseUrl}/rest/latest/projects/{projectId.Value}/releases",
                        $"{_baseUrl}/rest/v1/releases?project={projectId.Value}",
                        $"{_baseUrl}/rest/latest/releases?project={projectId.Value}"
                    };

                    foreach (var endpoint in releaseEndpoints)
                    {
                        var releaseResponse = await _httpClient.GetAsync(endpoint, cancellationToken);
                        var releaseBody = await releaseResponse.Content.ReadAsStringAsync(cancellationToken);
                        releaseEndpointAttempts.Add($"{endpoint} -> {(int)releaseResponse.StatusCode} {releaseResponse.StatusCode}");

                        if (!releaseResponse.IsSuccessStatusCode)
                        {
                            continue;
                        }

                        try
                        {
                            using var releaseDoc = JsonDocument.Parse(releaseBody);
                            var releaseData = TryGetPicklistDataArray(releaseDoc.RootElement);
                            if (releaseData.HasValue && releaseData.Value.ValueKind == JsonValueKind.Array)
                            {
                                var sampleNames = releaseData.Value.EnumerateArray()
                                    .Take(5)
                                    .Select(r => GetStringProperty(r, "name") ?? GetStringProperty(r, "releaseDate") ?? "<unknown>")
                                    .ToList();
                                releaseSample = sampleNames.Count > 0
                                    ? string.Join(", ", sampleNames)
                                    : "Release endpoint reachable but no entries returned.";
                            }
                        }
                        catch
                        {
                            releaseSample = "Release endpoint reachable, response shape not parsed.";
                        }

                        if (!string.IsNullOrWhiteSpace(releaseSample))
                        {
                            break;
                        }
                    }
                }

                var report = new StringBuilder();
                report.AppendLine("Jama Requirement Item Type 193 Field Dictionary Export");
                report.AppendLine($"Generated Local: {DateTime.Now:O}");
                report.AppendLine($"Generated UTC:   {DateTime.UtcNow:O}");
                report.AppendLine($"Base URL:        {_baseUrl}");
                report.AppendLine();

                report.AppendLine("Summary");
                report.AppendLine($"- ItemType endpoint: {itemTypeUrl}");
                report.AppendLine($"- Fields endpoint:   {fieldsUrl}");
                report.AppendLine($"- Picklist IDs discovered: {(picklistIds.Count == 0 ? "none" : string.Join(", ", picklistIds.OrderBy(i => i)))}");
                report.AppendLine($"- Endpoint attempts: {(endpointAttempts.Count == 0 ? "none" : string.Join(" | ", endpointAttempts))}");
                report.AppendLine($"- Probe project: {(projectId.HasValue ? $"{projectId.Value} ({projectName ?? "<unnamed>"})" : projectProbeMessage ?? "none")}");
                report.AppendLine();

                report.AppendLine("Parsed Fields (best-effort)");
                var fieldsArray = TryGetFieldsDataArray(fieldsDoc.RootElement)
                    ?? TryGetPicklistDataArray(fieldsDoc.RootElement);
                if (fieldsArray.HasValue && fieldsArray.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in fieldsArray.Value.EnumerateArray())
                    {
                        var fieldKey = GetStringProperty(field, "fieldName")
                            ?? GetStringProperty(field, "name")
                            ?? GetStringProperty(field, "apiName")
                            ?? GetStringProperty(field, "key")
                            ?? "<unknown>";

                        var type = GetStringProperty(field, "fieldType")
                            ?? GetStringProperty(field, "dataType")
                            ?? GetStringProperty(field, "type")
                            ?? "<unknown>";

                        var picklistId = GetIntProperty(field, "pickList")
                            ?? GetIntProperty(field, "picklist")
                            ?? GetIntProperty(field, "pickListId")
                            ?? GetIntProperty(field, "picklistId")
                            ?? GetIntProperty(field, "lookupType")
                            ?? GetIntProperty(field, "lookupTypeId");

                        var required = GetBoolProperty(field, "required");
                        var readOnly = GetBoolProperty(field, "readOnly") ?? GetBoolProperty(field, "readonly");
                        var multiSelect = GetBoolProperty(field, "multiSelect") ?? GetBoolProperty(field, "allowMultiple");
                        var minLength = GetIntProperty(field, "minLength");
                        var maxLength = GetIntProperty(field, "maxLength");

                        report.AppendLine($"- key={fieldKey}, type={type}, required={(required.HasValue ? required.Value.ToString() : "n/a")}, readOnly={(readOnly.HasValue ? readOnly.Value.ToString() : "n/a")}, multiSelect={(multiSelect.HasValue ? multiSelect.Value.ToString() : "n/a")}, minLength={(minLength.HasValue ? minLength.Value.ToString() : "n/a")}, maxLength={(maxLength.HasValue ? maxLength.Value.ToString() : "n/a")}, picklistId={(picklistId.HasValue ? picklistId.Value.ToString() : "n/a")}");
                    }
                }
                else
                {
                    report.AppendLine("- No field array found in response.");
                }

                report.AppendLine();
                report.AppendLine("Field Coverage Check");
                JamaFieldCoverageHealth? contractHealth = null;
                if (fieldsArray.HasValue && fieldsArray.Value.ValueKind == JsonValueKind.Array)
                {
                    var coverage = JamaRequirementFieldCoverage.AnalyzeFieldDictionary(fieldsArray.Value);
                    contractHealth = JamaRequirementFieldCoverage.EvaluateCoverageHealth(coverage);
                    report.AppendLine(JamaRequirementFieldCoverage.FormatCoverageResultForReport(coverage));
                }
                else
                {
                    report.AppendLine("- Field coverage check unavailable (field array not parsed).");
                }

                report.AppendLine();
                report.AppendLine("Extended Mapping Diagnostics");

                report.AppendLine("- Requirement create contract map (item type 193):");
                if (fieldsArray.HasValue && fieldsArray.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in fieldsArray.Value.EnumerateArray())
                    {
                        var fieldKey = GetStringProperty(field, "fieldName")
                            ?? GetStringProperty(field, "name")
                            ?? GetStringProperty(field, "apiName")
                            ?? GetStringProperty(field, "key")
                            ?? "<unknown>";

                        var type = GetStringProperty(field, "fieldType")
                            ?? GetStringProperty(field, "dataType")
                            ?? GetStringProperty(field, "type")
                            ?? "<unknown>";

                        var required = GetBoolProperty(field, "required") ?? false;
                        var readOnly = GetBoolProperty(field, "readOnly") ?? GetBoolProperty(field, "readonly") ?? false;
                        var picklistId = GetIntProperty(field, "pickList")
                            ?? GetIntProperty(field, "picklist")
                            ?? GetIntProperty(field, "pickListId")
                            ?? GetIntProperty(field, "picklistId")
                            ?? GetIntProperty(field, "lookupType")
                            ?? GetIntProperty(field, "lookupTypeId");

                        var createContract = readOnly
                            ? "Server-managed/read-only"
                            : required
                                ? "Required at create (or default must exist)"
                                : "Optional at create";

                        var defaultText = "n/a";
                        if (requirementDefaults.TryGetValue(fieldKey, out var defaultValue) && defaultValue != null)
                        {
                            defaultText = defaultValue is JsonElement jsonDefault
                                ? jsonDefault.ToString()
                                : defaultValue.ToString() ?? "n/a";
                        }

                        report.AppendLine($"  * {fieldKey}: {createContract}; type={type}; picklistId={(picklistId.HasValue ? picklistId.Value.ToString() : "n/a")}; default={defaultText}");
                    }
                }
                else
                {
                    report.AppendLine("  * Not available (field array not parsed).");
                }

                report.AppendLine("- Multi-lookup payload shape map:");
                if (fieldsArray.HasValue && fieldsArray.Value.ValueKind == JsonValueKind.Array)
                {
                    var anyMultiLookup = false;
                    foreach (var field in fieldsArray.Value.EnumerateArray())
                    {
                        var type = GetStringProperty(field, "fieldType")
                            ?? GetStringProperty(field, "dataType")
                            ?? GetStringProperty(field, "type")
                            ?? string.Empty;
                        if (!type.Contains("MULTI_LOOKUP", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        anyMultiLookup = true;
                        var fieldKey = GetStringProperty(field, "fieldName")
                            ?? GetStringProperty(field, "name")
                            ?? GetStringProperty(field, "apiName")
                            ?? GetStringProperty(field, "key")
                            ?? "<unknown>";
                        var picklistId = GetIntProperty(field, "pickList")
                            ?? GetIntProperty(field, "picklist")
                            ?? GetIntProperty(field, "pickListId")
                            ?? GetIntProperty(field, "picklistId");

                        var sample = picklistId.HasValue && picklistOptionSummaries.TryGetValue(picklistId.Value, out var options) && options.Count > 0
                            ? $"e.g. [{options[0].Id}]"
                            : "e.g. [<option-id>]";

                        report.AppendLine($"  * {fieldKey}: send integer array of option IDs, {sample}");
                    }

                    if (!anyMultiLookup)
                    {
                        report.AppendLine("  * No MULTI_LOOKUP fields discovered.");
                    }
                }
                else
                {
                    report.AppendLine("  * Not available (field array not parsed).");
                }

                report.AppendLine("- Status/workflow map:");
                var statusFieldPicklistId = fieldsArray.HasValue && fieldsArray.Value.ValueKind == JsonValueKind.Array
                    ? fieldsArray.Value.EnumerateArray()
                        .Where(f => string.Equals(GetStringProperty(f, "name") ?? GetStringProperty(f, "fieldName"), "status", StringComparison.OrdinalIgnoreCase))
                        .Select(f => GetIntProperty(f, "pickList")
                            ?? GetIntProperty(f, "picklist")
                            ?? GetIntProperty(f, "pickListId")
                            ?? GetIntProperty(f, "picklistId"))
                        .FirstOrDefault()
                    : null;

                if (statusFieldPicklistId.HasValue && picklistOptionSummaries.TryGetValue(statusFieldPicklistId.Value, out var statusOptions) && statusOptions.Count > 0)
                {
                    report.AppendLine($"  * Status picklist ({statusFieldPicklistId.Value}) options: {string.Join(", ", statusOptions.Select(o => o.IsDefault ? $"{o.Name} [default]" : o.Name))}");
                    report.AppendLine("  * Transition rules endpoint not standardized across all Jama instances; transitions should be validated with project workflow settings.");
                }
                else
                {
                    report.AppendLine("  * Could not resolve status picklist options.");
                }

                report.AppendLine("- Requirement parent location compatibility map:");
                if (projectId.HasValue)
                {
                    if (requirementContainerCandidates.Count == 0)
                    {
                        report.AppendLine($"  * No candidate requirement containers found for project {projectId.Value}.");
                    }
                    else
                    {
                        foreach (var candidate in requirementContainerCandidates.Take(10))
                        {
                            report.AppendLine($"  * Candidate container ID={candidate.Id}, name='{candidate.Name}', score={candidate.Score}");
                        }
                    }
                }
                else
                {
                    report.AppendLine("  * Project-scoped probe skipped (no accessible project resolved).");
                }

                report.AppendLine("- Relationship API map:");
                if (projectId.HasValue)
                {
                    if (relationshipTypeCandidates.Count == 0)
                    {
                        report.AppendLine("  * No relationship types discovered from available endpoints.");
                    }
                    else
                    {
                        report.AppendLine($"  * Preferred relationship type ID: {(preferredRelationshipTypeId.HasValue ? preferredRelationshipTypeId.Value.ToString() : "n/a")}");
                        foreach (var rel in relationshipTypeCandidates.Take(20))
                        {
                            report.AppendLine($"  * RelationshipType ID={rel.Id}, name='{rel.Name}'");
                        }
                    }
                }
                else
                {
                    report.AppendLine("  * Project-scoped probe skipped (no accessible project resolved).");
                }

                report.AppendLine("- Release field map:");
                if (projectId.HasValue)
                {
                    report.AppendLine($"  * Endpoint attempts: {(releaseEndpointAttempts.Count == 0 ? "none" : string.Join(" | ", releaseEndpointAttempts))}");
                    report.AppendLine($"  * Sample releases: {releaseSample ?? "none discovered"}");
                }
                else
                {
                    report.AppendLine("  * Project-scoped probe skipped (no accessible project resolved).");
                }

                report.AppendLine();
                report.AppendLine("Raw JSON: itemtypes/193");
                report.AppendLine(TryPrettyJson(itemTypeJson));
                report.AppendLine();
                report.AppendLine("Raw JSON: itemtypes/193/fields");
                report.AppendLine(TryPrettyJson(fieldsJson));

                foreach (var kvp in picklistOptionPayloads.OrderBy(k => k.Key))
                {
                    report.AppendLine();
                    report.AppendLine($"Raw JSON: picklists/{kvp.Key}/options");
                    report.AppendLine(kvp.Value);
                }

                await File.WriteAllTextAsync(outputPath, report.ToString(), Encoding.UTF8, cancellationToken);

                if (contractHealth is { IsContractHealthy: false })
                {
                    var missingCore = contractHealth.MissingCoreFields.Count == 0
                        ? "unknown"
                        : string.Join(", ", contractHealth.MissingCoreFields);
                    var failMessage = $"Export completed, but Jama item type 193 core contract validation FAILED. Missing core fields: {missingCore}.";
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] {failMessage}");
                    return (false, failMessage, outputPath);
                }

                return (true, "Requirement item type 193 field dictionary exported successfully.", outputPath);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Failed to export field dictionary for item type 193: {ex.Message}");
                return (false, $"Export failed: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Get list of projects accessible to the current user
        /// </summary>
        public async Task<List<JamaProject>> GetProjectsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Ensure we have a valid access token for OAuth
                await EnsureAccessTokenAsync();
                
                var response = await _httpClient.GetAsync($"{_baseUrl}/rest/v1/projects", cancellationToken);
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Projects API response: {response.StatusCode}");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Raw JSON response (first 1000 chars): {json.Substring(0, Math.Min(json.Length, 1000))}");
                    
                    var result = JsonSerializer.Deserialize<JamaProjectsResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    // Log the first few projects to see what data is actually being mapped
                    if (result?.Data?.Any() == true)
                    {
                        for (int i = 0; i < Math.Min(3, result.Data.Count); i++)
                        {
                            var p = result.Data[i];
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Project {i}: Id={p.Id}, Name='{p.Name}', Key='{p.Key}', Desc='{p.Description}'");
                        }
                    }
                    
                    return result?.Data ?? new List<JamaProject>();
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Projects API failed: {response.StatusCode} - {errorContent}");
                    throw new HttpRequestException($"Failed to get projects: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to get Jama projects: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Enhanced requirements retrieval using cookbook patterns
        /// Combines search, include parameters, and retry logic for optimal performance
        /// Falls back to standard API if enhanced features are not available
        /// </summary>
        public async Task<List<Requirement>> GetRequirementsEnhancedAsync(int projectId, CancellationToken cancellationToken = default)
        {
            return await WithRetryAsync(async () =>
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Getting requirements (enhanced) from project {projectId}");

                await EnsureAccessTokenAsync();
                
                try
                {
                    // Use enhanced search with include parameters for comprehensive data fetching
                    var includeParams = new List<string> 
                    {
                        "data.createdBy",
                        "data.modifiedBy",
                        "data.project",
                        "data.itemType"
                    };
                    
                    // Use abstract items for better search capabilities
                    var jamaItems = await SearchAbstractItemsAsync(
                        projectId: projectId,
                        maxResults: 1000,  // Increased batch size per cookbook
                        includeParams: includeParams
                    );
                    
                    // Convert to requirements
                    var requirements = await ConvertToRequirementsAsync(jamaItems);
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Retrieved {requirements.Count} requirements using enhanced patterns");
                    return requirements;
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Enhanced requirements failed, falling back to standard API: {ex.Message}");
                    
                    // Fallback to standard requirements API
                    var jamaItems = await GetRequirementsAsync(projectId, cancellationToken);
                    var requirements = await ConvertToRequirementsAsync(jamaItems);
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Retrieved {requirements.Count} requirements using fallback method");
                    return requirements;
                }
            });
        }

        /// <summary>
        /// Get requirements from a specific project
        /// Enhanced with fallback mechanisms for better reliability
        /// </summary>
        public async Task<List<JamaItem>> GetRequirementsAsync(int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                // Clear accumulated API responses for fresh start
                foreach (var doc in _allApiResponses)
                {
                    doc?.Dispose();
                }
                _allApiResponses.Clear();
                TestCaseEditorApp.Services.Logging.Log.Debug("[JamaConnect] Cleared previous API responses for fresh OCR processing");
                
                // Ensure we have a valid access token for OAuth
                await EnsureAccessTokenAsync();

                var (itemTypeResolved, requirementItemTypeId) = await GetRequirementItemTypeAsync(projectId, cancellationToken);
                var requirementItemTypeQuery = itemTypeResolved && requirementItemTypeId.HasValue
                    ? $"&itemType={requirementItemTypeId.Value}"
                    : string.Empty;

                if (itemTypeResolved && requirementItemTypeId.HasValue)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Using project requirement item type {requirementItemTypeId.Value} for project {projectId}");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Could not resolve requirement item type for project {projectId}. Falling back to legacy item retrieval.");
                }
                
                // Try enhanced URL with include parameters first
                var url1 = $"{_baseUrl}/rest/v1/items?project={projectId}{requirementItemTypeQuery}&maxResults=50&include=createdBy,modifiedBy,createdDate,modifiedDate";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Trying enhanced format: {url1}");
                
                var response = await _httpClient.GetAsync(url1, cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] API Response Status: {response.StatusCode}");
                
                // If enhanced format fails with server error, try basic format
                if (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Enhanced format failed with 500: {errorContent}");
                    TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Trying basic format without include parameters");
                    
                    var basicUrl = $"{_baseUrl}/rest/v1/items?project={projectId}{requirementItemTypeQuery}&maxResults=50";
                    response = await _httpClient.GetAsync(basicUrl, cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Basic format response: {response.StatusCode}");
                }
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Received API response: {json.Length} characters");
                    
                    var result = JsonSerializer.Deserialize<JamaItemsResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    // Parse JSON for rich content extraction from dynamic fields
                    try
                    {
                        var jsonDoc = JsonDocument.Parse(json);
                        _allApiResponses.Add(jsonDoc);
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Added page JSON data: {_allApiResponses.Count} total pages stored");
                        
                        // Also keep the last response for backward compatibility
                        _lastApiResponseJson?.Dispose();
                        _lastApiResponseJson = JsonDocument.Parse(json);
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Could not parse JSON for rich content extraction: {ex.Message}");
                    }
                    
                    var allItems = result?.Data ?? new List<JamaItem>();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Retrieved {allItems.Count} items from first page");
                    
                    // DIAGNOSTIC: Log first 3 items to verify DocumentKey deserialization
                    foreach (var item in allItems.Take(3))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Sample item {item.Id}: DocumentKey='{item.DocumentKey}', GlobalId='{item.GlobalId}', Item='{item.Item}'");
                    }
                    
                    // Check if there are more items to fetch (pagination)
                    if (allItems.Count == 50) // Full page means there might be more
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] First page full, fetching additional pages...");
                        
                        int pageNumber = 2;
                        int startIndex = 50;
                        bool hasMorePages = true;
                        var totalResults = result?.Meta?.PageInfo?.TotalResults;
                        
                        while (hasMorePages && pageNumber <= 200) // High safety cap; normal stop comes from pageInfo
                        {
                            var nextPageUrl = $"{_baseUrl}/rest/v1/items?project={projectId}{requirementItemTypeQuery}&maxResults=50&startAt={startIndex}&include=createdBy,modifiedBy,createdDate,modifiedDate";
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Fetching page {pageNumber} with user metadata: startAt={startIndex}");
                            
                            var nextPageResponse = await _httpClient.GetAsync(nextPageUrl, cancellationToken);
                            
                            // Handle pagination errors gracefully
                            if (!nextPageResponse.IsSuccessStatusCode && nextPageResponse.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                            {
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Page {pageNumber} failed with 500 error, trying basic format");
                                var basicPageUrl = $"{_baseUrl}/rest/v1/items?project={projectId}{requirementItemTypeQuery}&maxResults=50&startAt={startIndex}";
                                nextPageResponse = await _httpClient.GetAsync(basicPageUrl, cancellationToken);
                            }
                            
                            if (nextPageResponse.IsSuccessStatusCode)
                            {
                                var nextPageJson = await nextPageResponse.Content.ReadAsStringAsync(cancellationToken);
                                var nextPageResult = JsonSerializer.Deserialize<JamaItemsResponse>(nextPageJson, new JsonSerializerOptions 
                                { 
                                    PropertyNameCaseInsensitive = true 
                                });
                                
                                // Parse JSON for rich content extraction from dynamic fields (pagination fix)
                                try
                                {
                                    var jsonDoc = JsonDocument.Parse(nextPageJson);
                                    _allApiResponses.Add(jsonDoc);
                                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Added page {pageNumber} JSON data: {_allApiResponses.Count} total pages stored");
                                }
                                catch (Exception ex)
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Could not parse JSON for page {pageNumber}: {ex.Message}");
                                }
                                
                                var pageItems = nextPageResult?.Data ?? new List<JamaItem>();
                                allItems.AddRange(pageItems);

                                var resultCount = nextPageResult?.Meta?.PageInfo?.ResultCount ?? pageItems.Count;
                                var pageTotalResults = nextPageResult?.Meta?.PageInfo?.TotalResults;
                                if (pageTotalResults.HasValue && pageTotalResults.Value > 0)
                                {
                                    totalResults = pageTotalResults.Value;
                                }
                                
                                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Page {pageNumber}: Retrieved {pageItems.Count} items (total: {allItems.Count})");
                                
                                // Prefer server pagination metadata; fallback to legacy page-size behavior if metadata is absent.
                                if (totalResults.HasValue && totalResults.Value > 0)
                                {
                                    hasMorePages = startIndex + resultCount < totalResults.Value && resultCount > 0;
                                    startIndex += Math.Max(resultCount, 0);
                                }
                                else
                                {
                                    hasMorePages = pageItems.Count == 50;
                                    startIndex += 50;
                                }

                                pageNumber++;
                            }
                            else
                            {
                                var errorContent = await nextPageResponse.Content.ReadAsStringAsync(cancellationToken);
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Failed to fetch page {pageNumber}: {nextPageResponse.StatusCode} - {errorContent}");
                                
                                // For server errors, stop pagination but continue with what we have
                                if (nextPageResponse.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Server error during pagination - continuing with partial results");
                                    break;
                                }
                                
                                // For other errors, stop pagination
                                break;
                            }
                        }
                        
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Pagination complete: {allItems.Count} total items from {pageNumber - 1} pages");
                    }
                    
                    // Final guard filtering to avoid non-requirement bleed-through when endpoint ignores itemType.
                    var requirements = itemTypeResolved && requirementItemTypeId.HasValue
                        ? allItems.Where(item => item.ItemType == requirementItemTypeId.Value).ToList()
                        : allItems.Where(item => item.ItemType == 193).ToList();

                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Retrieved {requirements.Count} requirements from {allItems.Count} total items (requirementType={requirementItemTypeId?.ToString() ?? "legacy-193"})");
                    
                    return requirements;
                }
                else
                {
                    // If all attempts fail, log detailed error but try to return empty list for graceful degradation
                    var finalErrorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] All API attempts failed. Status: {response.StatusCode}, Content: {finalErrorContent}");
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Server error encountered - returning empty list for graceful degradation");
                        return new List<JamaItem>(); // Return empty list instead of throwing
                    }
                    
                    throw new HttpRequestException($"Failed to get requirements: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to get Jama requirements: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get the count of items (requirements/test cases) in a project
        /// </summary>
        public async Task<int> GetProjectRequirementCountAsync(int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                var (requirementTypeSuccess, requirementItemType) = await GetRequirementItemTypeAsync(projectId, cancellationToken);
                if (!requirementTypeSuccess || !requirementItemType.HasValue)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Could not resolve requirement item type for project {projectId}");
                    return 0;
                }

                return await GetProjectItemCountAsync(projectId, requirementItemType.Value, cancellationToken);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Error getting requirement count for project {projectId}");
                return 0;
            }
        }

        /// <summary>
        /// Get the total item count for a specific Jama item type within a project.
        /// Uses pagination metadata when available for an accurate server-side total.
        /// </summary>
        public async Task<int> GetProjectItemCountAsync(int projectId, int itemTypeId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureAccessTokenAsync();

                var url = $"{_baseUrl}/rest/v1/items?project={projectId}&itemType={itemTypeId}&maxResults=1";
                var response = await _httpClient.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Failed to get item count for project {projectId}, itemType {itemTypeId}: {response.StatusCode}");
                    return 0;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);

                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (TryGetPageInfoTotalResults(doc.RootElement, out var totalResults))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Project {projectId}, itemType {itemTypeId} total count: {totalResults}");
                        return totalResults;
                    }
                }
                catch (Exception parseEx)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Could not parse pageInfo totalResults for project {projectId}, itemType {itemTypeId}: {parseEx.Message}");
                }

                var fallback = JsonSerializer.Deserialize<JamaItemsResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var fallbackCount = fallback?.Data?.Count ?? 0;
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Project {projectId}, itemType {itemTypeId} fallback count: {fallbackCount}");
                return fallbackCount;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Error getting project item count for project {projectId}, itemType {itemTypeId}");
                return 0;
            }
        }

        private static bool TryGetPageInfoTotalResults(JsonElement root, out int totalResults)
        {
            totalResults = 0;

            if (!root.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!meta.TryGetProperty("pageInfo", out var pageInfo) || pageInfo.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!pageInfo.TryGetProperty("totalResults", out var totalElement))
            {
                return false;
            }

            if (totalElement.ValueKind == JsonValueKind.Number && totalElement.TryGetInt32(out var numericTotal))
            {
                totalResults = numericTotal;
                return true;
            }

            if (totalElement.ValueKind == JsonValueKind.String && int.TryParse(totalElement.GetString(), out var stringTotal))
            {
                totalResults = stringTotal;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get user information by user ID
        /// </summary>
        public async Task<JamaUser?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureAccessTokenAsync();
                
                var url = $"{_baseUrl}/rest/v1/users/{userId}";
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Getting user info: {url}");
                
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var result = JsonSerializer.Deserialize<JamaUserResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    return result?.Data;
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Failed to get user {userId}: {response.StatusCode}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to get user {userId}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Get individual item with user metadata resolved
        /// </summary>
        public async Task<JamaItem?> GetItemWithUserMetadataAsync(int itemId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureAccessTokenAsync();
                
                var url = $"{_baseUrl}/rest/v1/abstractitems/{itemId}";
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Getting item with user metadata: {url}");
                
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var result = JsonSerializer.Deserialize<JamaItemResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    if (result?.Data != null)
                    {
                        // Resolve CreatedBy user ID to name
                        if (result.Data.CreatedBy > 0)
                        {
                            var user = await GetUserAsync(result.Data.CreatedBy, cancellationToken);
                            if (user != null)
                            {
                                result.Data.CreatedByName = $"{user.FirstName} {user.LastName}".Trim();
                            }
                        }
                        
                        // Resolve ModifiedBy user ID to name
                        if (result.Data.ModifiedBy > 0)
                        {
                            var user = await GetUserAsync(result.Data.ModifiedBy, cancellationToken);
                            if (user != null)
                            {
                                result.Data.ModifiedByName = $"{user.FirstName} {user.LastName}".Trim();
                            }
                        }
                    }
                    
                    return result?.Data;
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Failed to get item {itemId}: {response.StatusCode}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to get item {itemId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get requirements with user metadata resolved (enhanced version)
        /// Uses individual item calls to get complete user information
        /// </summary>
        public async Task<List<JamaItem>> GetRequirementsWithUserMetadataAsync(int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                // First get the bulk list of requirements
                var bulkRequirements = await GetRequirementsAsync(projectId, cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Retrieved {bulkRequirements.Count} requirements, enhancing with user metadata...");
                
                var enhancedRequirements = new List<JamaItem>();
                
                // Process in batches to avoid overwhelming the API
                const int batchSize = 10;
                for (int i = 0; i < bulkRequirements.Count; i += batchSize)
                {
                    var batch = bulkRequirements.Skip(i).Take(batchSize);
                    var enhancedBatch = new List<JamaItem>();
                    
                    // Process batch items in parallel for efficiency
                    var tasks = batch.Select(async item =>
                    {
                        var enhancedItem = await GetItemWithUserMetadataAsync(item.Id, cancellationToken);
                        if (enhancedItem != null)
                        {
                            // Preserve the bulk data fields that aren't in individual calls
                            enhancedItem.Fields = item.Fields;
                            enhancedItem.Name = item.Name;
                            enhancedItem.Description = item.Description;
                            enhancedItem.Status = item.Status;
                            enhancedItem.Item = item.Item;
                            enhancedItem.Project = item.Project;
                            enhancedItem.ItemType = item.ItemType;
                        }
                        return enhancedItem;
                    });
                    
                    var batchResults = await Task.WhenAll(tasks);
                    enhancedBatch.AddRange(batchResults.Where(r => r != null)!);
                    
                    enhancedRequirements.AddRange(enhancedBatch);
                    
                    // Small delay between batches to be respectful to the API
                    if (i + batchSize < bulkRequirements.Count)
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                }
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Enhanced {enhancedRequirements.Count} requirements with user metadata");
                return enhancedRequirements;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to get requirements with user metadata for project {projectId}: {ex.Message}");
                // Fallback to bulk requirements if enhanced version fails
                return await GetRequirementsAsync(projectId, cancellationToken);
            }
        }
        
        /// <summary>
        /// Parse all fields in a Jama item for rich content (HTML tables, paragraphs, etc.)
        /// This method works with the raw JSON API response to access dynamic field names
        /// </summary>
        private async Task<RequirementLooseContent> ParseAllFieldsForRichContentFromJsonAsync(JsonElement itemElement, string description, int itemId)
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: 🔍 ENHANCED FIELD SCANNING STARTED - OCR processing will be attempted on HTML fields with images");
            
            var looseContent = new RequirementLooseContent()
            {
                Paragraphs = new List<string>(),
                Tables = new List<LooseTable>()
            };

            // Metadata/system fields that should NOT be displayed as user-facing content
            // These contain IDs, keys, and other technical data that users don't need to see
            // "description" is handled explicitly - extracting from fields would duplicate it
            var metadataFieldsToSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "documentKey", "globalId", "name", "text2", "id", "key", "itemType",
                "project", "createdBy", "modifiedBy", "createdDate", "modifiedDate",
                "parentId", "childItemType", "sortOrder", "release", "status",
                "synchronizedItem", "lockedBy", "lastLockedDate", "baselinedApplicableItems",
                "description"  // Already handled explicitly above - avoid duplicate processing
            };

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Scanning all fields for rich content");

            var fieldsToScan = new List<(string name, string content)>();

            // Add description field
            if (!string.IsNullOrWhiteSpace(description))
            {
                fieldsToScan.Add(("description", description));
            }

            // Scan all fields from the raw JSON
            if (itemElement.TryGetProperty("fields", out JsonElement fieldsElement))
            {
                foreach (var field in fieldsElement.EnumerateObject())
                {
                    try
                    {
                        // Skip metadata/system fields that shouldn't be displayed as content
                        if (metadataFieldsToSkip.Contains(field.Name))
                        {
                            TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Skipping metadata field '{field.Name}'");
                            continue;
                        }
                        
                        if (field.Value.ValueKind == JsonValueKind.String)
                        {
                            var value = field.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                fieldsToScan.Add((field.Name, value));
                                
                                // Log interesting field names for debugging
                                if (field.Name.Contains("jurisdiction") || field.Name.Contains("table") || 
                                    field.Name.Contains("classification") || value.Contains("<table"))
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Found potential table field '{field.Name}' with HTML content");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Failed to read field {field.Name}: {ex.Message}");
                    }
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Found {fieldsToScan.Count} fields to scan for HTML content");
            
            // Log all fields being scanned for debugging
            for (int i = 0; i < fieldsToScan.Count; i++)
            {
                var (fieldName, content) = fieldsToScan[i];
                var hasHtml = content.Contains("<") && content.Contains(">");
                var truncatedContent = content.Length > 200 ? content.Substring(0, 200) + "..." : content;
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Field {i + 1}/{fieldsToScan.Count}: '{fieldName}' - Length: {content.Length}, HasHTML: {hasHtml}, Content: {truncatedContent}");
            }

            // Process each field for HTML content
            int totalTablesFound = 0;
            int totalParagraphsFound = 0;
            int htmlFieldsFound = 0;

            foreach (var (fieldName, content) in fieldsToScan)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Processing field '{fieldName}' - Length: {content.Length} chars");
                
                if (content.Contains("<") && content.Contains(">"))
                {
                    htmlFieldsFound++;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Processing HTML content in field '{fieldName}' ({content.Length} chars)");

                    var fieldContent = ParseHtmlContent(content, itemId);
                    
                    // Merge tables (with deduplication)
                    foreach (var table in fieldContent.Tables)
                    {
                        // Check for duplicate tables based on content
                        bool isDuplicate = looseContent.Tables.Any(existingTable =>
                            existingTable.ColumnHeaders.Count == table.ColumnHeaders.Count &&
                            existingTable.Rows.Count == table.Rows.Count &&
                            existingTable.ColumnHeaders.SequenceEqual(table.ColumnHeaders) &&
                            existingTable.Rows.SelectMany(r => r).SequenceEqual(table.Rows.SelectMany(r => r)));
                        
                        if (!isDuplicate)
                        {
                            // Set a descriptive title indicating which field this came from
                            if (string.IsNullOrWhiteSpace(table.EditableTitle) || table.EditableTitle.StartsWith("Table from Jama Item"))
                            {
                                table.EditableTitle = $"Table from {fieldName} (Item {itemId})";
                            }
                            looseContent.Tables.Add(table);
                            totalTablesFound++;
                            
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Extracted table from field '{fieldName}' with {table.Rows.Count} rows");
                        }
                        else
                        {
                            TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Skipped duplicate table from field '{fieldName}'");
                        }
                    }

                    // Merge paragraphs
                    // Skip paragraphs from "description" field - that content is already in the Description property
                    if (fieldName != "description")
                    {
                        foreach (var paragraph in fieldContent.Paragraphs)
                        {
                            if (!string.IsNullOrWhiteSpace(paragraph))
                            {
                                // Only skip exact duplicates to preserve original content
                                if (!looseContent.Paragraphs.Contains(paragraph))
                                {
                                    looseContent.Paragraphs.Add(paragraph);
                                    totalParagraphsFound++;
                                }
                            }
                        }
                    }

                    // Extract text from images using OCR (if OCR service is available)
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Checking OCR availability for field '{fieldName}' - OCR Service: {(_ocrService != null ? "Available" : "Not Available")}");
                    
                    if (_ocrService != null)
                    {
                        try
                        {
                            await ExtractTextFromImagesInContentAsync(content, itemId, fieldName, looseContent);
                        }
                        catch (Exception ex)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Item {itemId}: Failed to extract text from images in field '{fieldName}': {ex.Message}");
                        }
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: OCR service not available, skipping image text extraction");
                    }
                }
                else 
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Skipping non-HTML field '{fieldName}' - no OCR processing for non-HTML content");
                    
                    if (!string.IsNullOrWhiteSpace(content) && fieldName != "description")
                    {
                        // Plain text content from non-description fields - add as paragraph
                        var normalizedContent = content.Trim();
                        
                        // Only skip exact duplicates to preserve original content
                        if (!looseContent.Paragraphs.Contains(normalizedContent))
                        {
                            looseContent.Paragraphs.Add(normalizedContent);
                            totalParagraphsFound++;
                        }
                    }
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Rich content parsing complete - " +
                $"{htmlFieldsFound} HTML fields, {totalTablesFound} tables, {totalParagraphsFound} paragraphs");

            return looseContent;
        }

        /// <summary>
        /// Get rich content for an item, using JSON data if available, fallback to object data
        /// </summary>
        private async Task<RequirementLooseContent> GetRichContentForItemAsync(int itemId, Dictionary<int, JsonElement> jsonLookup, string description)
        {
            // Add enhanced diagnostics for OCR troubleshooting
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: GetRichContentForItemAsync called - jsonLookup.Count={jsonLookup.Count}");
            
            // Try to use JSON data for better field access
            if (jsonLookup.TryGetValue(itemId, out JsonElement jsonElement))
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: 🎯 Using ENHANCED field scanning (JSON data available) - OCR processing enabled");
                return await ParseAllFieldsForRichContentFromJsonAsync(jsonElement, description, itemId);
            }
            else
            {
                // Fallback to basic content parsing from description only
                // IMPORTANT: Only extract TABLES from description - paragraphs would duplicate the Description property
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: ⚠️ Using BASIC fallback parsing (JSON data not available) - OCR processing DISABLED");
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Available JSON IDs: [{string.Join(", ", jsonLookup.Keys.Take(10))}]{(jsonLookup.Count > 10 ? "..." : "")}");
                var looseContent = ParseHtmlContent(description, itemId);
                // Clear paragraphs since description content is already in the Description property
                looseContent.Paragraphs.Clear();
                return looseContent;
            }
        }

        /// <summary>
        /// Parse HTML content to extract rich content like tables and paragraphs
        /// </summary>
        private RequirementLooseContent ParseHtmlContent(string htmlContent, int itemId)
        {
            var looseContent = new RequirementLooseContent()
            {
                Paragraphs = new List<string>(),
                Tables = new List<LooseTable>()
            };

            if (string.IsNullOrWhiteSpace(htmlContent))
            {
                return looseContent;
            }

            try
            {
                // Check if content contains HTML tags
                if (!htmlContent.Contains("<") || !htmlContent.Contains(">"))
                {
                    // Plain text content - add as single paragraph with HTML entity cleaning
                    var cleanedText = CleanHtmlText(htmlContent);
                    if (!string.IsNullOrWhiteSpace(cleanedText))
                    {
                        looseContent.Paragraphs.Add(cleanedText);
                    }
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Plain text content, added as single paragraph after cleaning");
                    return looseContent;
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Parsing HTML content for rich elements");

                // Load HTML content
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                // Extract tables first
                var tableNodes = doc.DocumentNode.SelectNodes("//table");
                if (tableNodes != null)
                {
                    foreach (var tableNode in tableNodes)
                    {
                        var table = ExtractTable(tableNode, itemId);
                        if (table != null)
                        {
                            looseContent.Tables.Add(table);
                        }
                        
                        // Remove the table from the document so it doesn't appear in paragraphs
                        tableNode.Remove();
                    }
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Extracted {looseContent.Tables.Count} tables from HTML and removed them from text content");
                }

                // Extract paragraphs from the remaining content (after tables removed)
                ExtractParagraphs(doc.DocumentNode, looseContent.Paragraphs, itemId);

                // If no paragraphs were extracted but we have content, fall back to the cleaned text
                if (looseContent.Paragraphs.Count == 0 && !string.IsNullOrWhiteSpace(doc.DocumentNode.InnerText))
                {
                    var plainText = CleanHtmlText(doc.DocumentNode.InnerText);
                    if (!string.IsNullOrWhiteSpace(plainText))
                    {
                        looseContent.Paragraphs.Add(plainText);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Fallback to plain text extraction from cleaned content");
                    }
                }
                
                // Store the cleaned description (with tables removed) for use in requirement description
                looseContent.CleanedDescription = CleanHtmlText(doc.DocumentNode.InnerText);

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: HTML parsing complete - {looseContent.Tables.Count} tables, {looseContent.Paragraphs.Count} paragraphs, cleaned description prepared");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Item {itemId}: Failed to parse HTML content, falling back to plain text");
                // Fallback: treat as plain text
                looseContent.Paragraphs.Add(htmlContent.Trim());
            }

            return looseContent;
        }

        /// <summary>
        /// Extract table structure from HTML table node
        /// </summary>
        private LooseTable? ExtractTable(HtmlNode tableNode, int itemId)
        {
            try
            {
                var table = new LooseTable();

                // Extract table caption if exists
                var captionNode = tableNode.SelectSingleNode(".//caption");
                if (captionNode != null)
                {
                    table.EditableTitle = captionNode.InnerText.Trim();
                }

                // Extract rows
                var rowNodes = tableNode.SelectNodes(".//tr");
                if (rowNodes == null || rowNodes.Count == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Table found but no rows, skipping");
                    return null;
                }

                var isFirstRowHeaders = false;
                var headerRow = rowNodes[0];
                
                // Check if first row contains th elements (headers)
                var headerCells = headerRow.SelectNodes(".//th");
                if (headerCells != null && headerCells.Count > 0)
                {
                    isFirstRowHeaders = true;
                    // Extract headers
                    foreach (var cell in headerCells)
                    {
                        // Apply CleanHtmlText to decode HTML entities in headers
                        var headerText = CleanHtmlText(cell.InnerText);
                        table.ColumnHeaders.Add(headerText);
                    }
                }
                else
                {
                    // Check if first row should be treated as headers based on td content
                    var firstRowCells = headerRow.SelectNodes(".//td");
                    if (firstRowCells != null && firstRowCells.Count > 0)
                    {
                        // Add headers from first row if it looks like headers
                        foreach (var cell in firstRowCells)
                        {
                            // Apply CleanHtmlText to decode HTML entities in headers
                            var cellText = CleanHtmlText(cell.InnerText);
                            table.ColumnHeaders.Add(cellText);
                        }
                        isFirstRowHeaders = true; // Treat first row as headers
                    }
                }

                // Generate column keys for headers
                for (int i = 0; i < table.ColumnHeaders.Count; i++)
                {
                    table.ColumnKeys.Add($"c{i}");
                }

                // Extract data rows (skip first row if it was headers)
                int startIndex = isFirstRowHeaders ? 1 : 0;
                for (int i = startIndex; i < rowNodes.Count; i++)
                {
                    var rowNode = rowNodes[i];
                    var cellNodes = rowNode.SelectNodes(".//td | .//th");
                    
                    if (cellNodes != null && cellNodes.Count > 0)
                    {
                        var row = new List<string>();
                        foreach (var cell in cellNodes)
                        {
                            // Apply CleanHtmlText to decode HTML entities in table cells
                            var cellText = CleanHtmlText(cell.InnerText);
                            row.Add(cellText);
                        }
                        
                        // Clean up trailing empty cells at parse time
                        while (row.Count > 0 && string.IsNullOrWhiteSpace(row[row.Count - 1]))
                        {
                            row.RemoveAt(row.Count - 1);
                        }
                        
                        // Only add rows that have actual content
                        if (row.Count > 0 && row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                        {
                            table.Rows.Add(row);
                        }
                    }
                }

                // Clean up empty columns more aggressively
                if (table.Rows.Any())
                {
                    // Find the maximum number of non-empty columns across all rows
                    var maxUsefulColumns = 0;
                    foreach (var row in table.Rows)
                    {
                        // Find the last non-empty cell in this row
                        var lastContentIndex = -1;
                        for (int i = row.Count - 1; i >= 0; i--)
                        {
                            if (!string.IsNullOrWhiteSpace(row[i]))
                            {
                                lastContentIndex = i;
                                break;
                            }
                        }
                        maxUsefulColumns = Math.Max(maxUsefulColumns, lastContentIndex + 1);
                    }

                    // Trim all rows to this size and update headers accordingly
                    for (int i = 0; i < table.Rows.Count; i++)
                    {
                        var row = table.Rows[i];
                        while (row.Count > maxUsefulColumns)
                        {
                            row.RemoveAt(row.Count - 1);
                        }
                    }

                    // Trim headers to match
                    while (table.ColumnHeaders.Count > maxUsefulColumns)
                    {
                        table.ColumnHeaders.RemoveAt(table.ColumnHeaders.Count - 1);
                        table.ColumnKeys.RemoveAt(table.ColumnKeys.Count - 1);
                    }
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Cleaned table to {maxUsefulColumns} useful columns");
                }

                // Set a default title if none was found
                if (string.IsNullOrWhiteSpace(table.EditableTitle))
                {
                    table.EditableTitle = $"Table from Jama Item {itemId}";
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Extracted table '{table.EditableTitle}' with {table.ColumnHeaders.Count} headers and {table.Rows.Count} rows");
                return table;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Item {itemId}: Failed to extract table");
                return null;
            }
        }

        /// <summary>
        /// Convert Jama itemType numbers to human-readable text
        /// </summary>
        private string GetHumanReadableItemType(int? itemType)
        {
            return itemType switch
            {
                193 => "Requirement",
                194 => "Test Case", 
                195 => "Test Step",
                196 => "Defect",
                197 => "Epic",
                198 => "User Story",
                199 => "Task",
                _ => itemType?.ToString() ?? "Unknown"
            };
        }

        /// <summary>
        /// Clean and decode HTML text content, converting entities to proper text and stripping HTML tags
        /// Preserves original paragraph structure and line breaks from Jama GUI
        /// Removes table content since tables are parsed separately as structured data
        /// </summary>
        private string CleanHtmlText(string htmlText)
        {
            if (string.IsNullOrWhiteSpace(htmlText))
                return string.Empty;

            try
            {
                // First decode HTML entities
                var decoded = System.Net.WebUtility.HtmlDecode(htmlText);
                
                // If no HTML tags, just clean whitespace and return
                if (!decoded.Contains("<") || !decoded.Contains(">"))
                {
                    return System.Text.RegularExpressions.Regex.Replace(decoded.Trim(), @"\s+", " ");
                }

                // Parse HTML and extract plain text (this avoids duplication)
                var doc = new HtmlDocument();
                doc.LoadHtml(decoded);

                // Remove table nodes to avoid duplicate content - tables are parsed separately as structured data
                var tableNodes = doc.DocumentNode.SelectNodes(".//table")?.ToList();
                if (tableNodes != null)
                {
                    foreach (var tableNode in tableNodes)
                    {
                        tableNode.Remove();
                    }
                }

                // Extract text after removing tables
                var plainText = doc.DocumentNode.InnerText;
                
                if (string.IsNullOrWhiteSpace(plainText))
                    return string.Empty;

                // Basic formatting cleanup only
                // 1. Normalize multiple spaces/tabs to single space
                plainText = System.Text.RegularExpressions.Regex.Replace(plainText, @"[ \t]+", " ");
                
                // 2. Normalize line breaks
                plainText = System.Text.RegularExpressions.Regex.Replace(plainText, @"\r\n|\r|\n", "\n");
                
                // 3. Remove excessive blank lines (more than 2 newlines)
                plainText = System.Text.RegularExpressions.Regex.Replace(plainText, @"\n{3,}", "\n\n");
                
                // 4. Trim final result
                return plainText.Trim();
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Error cleaning HTML text: {ex.Message}");
                // Fallback: basic HTML tag removal
                var fallback = System.Text.RegularExpressions.Regex.Replace(htmlText, @"<[^>]+>", "");
                return System.Net.WebUtility.HtmlDecode(fallback).Trim();
            }
        }

        /// <summary>
        /// Extract text from HTML nodes while preserving paragraph structure and line breaks
        /// <summary>
        /// Extract paragraphs from HTML, excluding table content
        /// </summary>
        private void ExtractParagraphs(HtmlNode node, List<string> paragraphs, int itemId)
        {
            try
            {
                // Remove table nodes to avoid duplicate content
                var tableNodes = node.SelectNodes(".//table")?.ToList();
                if (tableNodes != null)
                {
                    foreach (var tableNode in tableNodes)
                    {
                        tableNode.Remove();
                    }
                }

                // Extract paragraph nodes
                var paragraphNodes = node.SelectNodes(".//p");
                if (paragraphNodes != null)
                {
                    foreach (var pNode in paragraphNodes)
                    {
                        var text = CleanHtmlText(pNode.InnerText);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            paragraphs.Add(text);
                        }
                    }
                }

                // If no paragraph nodes found, extract text from other common elements
                if (paragraphs.Count == 0)
                {
                    // Try div elements
                    var divNodes = node.SelectNodes(".//div");
                    if (divNodes != null)
                    {
                        foreach (var divNode in divNodes)
                        {
                            var text = CleanHtmlText(divNode.InnerText);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                paragraphs.Add(text);
                            }
                        }
                    }
                }

                // If still no content, extract from lists
                if (paragraphs.Count == 0)
                {
                    var listItems = node.SelectNodes(".//li");
                    if (listItems != null)
                    {
                        var listContent = new List<string>();
                        foreach (var li in listItems)
                        {
                            var text = CleanHtmlText(li.InnerText);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                listContent.Add($"• {text}");
                            }
                        }
                        if (listContent.Count > 0)
                        {
                            paragraphs.Add(string.Join("\n", listContent));
                        }
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Extracted {paragraphs.Count} paragraphs");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Item {itemId}: Failed to extract paragraphs");
            }
        }

        /// <summary>
        /// Convert Jama items to our Requirement model
        /// </summary>
        public async Task<List<Requirement>> ConvertToRequirementsAsync(List<JamaItem> jamaItems)
        {
            var requirements = new List<Requirement>();
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Converting {jamaItems.Count} Jama items to requirements");
            
            // Create a lookup for JSON elements by item ID for rich content parsing FROM ALL PAGES
            Dictionary<int, JsonElement> jsonItemLookup = new Dictionary<int, JsonElement>();
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Building JSON lookup from {_allApiResponses.Count} API response pages");
            
            foreach (var apiResponseJson in _allApiResponses)
            {
                try
                {
                    if (apiResponseJson.RootElement.TryGetProperty("data", out JsonElement dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                    {
                        int pageItems = 0;
                        foreach (var jsonItem in dataElement.EnumerateArray())
                        {
                            if (jsonItem.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.Number)
                            {
                                var id = idElement.GetInt32();
                                jsonItemLookup[id] = jsonItem;
                                pageItems++;
                            }
                        }
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Added {pageItems} items to JSON lookup from one API page");
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed to process one API response page for JSON lookup: {ex.Message}");
                }
            }
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] ✅ JSON lookup created with {jsonItemLookup.Count} items from ALL pages - OCR processing now available for all requirements");
            
            foreach (var item in jamaItems)
            {
                // Enhanced field mapping with better fallbacks
                // CRITICAL: Log all ID fields to diagnose why DocumentKey might not be used
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id} ID fields - Item: '{item.Item}', DocumentKey: '{item.DocumentKey}', GlobalId: '{item.GlobalId}'");
                
                var itemId = item.Item ?? item.DocumentKey ?? item.GlobalId ?? $"JAMA-{item.Id}";
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id} final selected ID: '{itemId}'");
                
                // Access Name and Description directly from the item, not from Fields
                var name = item.Name;
                var description = item.Description;
                
                // Try multiple field sources for name
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = item.Fields?.Name;  // Fallback to Fields.Name
                }
                
                // Try multiple field sources for description  
                if (string.IsNullOrWhiteSpace(description))
                {
                    description = item.Fields?.Description;  // Fallback to Fields.Description
                }
                
                // Use the actual name or empty string - NO fake name generation

                var requirement = new Requirement
                {
                    Item = itemId,
                    Name = CleanHtmlText(name ?? ""),  // Clean HTML entities from name
                    Description = CleanHtmlText(description ?? ""),
                    GlobalId = item.GlobalId ?? "",
                    Status = item.Status ?? item.Fields?.Status ?? "",
                    RequirementType = GetHumanReadableItemType(item.ItemType),
                    Project = item.Project?.ToString() ?? "",
                    
                    // Enhanced field mapping from Jama with user metadata resolution
                    Version = item.Fields?.Version ?? "",
                    CreatedBy = GetCreatedByField(item),  // Enhanced user resolution
                    ModifiedBy = GetModifiedByField(item), // Enhanced user resolution  
                    CreatedDateRaw = item.Fields?.CreatedDate ?? "",
                    ModifiedDateRaw = item.Fields?.ModifiedDate ?? "",
                    KeyCharacteristics = CleanHtmlText(item.Fields?.KeyCharacteristics ?? ""), // Clean HTML entities
                    Fdal = item.Fields?.FDAL ?? "",
                    DerivedRequirement = item.Fields?.Derived ?? "",
                    ExportControlled = item.Fields?.ExportControlled ?? "",
                    SetName = item.Fields?.Set ?? "",
                    Heading = CleanHtmlText(item.Fields?.Heading ?? ""), // Clean HTML entities
                    ChangeDriver = CleanHtmlText(item.Fields?.ChangeDriver ?? ""), // Clean HTML entities
                    LastLockedBy = item.Fields?.LockedBy ?? "",
                    
                    // Set dates if available from enhanced metadata
                    CreatedDate = TryParseDate(item.CreatedDate),
                    ModifiedDate = TryParseDate(item.ModifiedDate),
                    
                    // Initialize LooseContent with HTML parsing for rich content from ALL fields
                    LooseContent = await GetRichContentForItemAsync(item.Id, jsonItemLookup, description ?? "")
                };
                
                requirements.Add(requirement);
            }
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Successfully converted {requirements.Count} Jama items to requirements");
            
            return requirements;
        }
        
        /// <summary>
        /// Get CreatedBy field with enhanced user metadata resolution
        /// </summary>
        private string GetCreatedByField(JamaItem item)
        {
            // Priority 1: Enhanced user metadata (from individual item calls)
            if (!string.IsNullOrEmpty(item.CreatedByName))
            {
                return item.CreatedByName;
            }
            
            // Priority 2: Fallback to bulk field data
            if (!string.IsNullOrEmpty(item.Fields?.CreatedBy))
            {
                return item.Fields.CreatedBy;
            }
            
            // Priority 3: Empty string for missing data
            return "";
        }
        
        /// <summary>
        /// Get ModifiedBy field with enhanced user metadata resolution
        /// </summary>
        private string GetModifiedByField(JamaItem item)
        {
            // Priority 1: Enhanced user metadata (from individual item calls)
            if (!string.IsNullOrEmpty(item.ModifiedByName))
            {
                return item.ModifiedByName;
            }
            
            // Priority 2: Fallback to bulk field data  
            if (!string.IsNullOrEmpty(item.Fields?.ModifiedBy))
            {
                return item.Fields.ModifiedBy;
            }
            
            // Priority 3: Empty string for missing data
            return "";
        }
        
        /// <summary>
        /// Try to parse date string into nullable DateTime
        /// </summary>
        private DateTime? TryParseDate(string dateString)
        {
            if (string.IsNullOrEmpty(dateString))
                return null;
                
            if (DateTime.TryParse(dateString, out var result))
                return result;
                
            return null;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // ENHANCED API METHODS - Based on Jama Developer Cookbook patterns
        // ═══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Download file from rich text field attachment using Files endpoint
        /// Based on cookbook pattern for downloading attachments from rich text fields
        /// </summary>
        public async Task<byte[]?> DownloadFileAsync(string jamaFileUrl)
        {
            return await WithRetryAsync(async () =>
            {
                await EnsureAccessTokenAsync();
                
                // Extract file URL parameter if it's a full Jama URL
                var fileUrl = jamaFileUrl;
                if (jamaFileUrl.Contains("?url="))
                {
                    var urlParam = jamaFileUrl.Split("?url=")[1];
                    fileUrl = Uri.UnescapeDataString(urlParam);
                }
                
                // Use Files endpoint: GET /rest/v1/files?url=<file-url>
                var filesEndpoint = $"{_baseUrl}/rest/v1/files?url={Uri.EscapeDataString(fileUrl)}";
                
                var response = await _httpClient.GetAsync(filesEndpoint);
                response.EnsureSuccessStatusCode();
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Downloaded file from {fileUrl}");
                return await response.Content.ReadAsByteArrayAsync();
            });
        }

        /// <summary>
        /// Get attachments from a Jama project with a limit for faster automatic scanning.
        /// Based on: https://dev.jamasoftware.com/cookbook/ - REST Attachments section
        /// </summary>
        public async Task<List<JamaAttachment>> GetProjectAttachmentsLimitedAsync(
            int projectId,
            int maxItems = 20,
            CancellationToken cancellationToken = default,
            Action<int, int, string>? progressCallback = null,
            string projectName = "")
        {
            return await WithRetryAsync(async () =>
            {
                await EnsureAccessTokenAsync();
                
                var attachments = new List<JamaAttachment>();
                var displayName = string.IsNullOrWhiteSpace(projectName) ? $"Project {projectId}" : projectName;
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Starting limited attachment discovery for project {projectId} (max {maxItems} items)");
                
                try 
                {
                    // Step 1: Get limited items in the project
                    var items = await GetAbstractItemsForProjectAsync(projectId, cancellationToken);
                    var itemsToCheck = items.Take(maxItems).ToList();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Checking first {itemsToCheck.Count} of {items.Count} items for attachments");
                    progressCallback?.Invoke(0, Math.Max(1, itemsToCheck.Count), $"Searching {displayName} for attachments | quick scan 0% | 0 attachments found");
                    
                    // Step 2: Check limited items for attachments  
                    var itemsWithAttachments = 0;
                    var scannedItems = 0;
                    foreach (var item in itemsToCheck)
                    {
                        scannedItems++;
                        try
                        {
                            var itemAttachments = await GetItemAttachmentsAsync(item.Id, cancellationToken);
                            if (itemAttachments.Count > 0)
                            {
                                attachments.AddRange(itemAttachments);
                                itemsWithAttachments++;
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id} ({item.DocumentKey}) has {itemAttachments.Count} attachment(s)");
                                
                                // Early exit if we found enough attachments for preview
                                if (attachments.Count >= 10)
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Found {attachments.Count} attachments, stopping scan early");
                                    var earlyPercentage = itemsToCheck.Count == 0 ? 100 : (int)Math.Round((double)scannedItems / itemsToCheck.Count * 100);
                                    progressCallback?.Invoke(scannedItems, Math.Max(1, itemsToCheck.Count), $"Searching {displayName} for attachments | quick scan {earlyPercentage}% | {attachments.Count} attachments found");
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed to check attachments for item {item.Id}: {ex.Message}");
                        }

                        var percentage = itemsToCheck.Count == 0 ? 100 : (int)Math.Round((double)scannedItems / itemsToCheck.Count * 100);
                        progressCallback?.Invoke(scannedItems, Math.Max(1, itemsToCheck.Count), $"Searching {displayName} for attachments | quick scan {percentage}% | {attachments.Count} attachments found");
                        
                        // Small delay to avoid API rate limits
                        await Task.Delay(25, cancellationToken); // Reduced from 50ms
                    }

                    progressCallback?.Invoke(Math.Max(1, itemsToCheck.Count), Math.Max(1, itemsToCheck.Count), $"Searching {displayName} for attachments | quick scan complete | {attachments.Count} attachments found");
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Limited scan complete: {attachments.Count} attachments found across {itemsWithAttachments} items");
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Error during limited attachment discovery for project {projectId}: {ex.Message}");
                }
                
                return attachments;
            });
        }

        /// <summary>
        /// Get all attachments for a project using cookbook-compliant approach:
        /// 1. Get all items in the project using abstract items endpoint
        /// 2. For each item, check for attachments using /items/{id}/attachments
        /// Based on: https://dev.jamasoftware.com/cookbook/ - REST Attachments section
        /// </summary>
        public async Task<List<JamaAttachment>> GetProjectAttachmentsAsync(int projectId, CancellationToken cancellationToken = default, Action<int, int, string>? progressCallback = null, string projectName = "")
        {
            return await WithRetryAsync(async () =>
            {
                await EnsureAccessTokenAsync();
                
                var attachments = new List<JamaAttachment>();
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Starting cookbook-compliant attachment discovery for project {projectId}");
                
                try 
                {
                    // Step 1: Get all items in the project using abstract items endpoint
                    // This is the correct approach according to the cookbook
                    var items = await GetAbstractItemsForProjectAsync(projectId, cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Found {items.Count} items in project {projectId}, checking each for attachments");
                    
                    // Step 2: For each item, check for attachments using the proper endpoint
                    var itemsWithAttachments = 0;
                    var currentItemIndex = 0;
                    var displayName = string.IsNullOrEmpty(projectName) ? $"Project {projectId}" : projectName;
                    
                    foreach (var item in items)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        currentItemIndex++;
                        var percentage = (int)((double)currentItemIndex / items.Count * 100);
                        
                        // Progress report with user-requested format
                        progressCallback?.Invoke(currentItemIndex, items.Count, $"Searching {displayName} for attachments | {percentage}% complete | {attachments.Count} attachments found");
                        
                        try
                        {
                            var itemAttachments = await GetItemAttachmentsAsync(item.Id, cancellationToken);
                            if (itemAttachments.Count > 0)
                            {
                                attachments.AddRange(itemAttachments);
                                itemsWithAttachments++;
                                
                                // Update progress with new attachment count
                                progressCallback?.Invoke(currentItemIndex, items.Count, $"Searching {displayName} for attachments | {percentage}% complete | {attachments.Count} attachments found");
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id} ({item.DocumentKey}) has {itemAttachments.Count} attachment(s) - Total: {attachments.Count}");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed to check attachments for item {item.Id}: {ex.Message}");
                        }
                        
                        // Add small delay to avoid overwhelming the API
                        await Task.Delay(50, cancellationToken);
                    }
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Attachment discovery complete: {attachments.Count} attachments found across {itemsWithAttachments} items");
                }
                catch (OperationCanceledException)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Attachment discovery cancelled for project {projectId}");
                    throw;
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Error during attachment discovery for project {projectId}: {ex.Message}");
                }
                
                return attachments;
            });
        }
        
        /// <summary>
        /// Get abstract items for a project using the recommended cookbook approach
        /// Uses /abstractitems endpoint with project filtering
        /// </summary>
        private async Task<List<JamaAbstractItem>> GetAbstractItemsForProjectAsync(int projectId, CancellationToken cancellationToken = default)
        {
            var items = new List<JamaAbstractItem>();
            var startAt = 0;
            const int maxResults = 50; // Reasonable batch size per cookbook guidance
            var hasMore = true;
            
            while (hasMore)
            {
                try
                {
                    var url = $"{_baseUrl}/rest/v1/abstractitems?project={projectId}&startAt={startAt}&maxResults={maxResults}";
                    var response = await _httpClient.GetAsync(url, cancellationToken);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken);
                        var result = JsonSerializer.Deserialize<JamaAbstractItemsResponse>(json, new JsonSerializerOptions 
                        { 
                            PropertyNameCaseInsensitive = true 
                        });
                        
                        if (result?.Data != null && result.Data.Count > 0)
                        {
                            var matchingItems = result.Data
                                .Where(item => item.Project == projectId)
                                .ToList();

                            var mismatchedItems = result.Data
                                .Where(item => item.Project != projectId)
                                .ToList();

                            if (mismatchedItems.Count > 0)
                            {
                                var sampleMismatches = string.Join(", ", mismatchedItems
                                    .Take(5)
                                    .Select(item => $"{item.DocumentKey} (item {item.Id}, project {item.Project})"));

                                TestCaseEditorApp.Services.Logging.Log.Warn(
                                    $"[JamaConnect] Abstract items endpoint returned {mismatchedItems.Count} item(s) outside requested project {projectId}; ignoring them. Sample: {sampleMismatches}");
                            }

                            items.AddRange(matchingItems);
                            startAt += result.Data.Count;
                            hasMore = result.Meta?.PageInfo?.TotalResults > startAt;
                        }
                        else
                        {
                            hasMore = false;
                        }
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Abstract items request failed: {response.StatusCode}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, "[JamaConnect] Error fetching abstract items");
                    break;
                }
            }
            
            return items;
        }
        
        /// <summary>
        /// Get attachments for a specific item using the cookbook-recommended endpoint:
        /// GET /rest/v1/items/{id}/attachments
        /// </summary>
        private async Task<List<JamaAttachment>> GetItemAttachmentsAsync(int itemId, CancellationToken cancellationToken = default)
        {
            var attachments = new List<JamaAttachment>();
            var startAt = 0;
            const int maxResults = 50;

            try
            {
                while (true)
                {
                    var url = $"{_baseUrl}/rest/v1/items/{itemId}/attachments?startAt={startAt}&maxResults={maxResults}";
                    var response = await _httpClient.GetAsync(url, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken);
                        var result = JsonSerializer.Deserialize<JamaAttachmentsResponse>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        var pageData = result?.Data ?? new List<JamaAttachment>();
                        if (pageData.Count == 0)
                        {
                            break;
                        }

                        attachments.AddRange(pageData);

                        var resultCount = result?.Meta?.PageInfo?.ResultCount ?? pageData.Count;
                        var totalResults = result?.Meta?.PageInfo?.TotalResults ?? attachments.Count;
                        startAt += resultCount;

                        if (startAt >= totalResults || resultCount <= 0)
                        {
                            break;
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // Item has no attachments or endpoint not supported.
                        break;
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed to get attachments for item {itemId} (startAt={startAt}): {response.StatusCode} - {errorContent}");
                        break;
                    }
                }

                return attachments;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Exception getting attachments for item {itemId}: {ex.Message}");
                return attachments;
            }
        }

        /// <summary>
        /// Download attachment by ID
        /// </summary>
        public async Task<byte[]?> DownloadAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default)
        {
            return await WithRetryAsync(async () =>
            {
                await EnsureAccessTokenAsync();
                
                var url = $"{_baseUrl}/rest/v1/attachments/{attachmentId}/file";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    
                    // 🎯 AUTO-REFRESH EXPIRED TOKENS
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && 
                        errorContent.Contains("token is expired"))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Token expired, refreshing and retrying attachment {attachmentId}");
                        
                        if (await RefreshExpiredTokenAsync())
                        {
                            // Retry the request with fresh token
                            response = await _httpClient.GetAsync(url, cancellationToken);
                            if (response.IsSuccessStatusCode)
                            {
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] ✅ Downloaded attachment {attachmentId} after token refresh");
                                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
                            }
                        }
                    }
                    
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed to download attachment {attachmentId}: {response.StatusCode} - {errorContent}");
                    return null;
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Downloaded attachment {attachmentId}");
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            });
        }

        /// <summary>
        /// Search items using Abstract Items endpoint for advanced filtering
        /// Based on cookbook pattern for searching with Lucene syntax
        /// </summary>
        public async Task<List<JamaItem>> SearchAbstractItemsAsync(
            int projectId,
            string? contains = null,
            int maxResults = 20,
            List<string>? includeParams = null)
        {
            return await WithRetryAsync(async () =>
            {
                await EnsureAccessTokenAsync();
                
                var query = new StringBuilder($"{_baseUrl}/rest/v1/abstractitems");
                query.Append($"?project={projectId}");
                
                if (!string.IsNullOrEmpty(contains))
                {
                    query.Append($"&contains={Uri.EscapeDataString(contains)}");
                }
                
                query.Append($"&maxResults={maxResults}");
                
                if (includeParams?.Count > 0)
                {
                    query.Append($"&include={string.Join(",", includeParams)}");
                }
                
                var response = await _httpClient.GetAsync(query.ToString());
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Abstract items endpoint failed with {response.StatusCode}: {errorContent}");
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Falling back to standard items endpoint");
                        // Fallback to standard items endpoint
                        var fallbackUrl = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults={maxResults}";
                        var fallbackResponse = await _httpClient.GetAsync(fallbackUrl);
                        fallbackResponse.EnsureSuccessStatusCode();
                        
                        var fallbackJson = await fallbackResponse.Content.ReadAsStringAsync();
                        var fallbackResult = JsonSerializer.Deserialize<JamaItemsResponse>(fallbackJson, new JsonSerializerOptions 
                        { 
                            PropertyNameCaseInsensitive = true 
                        });
                        
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Fallback search found {fallbackResult?.Data?.Count ?? 0} items");
                        return fallbackResult?.Data ?? new List<JamaItem>();
                    }
                    
                    response.EnsureSuccessStatusCode();
                }
                
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JamaItemsResponse>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Abstract search found {result?.Data?.Count ?? 0} items");
                return result?.Data ?? new List<JamaItem>();
            });
        }

        /// <summary>
        /// Get activities for tracking changes - based on cookbook change detection pattern
        /// </summary>
        public async Task<List<JamaActivity>> GetActivitiesAsync(int projectId, int maxResults = 20)
        {
            return await WithRetryAsync(async () =>
            {
                await EnsureAccessTokenAsync();
                
                var url = $"{_baseUrl}/rest/v1/activities?project={projectId}&maxResults={maxResults}";
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Activities endpoint failed with {response.StatusCode}: {errorContent}");
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Activities endpoint not supported - returning empty list");
                        return new List<JamaActivity>();
                    }
                    
                    response.EnsureSuccessStatusCode();
                }
                
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JamaActivityListResponse>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Retrieved {result?.Data?.Count ?? 0} activities");
                return result?.Data ?? new List<JamaActivity>();
            });
        }

        /// <summary>
        /// Get requirements with include parameters for efficient API usage
        /// Based on cookbook pattern for reducing API calls
        /// </summary>
        public async Task<List<JamaItem>> GetRequirementsWithIncludesAsync(
            int projectId,
            List<string> includeParams,
            CancellationToken cancellationToken = default)
        {
            return await WithRetryAsync(async () =>
            {
                await EnsureAccessTokenAsync();
                
                var includes = string.Join(",", includeParams);
                var url = $"{_baseUrl}/rest/v1/items?project={projectId}&include={includes}&maxResults=50";
                
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Enhanced requirements endpoint failed with {response.StatusCode}: {errorContent}");
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Falling back to basic items endpoint");
                        // Fallback to basic endpoint without include parameters
                        var fallbackUrl = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults=50";
                        var fallbackResponse = await _httpClient.GetAsync(fallbackUrl, cancellationToken);
                        fallbackResponse.EnsureSuccessStatusCode();
                        
                        var fallbackJson = await fallbackResponse.Content.ReadAsStringAsync(cancellationToken);
                        var fallbackResult = JsonSerializer.Deserialize<JamaItemsResponse>(fallbackJson, new JsonSerializerOptions 
                        { 
                            PropertyNameCaseInsensitive = true 
                        });
                        
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Fallback found {fallbackResult?.Data?.Count ?? 0} items");
                        return fallbackResult?.Data ?? new List<JamaItem>();
                    }
                    
                    response.EnsureSuccessStatusCode();
                }
                
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<JamaItemsResponse>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Retrieved {result?.Data?.Count ?? 0} items with includes: {includes}");
                return result?.Data ?? new List<JamaItem>();
            });
        }



        /// <summary>
        /// Retry wrapper with rate limiting - based on cookbook throttling pattern
        /// </summary>
        private async Task<T> WithRetryAsync<T>(Func<Task<T>> operation, int maxRetries = 3)
        {
            await _rateLimitSemaphore.WaitAsync();
            
            try
            {
                for (int attempt = 0; attempt < maxRetries; attempt++)
                {
                    try
                    {
                        return await operation();
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("429") && attempt < maxRetries - 1)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Rate limited, retrying in {delay.TotalSeconds}s (attempt {attempt + 1}/{maxRetries})");
                        await Task.Delay(delay);
                    }
                }
                
                // Final attempt without catch
                return await operation();
            }
            finally
            {
                // Release rate limit after a short delay
                _ = Task.Delay(100).ContinueWith(_ => _rateLimitSemaphore.Release());
            }
        }

        /// <summary>
        /// Extract attachment URLs from rich text fields - based on cookbook file download pattern
        /// </summary>
        public List<string> ExtractAttachmentUrls(string richTextContent)
        {
            var urls = new List<string>();
            
            if (string.IsNullOrEmpty(richTextContent))
                return urls;
            
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(richTextContent);
                
                // Find images with src attributes
                var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
                if (imgNodes != null)
                {
                    foreach (var img in imgNodes)
                    {
                        var src = img.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(src) && src.Contains("attachment"))
                        {
                            urls.Add(src);
                        }
                    }
                }
                
                // Find links with href attributes pointing to attachments
                var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
                if (linkNodes != null)
                {
                    foreach (var link in linkNodes)
                    {
                        var href = link.GetAttributeValue("href", "");
                        if (!string.IsNullOrEmpty(href) && href.Contains("attachment"))
                        {
                            urls.Add(href);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[JamaConnect] Error parsing rich text for attachments");
            }
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Extracted {urls.Count} attachment URLs from rich text");
            return urls;
        }

        /// <summary>
        /// Extract text from images found in HTML content using OCR
        /// </summary>
        private async Task ExtractTextFromImagesInContentAsync(string htmlContent, int itemId, string fieldName, RequirementLooseContent looseContent)
        {
            if (_ocrService == null || string.IsNullOrWhiteSpace(htmlContent))
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Skipping OCR - OCR Service: {(_ocrService != null ? "Available" : "Null")}, Content Length: {htmlContent?.Length ?? 0}");
                return;
            }

            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Scanning field '{fieldName}' for images to OCR");

                // Parse HTML to find images
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);
                
                var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
                if (imgNodes == null || !imgNodes.Any())
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: No images found in field '{fieldName}' HTML content");
                    
                    // Log the HTML content for debugging (truncated)
                    var truncatedHtml = htmlContent.Length > 500 ? htmlContent.Substring(0, 500) + "..." : htmlContent;
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: HTML content for '{fieldName}' - {truncatedHtml}");
                    
                    return;
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Found {imgNodes.Count} images in field '{fieldName}' for OCR processing");

                foreach (var imgNode in imgNodes)
                {
                    var src = imgNode.GetAttributeValue("src", "");
                    if (string.IsNullOrEmpty(src))
                    {
                        continue;
                    }

                    try
                    {
                        // Download and process the image
                        var ocrResult = await ProcessImageForOCRAsync(src, itemId);
                        if (ocrResult != null && ocrResult.Success && !string.IsNullOrWhiteSpace(ocrResult.ExtractedText))
                        {
                            var extractedImageText = new ExtractedImageText
                            {
                                ImageSource = src,
                                ExtractedText = $"⚠️ OCR-extracted text (please verify accuracy): {ocrResult.ExtractedText}",
                                Confidence = ocrResult.Confidence
                            };

                            // Convert OCR table data to LooseTable format
                            if (ocrResult.TableData.Any())
                            {
                                var ocrTable = new LooseTable
                                {
                                    EditableTitle = "⚠️ OCR was used to capture data - please verify accuracy"
                                };

                                // Use first row as headers if it looks like headers
                                if (ocrResult.TableData.Count > 1)
                                {
                                    ocrTable.ColumnHeaders = ocrResult.TableData[0];
                                    ocrTable.Rows = ocrResult.TableData.Skip(1).ToList();
                                }
                                else if (ocrResult.TableData.Count == 1)
                                {
                                    // Single row - treat as data without headers
                                    ocrTable.ColumnHeaders = ocrResult.TableData[0].Select((_, index) => $"Column {index + 1}").ToList();
                                    ocrTable.Rows = new List<List<string>> { ocrResult.TableData[0] };
                                }

                                extractedImageText.DetectedTables.Add(ocrTable);
                                
                                // Also add to main tables collection
                                looseContent.Tables.Add(ocrTable);
                                
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: OCR extracted table with {ocrTable.Rows.Count} rows from image in '{fieldName}'");
                            }

                            looseContent.ExtractedImageTexts.Add(extractedImageText);
                            
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: OCR extracted {ocrResult.ExtractedText.Length} characters from image in '{fieldName}' (confidence: {ocrResult.Confidence:P1})");
                        }
                    }
                    catch (Exception imgEx)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Item {itemId}: Failed to process image '{src}' in field '{fieldName}': {imgEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Item {itemId}: Error during OCR processing for field '{fieldName}'");
            }
        }

        /// <summary>
        /// Download and process a single image for OCR text extraction
        /// </summary>
        private async Task<OCRResult?> ProcessImageForOCRAsync(string imageUrl, int itemId)
        {
            if (_ocrService == null || string.IsNullOrEmpty(imageUrl))
            {
                return null;
            }

            try
            {
                // Handle different image URL formats and authentication
                string downloadUrl = await PrepareImageDownloadUrlAsync(imageUrl, itemId);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Item {itemId}: Could not prepare download URL for: {imageUrl}");
                    return null;
                }
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Downloading image from: {downloadUrl}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Original image URL: {imageUrl}");

                // Create a new request with proper authentication headers
                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                
                // Try multiple authentication strategies for image download
                var downloadResult = await DownloadImageWithRetryAsync(downloadUrl, itemId);
                if (downloadResult == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Item {itemId}: Failed to download image after all retry attempts: {downloadUrl}");
                    return null;
                }

                var (imageData, contentType) = downloadResult.Value;
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Downloaded image - Size: {imageData.Length} bytes, Content-Type: {contentType}");
                
                // Check if we got HTML instead of image data (authentication failure)
                if (contentType.Contains("text/html") || 
                    (imageData.Length > 10 && Encoding.UTF8.GetString(imageData.Take(100).ToArray()).Contains("<html")))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Item {itemId}: Received HTML content instead of image - likely authentication issue");
                    var htmlPreview = Encoding.UTF8.GetString(imageData.Take(200).ToArray());
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: HTML preview: {htmlPreview}");
                    return null;
                }
                
                // Validate image format by checking magic bytes
                string formatDetected = DetectImageFormat(imageData);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Detected image format: {formatDetected} (starts with: {string.Join(" ", imageData.Take(8).Select(b => b.ToString("X2")))})");
                
                // Verify it's actually an image format we can process
                if (formatDetected.Contains("Unknown"))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Item {itemId}: Downloaded content does not appear to be a valid image format");
                    return null;
                }

                // Extract text using OCR
                var fileName = Path.GetFileName(imageUrl) ?? "image.png";
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: About to call OCR with {imageData.Length} bytes for file: {fileName}");
                var ocrResult = await _ocrService.ExtractTextFromImageAsync(imageData, fileName);
                
                return ocrResult;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Item {itemId}: Failed to download/process image: {imageUrl}");
                return null;
            }
        }

        /// <summary>
        /// Download image data with retry strategies for different authentication approaches
        /// </summary>
        private async Task<(byte[] data, string contentType)?> DownloadImageWithRetryAsync(string downloadUrl, int itemId)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Starting download retry sequence for: {downloadUrl}");
            
            // Strategy 1: Try session-based authentication (best for attachments)
            TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Attempting session-based authentication...");
            var result = await TryDownloadImageWithSessionAsync(downloadUrl, itemId);
            if (result != null) 
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: ✅ Session-based download succeeded");
                return result;
            }
            TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Session-based authentication failed, trying OAuth/API Token...");
            
            // Strategy 2: Standard OAuth/API token authentication
            result = await TryDownloadImageAsync(downloadUrl, itemId, "OAuth/API Token");
            if (result != null) return result;
            
            // Strategy 3: Try without authentication (public endpoints)
            result = await TryDownloadImageAsync(downloadUrl, itemId, "No Auth", useAuth: false);
            if (result != null) return result;
            
            // Strategy 4: Try with basic authentication if we have username/password
            if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
            {
                result = await TryDownloadImageAsync(downloadUrl, itemId, "Basic Auth", useBasicAuth: true);
                if (result != null) return result;
            }
            
            TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Item {itemId}: All download strategies failed");
            return null;
        }

        /// <summary>
        /// Try downloading an image with specific authentication strategy
        /// </summary>
        private async Task<(byte[] data, string contentType)?> TryDownloadImageAsync(string downloadUrl, int itemId, string strategy, bool useAuth = true, bool useBasicAuth = false)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Trying download strategy '{strategy}' for: {downloadUrl}");
                
                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                
                if (useAuth)
                {
                    if (useBasicAuth && !string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
                    {
                        // Use basic authentication
                        var authBytes = Encoding.UTF8.GetBytes($"{_username}:{_password}");
                        var authHeader = Convert.ToBase64String(authBytes);
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
                    }
                    else
                    {
                        // Ensure OAuth/API token authentication
                        if (!await EnsureAuthenticationAsync())
                        {
                            TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Authentication failed for strategy '{strategy}'");
                            return null;
                        }
                        
                        // Copy authentication headers from the main HttpClient
                        if (_httpClient.DefaultRequestHeaders.Authorization != null)
                        {
                            request.Headers.Authorization = _httpClient.DefaultRequestHeaders.Authorization;
                        }
                    }
                }
                
                // Add Accept headers for images
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

                // Download the image
                var response = await _httpClient.SendAsync(request);
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Strategy '{strategy}' response: {response.StatusCode}");
                
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Strategy '{strategy}' got NotFound (404) - trying next strategy");
                    }
                    else
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Strategy '{strategy}' failed: {response.StatusCode} - {TruncateForLog(responseContent)}");
                    }
                    return null;
                }

                var imageData = await response.Content.ReadAsByteArrayAsync();
                if (imageData.Length == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Strategy '{strategy}' returned empty data");
                    return null;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: ✅ Strategy '{strategy}' succeeded - Size: {imageData.Length} bytes, Content-Type: {contentType}");
                
                return (imageData, contentType);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Strategy '{strategy}' exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Prepare the correct download URL for Jama attachments
        /// </summary>
        private async Task<string> PrepareImageDownloadUrlAsync(string imageUrl, int itemId)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Preparing download URL for: {imageUrl}");
                
                // Handle different image URL formats
                string downloadUrl = imageUrl;
                
                // If it's a relative URL, make it absolute
                if (imageUrl.StartsWith("/") || imageUrl.StartsWith("./"))
                {
                    downloadUrl = $"{_baseUrl.TrimEnd('/')}{imageUrl}";
                }
                
                // Handle Jama-specific attachment URLs with multiple format attempts
                var attachmentMatch = System.Text.RegularExpressions.Regex.Match(downloadUrl, @"attachment/(\d+)");
                if (attachmentMatch.Success)
                {
                    var attachmentId = attachmentMatch.Groups[1].Value;
                    
                    // Try multiple URL formats for Jama attachments
                    var urlCandidates = new List<string>
                    {
                        // Original attachment URL (sometimes works with authentication)
                        downloadUrl,
                        
                        // REST API attachment endpoint v1
                        $"{_baseUrl.TrimEnd('/')}/rest/v1/attachments/{attachmentId}/file",
                        
                        // REST API attachment endpoint latest
                        $"{_baseUrl.TrimEnd('/')}/rest/latest/attachments/{attachmentId}/file",
                        
                        // Direct attachment download without API prefix  
                        $"{_baseUrl.TrimEnd('/')}/attachments/{attachmentId}/file",
                        
                        // Alternative attachment download endpoint
                        $"{_baseUrl.TrimEnd('/')}/files/attachment/{attachmentId}",
                    };
                    
                    // Test each URL format to find working one
                    foreach (var candidateUrl in urlCandidates)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Testing attachment URL: {candidateUrl}");
                        
                        if (await TestAttachmentUrlAsync(candidateUrl, itemId))
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: ✅ Working attachment URL found: {candidateUrl}");
                            return candidateUrl;
                        }
                    }
                    
                    // All URL formats failed, log for debugging and return first REST API format
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Item {itemId}: ⚠️ All attachment URL formats failed for attachment ID: {attachmentId}");
                    return urlCandidates[1]; // Return REST API v1 format as fallback
                }
                
                return downloadUrl;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Item {itemId}: Error preparing download URL for: {imageUrl}");
                return imageUrl; // Fallback to original URL
            }
        }

        /// <summary>
        /// Test if an attachment URL is accessible with current authentication
        /// </summary>
        private async Task<bool> TestAttachmentUrlAsync(string url, int itemId)
        {
            try
            {
                // Create a HEAD request to test URL availability without downloading content
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                
                // Ensure authentication
                if (!await EnsureAuthenticationAsync())
                {
                    return false;
                }
                
                // Copy authentication headers from the main HttpClient
                if (_httpClient.DefaultRequestHeaders.Authorization != null)
                {
                    request.Headers.Authorization = _httpClient.DefaultRequestHeaders.Authorization;
                }
                
                // Add Accept headers for images
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                
                using var response = await _httpClient.SendAsync(request);
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: URL test result for {url}: {response.StatusCode}");
                
                // Consider successful if we get OK or any 2xx response
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: URL test failed for {url}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Ensure authentication is valid for the current request
        /// </summary>
        private async Task<bool> EnsureAuthenticationAsync()
        {
            try
            {
                // For OAuth, ensure we have a valid access token
                if (_isOAuth)
                {
                    return await EnsureAccessTokenAsync();
                }
                
                // For basic auth and API token, authentication is in headers already
                return true;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[JamaConnect] Authentication check failed");
                return false;
            }
        }

        private static string TruncateForLog(string? value, int maxLength = 200)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength);
        }

        private string DetectImageFormat(byte[] imageData)
        {
            if (imageData?.Length < 4) return "Unknown (too small)";

            // Check PNG signature (89 50 4E 47 0D 0A 1A 0A)
            if (imageData.Length >= 8 && 
                imageData[0] == 0x89 && imageData[1] == 0x50 && imageData[2] == 0x4E && imageData[3] == 0x47 &&
                imageData[4] == 0x0D && imageData[5] == 0x0A && imageData[6] == 0x1A && imageData[7] == 0x0A)
                return "PNG";

            // Check JPEG signature (FF D8 FF)
            if (imageData.Length >= 3 && imageData[0] == 0xFF && imageData[1] == 0xD8 && imageData[2] == 0xFF)
                return "JPEG";

            // Check GIF signature (GIF87a or GIF89a)
            if (imageData.Length >= 6)
            {
                var gifHeader = Encoding.ASCII.GetString(imageData.Take(6).ToArray());
                if (gifHeader == "GIF87a" || gifHeader == "GIF89a")
                    return "GIF";
            }

            // Check BMP signature (BM)
            if (imageData.Length >= 2 && imageData[0] == 0x42 && imageData[1] == 0x4D)
                return "BMP";

            // Check TIFF signatures (II*\0 or MM\0*)
            if (imageData.Length >= 4)
            {
                if ((imageData[0] == 0x49 && imageData[1] == 0x49 && imageData[2] == 0x2A && imageData[3] == 0x00) ||
                    (imageData[0] == 0x4D && imageData[1] == 0x4D && imageData[2] == 0x00 && imageData[3] == 0x2A))
                    return "TIFF";
            }

            // Check WebP signature (RIFF....WEBP)
            if (imageData.Length >= 12)
            {
                var riffHeader = Encoding.ASCII.GetString(imageData.Take(4).ToArray());
                var webpHeader = Encoding.ASCII.GetString(imageData.Skip(8).Take(4).ToArray());
                if (riffHeader == "RIFF" && webpHeader == "WEBP")
                    return "WebP";
            }

            // Check for HTML content (authentication errors)
            if (imageData.Length >= 10)
            {
                var textStart = Encoding.UTF8.GetString(imageData.Take(20).ToArray()).ToLower();
                if (textStart.Contains("<html") || textStart.Contains("<!doc"))
                    return "HTML (not an image)";
            }

            // Check for text content (all printable ASCII or newlines)
            if (imageData.Length >= 20)
            {
                bool allTextLike = imageData.Take(20).All(b => (b >= 32 && b <= 126) || b == 0x0A || b == 0x0D || b == 0x09);
                if (allTextLike)
                    return "Text (not an image)";
            }

            return $"Unknown (starts with: {string.Join(" ", imageData.Take(8).Select(b => b.ToString("X2")))})";
        }

        /// <summary>
        /// Try downloading image using session-based authentication (cookies)
        /// </summary>
        private async Task<(byte[] data, string contentType)?> TryDownloadImageWithSessionAsync(string downloadUrl, int itemId)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Trying session-based authentication for: {downloadUrl}");

                // First try the cookbook-compliant Files endpoint approach
                var filesEndpointResult = await TryDownloadImageViaFilesEndpointAsync(downloadUrl, itemId);
                if (filesEndpointResult != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: ✅ Files endpoint succeeded - Size: {filesEndpointResult.Value.data.Length} bytes, Content-Type: {filesEndpointResult.Value.contentType}");
                    return filesEndpointResult;
                }

                // Fallback to direct session authentication if Files endpoint fails
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Files endpoint failed, trying direct session authentication");

                // Ensure we have an authenticated session
                if (!await EnsureSessionAuthenticationAsync())
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Session authentication failed");
                    return null;
                }

                // Make request with session cookies
                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                
                // Add Accept headers for images
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                
                var response = await _sessionClient.SendAsync(request);
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Session authentication response: {response.StatusCode}");
                
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Session auth got NotFound (404)");
                    }
                    else
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Session auth failed: {response.StatusCode} - {TruncateForLog(responseContent)}");
                    }
                    return null;
                }

                var imageData = await response.Content.ReadAsByteArrayAsync();
                if (imageData.Length == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Session auth returned empty data");
                    return null;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                
                // Check if we got HTML instead of image data (authentication still failed)
                if (contentType.Contains("text/html") || 
                    (imageData.Length > 10 && Encoding.UTF8.GetString(imageData.Take(100).ToArray()).ToLower().Contains("<html")))
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Session auth still returning HTML - clearing session and will retry");
                    _sessionAuthenticated = false;
                    return null;
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: ✅ Session authentication succeeded - Size: {imageData.Length} bytes, Content-Type: {contentType}");
                
                return (imageData, contentType);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Session authentication exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Try downloading image using the cookbook-compliant Files endpoint
        /// Based on: GET /rest/v1/files?url=<file-url>
        /// </summary>
        private async Task<(byte[] data, string contentType)?> TryDownloadImageViaFilesEndpointAsync(string imageUrl, int itemId)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Trying Files endpoint for: {imageUrl}");

                // Ensure we have authentication
                if (!await EnsureAuthenticationAsync())
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Files endpoint authentication failed");
                    return null;
                }

                // Use the cookbook-compliant Files endpoint
                var filesEndpoint = $"{_baseUrl}/rest/v1/files?url={Uri.EscapeDataString(imageUrl)}";
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Using Files endpoint: {filesEndpoint}");

                using var request = new HttpRequestMessage(HttpMethod.Get, filesEndpoint);
                
                // Add Accept headers for images
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

                var response = await _httpClient.SendAsync(request);
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Files endpoint response: {response.StatusCode}");
                
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Files endpoint got NotFound (404)");
                    }
                    else
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Files endpoint failed: {response.StatusCode} - {TruncateForLog(responseContent)}");
                    }
                    return null;
                }

                var imageData = await response.Content.ReadAsByteArrayAsync();
                if (imageData.Length == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Files endpoint returned empty data");
                    return null;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                
                // Check if we got HTML instead of image data
                if (contentType.Contains("text/html") || 
                    (imageData.Length > 10 && Encoding.UTF8.GetString(imageData.Take(100).ToArray()).ToLower().Contains("<html")))
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Files endpoint returning HTML instead of image data");
                    return null;
                }
                
                return (imageData, contentType);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Files endpoint exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Ensure we have an authenticated session with Jama using web-based login
        /// </summary>
        private async Task<bool> EnsureSessionAuthenticationAsync()
        {
            if (_sessionAuthenticated)
            {
                return true; // Already authenticated
            }

            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Establishing session-based authentication for image downloads");

                // For username/password authentication, login through web interface
                if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
                {
                    var loginUrl = $"{_baseUrl}/perspective.req";
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Attempting web login at: {loginUrl}");

                    // First, get the login page to establish session and get any CSRF tokens
                    var loginPageResponse = await _sessionClient.GetAsync(loginUrl);
                    if (!loginPageResponse.IsSuccessStatusCode)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed to access login page: {loginPageResponse.StatusCode}");
                        return false;
                    }

                    var loginPageContent = await loginPageResponse.Content.ReadAsStringAsync();
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Got login page, length: {loginPageContent.Length}");

                    // Extract any CSRF tokens or other required form data
                    var loginFormUrl = $"{_baseUrl}/perspective.req#/login";
                    
                    // Try direct API authentication first (often works better than form submission)
                    _sessionClient.DefaultRequestHeaders.Clear();
                    var authBytes = Encoding.UTF8.GetBytes($"{_username}:{_password}");
                    var authHeader = Convert.ToBase64String(authBytes);
                    _sessionClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);

                    // Test authentication by accessing a protected resource
                    var testUrl = $"{_baseUrl}/rest/latest/projects";
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Testing session authentication with: {testUrl}");
                    
                    var testResponse = await _sessionClient.GetAsync(testUrl);
                    if (testResponse.IsSuccessStatusCode)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] ✅ Session authentication established successfully");
                        _sessionAuthenticated = true;
                        return true;
                    }

                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Session auth test failed: {testResponse.StatusCode}");
                }

                // For OAuth or API token, copy authentication to session client
                else if (!string.IsNullOrEmpty(_apiToken))
                {
                    _sessionClient.DefaultRequestHeaders.Clear();
                    _sessionClient.DefaultRequestHeaders.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_apiToken}:"))}");
                    
                    // Test the authentication
                    var testUrl = $"{_baseUrl}/rest/latest/projects";
                    var testResponse = await _sessionClient.GetAsync(testUrl);
                    if (testResponse.IsSuccessStatusCode)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] ✅ Session authentication established with API token");
                        _sessionAuthenticated = true;
                        return true;
                    }
                }

                // For OAuth, ensure we have access token and copy to session client
                else if (_isOAuth && await EnsureAccessTokenAsync())
                {
                    _sessionClient.DefaultRequestHeaders.Clear();
                    _sessionClient.DefaultRequestHeaders.Authorization = _httpClient.DefaultRequestHeaders.Authorization;
                    
                    // Test the authentication
                    var testUrl = $"{_baseUrl}/rest/latest/projects";
                    var testResponse = await _sessionClient.GetAsync(testUrl);
                    if (testResponse.IsSuccessStatusCode)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] ✅ Session authentication established with OAuth");
                        _sessionAuthenticated = true;
                        return true;
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Warn("[JamaConnect] Failed to establish session authentication");
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[JamaConnect] Error during session authentication");
                return false;
            }
        }

        /// <summary>
        /// Get test case item type ID for a project
        /// </summary>
        public async Task<(bool Success, int? TestCaseItemType)> GetTestCaseItemTypeAsync(int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureAccessTokenAsync();
                
                var url = $"{_baseUrl}/rest/v1/projects/{projectId}/itemtypes";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Fetching item types for project {projectId}: {url}");
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Failed to get item types. Status: {response.StatusCode}");
                    return (false, null);
                }
                
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item types response: {json}");
                var result = JsonSerializer.Deserialize<JsonDocument>(json);
                
                if (result?.RootElement.TryGetProperty("data", out var data) == true && data.ValueKind == JsonValueKind.Array)
                {
                    var itemTypes = new List<(int id, string display, string? typeKey)>();
                    
                    // First pass: collect all item types for logging
                    foreach (var itemType in data.EnumerateArray())
                    {
                        var id = itemType.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                        var display = itemType.TryGetProperty("display", out var displayProp) ? displayProp.GetString() : "";
                        var typeKey = itemType.TryGetProperty("typeKey", out var keyProp) ? keyProp.GetString() : "";
                        
                        itemTypes.Add((id, display ?? "", typeKey));
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Found item type: ID={id}, Display='{display}', Key='{typeKey}'");
                    }
                    
                    // Second pass: try to find best test case type match
                    // 🚨 CRITICAL: For enterprise projects like RTU4220, prioritize Verification Case over Test Case
                    var testCasePatterns = new[]
                    {
                        "Verification Case",            // 🎯 PRIORITIZED: Enterprise/Rockwell Collins pattern
                        "VerificationCase",
                        "Verification_Case", 
                        "VerCse",                      // Key pattern from RTU4220
                        "Test Case",                    // Standard test case (lower priority for enterprise)
                        "TestCase", 
                        "Test_Case",
                        "test case",
                        "testcase",
                        "Verification",                 // Broader verification pattern
                        "Test",                         // Generic test
                        "Test Plan",                    // Sometimes used for test cases
                        "Test Step",                    // Another possibility  
                        "TC",                          // Abbreviation
                        "T"                            // Last resort - single letter
                    };
                    
                    foreach (var pattern in testCasePatterns)
                    {
                        var matchingType = itemTypes.FirstOrDefault(t => 
                            t.display.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                            t.typeKey?.Equals(pattern, StringComparison.OrdinalIgnoreCase) == true);
                            
                        if (matchingType.id > 0)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Selected test case item type: ID={matchingType.id}, Display='{matchingType.display}', Pattern='{pattern}'");
                            return (true, matchingType.id);
                        }
                    }
                    
                    // Fallback: look for anything containing "test" (case insensitive)  
                    var testContainingType = itemTypes.FirstOrDefault(t => 
                        t.display.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                        t.typeKey?.Contains("test", StringComparison.OrdinalIgnoreCase) == true);
                        
                    if (testContainingType.id > 0)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Using fallback test-containing item type: ID={testContainingType.id}, Display='{testContainingType.display}'");
                        return (true, testContainingType.id);
                    }
                    
                    // Last resort: use first available item type (if any)
                    if (itemTypes.Count > 0)
                    {
                        var firstType = itemTypes.First();
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] No test-specific item type found. Using first available: ID={firstType.id}, Display='{firstType.display}'");
                        return (true, firstType.id);
                    }
                }
                
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] No item types found in project {projectId}");
                return (false, null);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Error getting test case item type: {ex.Message}");
                return (false, null);
            }
        }

        /// <summary>
        /// Get requirement item type ID for a project
        /// </summary>
        public async Task<(bool Success, int? RequirementItemType)> GetRequirementItemTypeAsync(int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                lock (_requirementItemTypeCache)
                {
                    if (_requirementItemTypeCache.TryGetValue(projectId, out var cachedTypeId))
                    {
                        return (true, cachedTypeId);
                    }
                }

                await EnsureAccessTokenAsync();

                var url = $"{_baseUrl}/rest/v1/projects/{projectId}/itemtypes";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Fetching requirement item types for project {projectId}: {url}");
                var response = await _httpClient.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Failed to get requirement item types. Status: {response.StatusCode}");
                    return (false, null);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<JsonDocument>(json);

                if (result?.RootElement.TryGetProperty("data", out var data) == true && data.ValueKind == JsonValueKind.Array)
                {
                    var itemTypes = new List<(int id, string display, string? typeKey)>();

                    foreach (var itemType in data.EnumerateArray())
                    {
                        var id = itemType.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                        var display = itemType.TryGetProperty("display", out var displayProp) ? displayProp.GetString() : "";
                        var typeKey = itemType.TryGetProperty("typeKey", out var keyProp) ? keyProp.GetString() : "";
                        itemTypes.Add((id, display ?? string.Empty, typeKey));
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Found requirement item type candidate: ID={id}, Display='{display}', Key='{typeKey}'");
                    }

                    var requirementPatterns = new[]
                    {
                        "Requirement",
                        "Requirements",
                        "System Requirement",
                        "System Requirements",
                        "Software Requirement",
                        "Hardware Requirement",
                        "User Requirement",
                        "Req"
                    };

                    // Prefer the known Jama requirement item type ID when present.
                    var knownRequirementType = itemTypes.FirstOrDefault(t => t.id == 193);
                    if (knownRequirementType.id > 0)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Selected known requirement item type ID 193: Display='{knownRequirementType.display}', Key='{knownRequirementType.typeKey}'");
                        lock (_requirementItemTypeCache)
                        {
                            _requirementItemTypeCache[projectId] = knownRequirementType.id;
                        }
                        return (true, knownRequirementType.id);
                    }

                    foreach (var pattern in requirementPatterns)
                    {
                        var matchingType = itemTypes.FirstOrDefault(t =>
                            t.display.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                            t.typeKey?.Equals(pattern, StringComparison.OrdinalIgnoreCase) == true);

                        if (matchingType.id > 0)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Selected requirement item type: ID={matchingType.id}, Display='{matchingType.display}', Pattern='{pattern}'");
                            lock (_requirementItemTypeCache)
                            {
                                _requirementItemTypeCache[projectId] = matchingType.id;
                            }
                            return (true, matchingType.id);
                        }
                    }

                    var requirementContainingType = itemTypes.FirstOrDefault(t =>
                        t.display.Contains("requirement", StringComparison.OrdinalIgnoreCase) ||
                        t.typeKey?.Contains("requirement", StringComparison.OrdinalIgnoreCase) == true);

                    if (requirementContainingType.id > 0)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Using fallback requirement-containing item type: ID={requirementContainingType.id}, Display='{requirementContainingType.display}'");
                        lock (_requirementItemTypeCache)
                        {
                            _requirementItemTypeCache[projectId] = requirementContainingType.id;
                        }
                        return (true, requirementContainingType.id);
                    }

                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] No requirement item type found. Refusing to create requirements with ambiguous type mapping.");
                    return (false, null);
                }

                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] No item types found for requirements in project {projectId}");
                return (false, null);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Error getting requirement item type: {ex.Message}");
                return (false, null);
            }
        }

        /// <summary>
        /// Parse the component (Systems, Hardware, Software) from a requirement's location information
        /// Enhanced to handle hierarchical path structures like "C1XMB2437-1N RTU-4220/Systems/Requirements/General Systems"
        /// </summary>
        private string ParseComponentFromRequirement(Requirement requirement)
        {
            try
            {
                var locations = new[]
                {
                    requirement.FolderPath,
                    requirement.SetName,
                    requirement.ItemPath,
                    requirement.Project,
                    requirement.GlobalId,  // Add Global ID for additional context
                    requirement.Item,      // Add Item ID for additional context
                    requirement.Name       // Add Name for additional context
                }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                foreach (var location in locations)
                {
                    var lowerLocation = location!.ToLowerInvariant();
                    
                    // Enhanced parsing for hierarchical paths like "RTU-4220/Systems/Requirements/General Systems"
                    // Check for path components separated by slashes
                    if (location.Contains("/") || location.Contains("\\"))
                    {
                        var pathParts = location.Split('/', '\\').Select(p => p.Trim()).ToArray();
                        foreach (var part in pathParts)
                        {
                            var lowerPart = part.ToLowerInvariant();
                            
                            // Check for exact component matches in path structure
                            if (lowerPart == "systems" && !lowerPart.Contains("subsystem"))
                            {
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Parsed component 'Systems' from path part: {part} (full path: {location})");
                                return "Systems";
                            }
                            
                            if (lowerPart == "hardware" || lowerPart == "hw")
                            {
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Parsed component 'Hardware' from path part: {part} (full path: {location})");
                                return "Hardware";
                            }
                            
                            if (lowerPart == "software" || lowerPart == "sw")
                            {
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Parsed component 'Software' from path part: {part} (full path: {location})");
                                return "Software";
                            }
                        }
                    }
                    
                    // Check for Systems component (broader pattern matching)
                    if (lowerLocation.Contains("system") && !lowerLocation.Contains("subsystem"))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Parsed component 'Systems' from location: {location}");
                        return "Systems";
                    }
                    
                    // Check for Hardware component
                    if (lowerLocation.Contains("hardware") || lowerLocation.Contains("hw") || 
                        lowerLocation.Contains("electronic") || lowerLocation.Contains("mechanical"))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Parsed component 'Hardware' from location: {location}");
                        return "Hardware";
                    }
                    
                    // Check for Software component  
                    if (lowerLocation.Contains("software") || lowerLocation.Contains("sw") ||
                        lowerLocation.Contains("application") || lowerLocation.Contains("firmware"))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Parsed component 'Software' from location: {location}");
                        return "Software";
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Could not parse component from requirement locations: {string.Join(", ", locations)}");
                
                // For enterprise projects like RTU4220, default to Systems as most test cases are system-level
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Defaulting to 'Systems' component for enterprise project");
                return "Systems"; 
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Error parsing component from requirement: {ex.Message}");
                return "Systems"; // Default fallback
            }
        }

        /// <summary>
        /// Find existing verification cases set in the specified component with pagination and path-aware search
        /// </summary>
        private async Task<(bool Success, int? ContainerId, string Message)> FindVerificationCasesSetInComponentAsync(int projectId, string component, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureAccessTokenAsync();
                
                var candidateSets = new List<(int Id, string Name, int Priority, string Path)>();
                var startIndex = 0;
                var batchSize = 50;
                bool hasMoreResults = true;
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Searching for verification cases set in {component} component (with pagination and path analysis)");
                
                // Paginate through all project items to build a complete picture
                while (hasMoreResults)
                {
                    var url = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults={batchSize}&startAt={startIndex}";
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Fetching items batch: startAt={startIndex}, maxResults={batchSize}");
                    
                    var response = await _httpClient.GetAsync(url, cancellationToken);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                        TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Failed to get project items batch {startIndex}: {response.StatusCode} - {errorContent}");
                        break;
                    }
                    
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var document = JsonDocument.Parse(content);
                    
                    var meta = document.RootElement.GetProperty("meta");
                    var pageInfo = meta.GetProperty("pageInfo");
                    var resultCount = pageInfo.GetProperty("resultCount").GetInt32();
                    var totalResults = pageInfo.GetProperty("totalResults").GetInt32();
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Batch results: {resultCount} items, total available: {totalResults}");
                    
                    var data = document.RootElement.GetProperty("data");
                    
                    // Analyze Sets in this batch
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("itemType", out var itemTypeElement) && 
                            item.TryGetProperty("id", out var idElement) &&
                            item.TryGetProperty("fields", out var fieldsElement) &&
                            fieldsElement.TryGetProperty("name", out var nameElement))
                        {
                            var itemTypeId = itemTypeElement.GetInt32();
                            var id = idElement.GetInt32();
                            var name = nameElement.GetString() ?? "";
                            var lowerName = name.ToLowerInvariant();
                            var lowerComponent = component.ToLowerInvariant();
                            
                            if (itemTypeId == 54) // Only consider Sets
                            {
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Examining Set: ID={id}, Name='{name}'");
                                
                                var priority = 0;
                                var pathDescription = "";
                                
                                // HIGHEST priority: Component-specific verification cases with system structure
                                // Look for "General Systems" or similar under Component/Verification Cases/
                                if ((name.Equals("General Systems", StringComparison.OrdinalIgnoreCase) ||
                                     name.Equals($"General {component}", StringComparison.OrdinalIgnoreCase) ||
                                     name.Equals($"{component} General", StringComparison.OrdinalIgnoreCase)) &&
                                    lowerName.Contains(lowerComponent.Substring(0, Math.Min(lowerComponent.Length, 6))))
                                {
                                    priority = 300;
                                    pathDescription = $"Component-specific General {component} set";
                                }
                                // VERY HIGH priority: Exact match for component verification cases
                                else if ((name.Equals($"{component} Verification Cases", StringComparison.OrdinalIgnoreCase) ||
                                          name.Equals($"{component}Verification Cases", StringComparison.OrdinalIgnoreCase) ||
                                          name.Equals($"{component} Verification", StringComparison.OrdinalIgnoreCase)) ||
                                         (lowerName.Contains(lowerComponent) && lowerName.Contains("verification case")))
                                {
                                    priority = 250;
                                    pathDescription = $"{component} verification cases set";
                                }
                                // HIGH priority: General verification cases sets with component context
                                else if (lowerName.Contains("verification case") || 
                                         (lowerName.Contains("verification") && lowerComponent == "systems"))
                                {
                                    priority = 200;
                                    pathDescription = "Verification cases set";
                                }
                                // MEDIUM-HIGH priority: Component-related test/verify containers
                                else if (lowerName.Contains(lowerComponent) &&
                                         (lowerName.Contains("verification") || lowerName.Contains("test") || 
                                          lowerName.Contains("verify") || lowerName.Contains("case")))
                                {
                                    priority = 150;
                                    pathDescription = $"Component {component} test/verification set";
                                }
                                // MEDIUM priority: General test/verification containers
                                else if (lowerName.Contains("verification") || lowerName.Contains("test case") || 
                                         lowerName.Contains("verify") || lowerName.Contains("procedure"))
                                {
                                    priority = 100;
                                    pathDescription = "General verification/test set";
                                }
                                // LOWER priority: Test-related but exclude artifacts, documents, requirements
                                else if (lowerName.Contains("test") && 
                                         !lowerName.Contains("artifact") && !lowerName.Contains("document") && 
                                         !lowerName.Contains("drawing") && !lowerName.Contains("spec") &&
                                         !lowerName.Contains("manual") && !lowerName.Contains("report") &&
                                         !lowerName.Contains("requirement") && !lowerName.Contains("requirements"))
                                {
                                    priority = 80;
                                    pathDescription = "Test-related set";
                                }
                                
                                if (priority > 0)
                                {
                                    candidateSets.Add((id, name, priority, pathDescription));
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Found candidate verification cases set: ID={id}, Name='{name}', Priority={priority}, Type='{pathDescription}'");
                                }
                                else
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Excluding Set: ID={id}, Name='{name}' (not suitable for verification cases)");
                                }
                            }
                        }
                    }
                    
                    // Check if there are more results to fetch
                    startIndex += resultCount;
                    hasMoreResults = startIndex < totalResults && resultCount > 0;
                }
                
                // Select the highest priority candidate, with component-specific preference
                if (candidateSets.Any())
                {
                    // Log all candidates for transparency
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Found {candidateSets.Count} candidate sets for {component} verification cases:");
                    foreach (var candidate in candidateSets.OrderByDescending(c => c.Priority))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect]   - ID={candidate.Id}, Name='{candidate.Name}', Priority={candidate.Priority}, Type='{candidate.Path}'");
                    }
                    
                    var best = candidateSets.OrderByDescending(c => c.Priority).ThenBy(c => c.Name).First();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Selected best verification cases container for {component}: ID={best.Id}, Name='{best.Name}', Priority={best.Priority}");
                    return (true, best.Id, $"Found {component}-aware verification cases set '{best.Name}' (priority: {best.Priority}, type: {best.Path})");
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] No verification cases container found in {component} component after checking all items");
                return (false, null, $"No suitable verification cases container found for {component} component");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Error finding {component} verification cases container: {ex.Message}");
                return (false, null, $"Error finding {component} verification cases container: {ex.Message}");
            }
        }

        /// <summary>
        /// Create a verification cases set in the specified component
        /// </summary>
        private async Task<(bool Success, int? ContainerId, string Message)> CreateVerificationCasesSetInComponentAsync(int projectId, string component, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureAccessTokenAsync();
                
                // First, find the component's parent folder/container
                var componentContainerId = await FindComponentContainerAsync(projectId, component, cancellationToken);
                
                var setName = $"{component} Verification Cases";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Creating verification cases set: '{setName}' in {component} component");
                
                // Build request to create Set (item type 54)
                object requestBody;
                if (componentContainerId.HasValue)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Creating verification cases set under component container {componentContainerId.Value}");
                    requestBody = new
                    {
                        project = projectId,
                        itemType = 54, // Set
                        childItemType = 194, // Verification Case
                        location = new
                        {
                            parent = componentContainerId.Value
                        },
                        fields = new
                        {
                            setKey = $"{component.ToUpper()}_VC",
                            name = setName,
                            description = $"Container for {component} verification cases and test procedures"
                        }
                    };
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Creating verification cases set at project root (no component container found)");
                    requestBody = new
                    {
                        project = projectId,
                        itemType = 54, // Set
                        childItemType = 194, // Verification Case
                        fields = new
                        {
                            setKey = $"{component.ToUpper()}_VC",
                            name = setName,
                            description = $"Container for {component} verification cases and test procedures"
                        }
                    };
                }

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var url = $"{_baseUrl}/rest/v1/items";
                var response = await _httpClient.PostAsync(url, content, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Failed to create verification cases set: {response.StatusCode} - {errorContent}");
                    return (false, null, $"Failed to create verification cases set: {response.StatusCode} - {errorContent}");
                }
                
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                if (TryExtractCreatedItemInfo(responseContent, out var createdId, out _, out _))
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Successfully created verification cases set '{setName}' with ID {createdId}");
                    return (true, createdId, $"Created verification cases set '{setName}' in {component} component");
                }
                
                return (false, null, "Failed to parse response from verification cases set creation");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Error creating verification cases set: {ex.Message}");
                return (false, null, $"Error creating verification cases set: {ex.Message}");
            }
        }

        /// <summary>
        /// Find any suitable Set that can accept Verification Cases (enterprise fallback method)
        /// </summary>
        private async Task<(bool Success, int? ContainerId, string Message)> FindSuitableVerificationCasesSetAsync(int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureAccessTokenAsync();
                
                var candidateSets = new List<(int Id, string Name, int Priority)>();
                var startIndex = 0;
                var batchSize = 50;
                bool hasMoreResults = true;
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Searching for any Set that can accept Verification Cases (with pagination)");
                
                // Paginate through all project items to find suitable Sets
                while (hasMoreResults)
                {
                    var url = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults={batchSize}&startAt={startIndex}";
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Fetching items batch: startAt={startIndex}, maxResults={batchSize}");
                    
                    var response = await _httpClient.GetAsync(url, cancellationToken);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                        TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Failed to get project items batch {startIndex}: {response.StatusCode} - {errorContent}");
                        break;
                    }
                    
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var document = JsonDocument.Parse(content);
                    
                    var meta = document.RootElement.GetProperty("meta");
                    var pageInfo = meta.GetProperty("pageInfo");
                    var resultCount = pageInfo.GetProperty("resultCount").GetInt32();
                    var totalResults = pageInfo.GetProperty("totalResults").GetInt32();
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Batch results: {resultCount} items, total available: {totalResults}");
                    
                    var data = document.RootElement.GetProperty("data");
                    
                    // Analyze Sets in this batch
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("itemType", out var itemTypeElement) && 
                            item.TryGetProperty("id", out var idElement) &&
                            item.TryGetProperty("fields", out var fieldsElement) &&
                            fieldsElement.TryGetProperty("name", out var nameElement))
                        {
                            var itemTypeId = itemTypeElement.GetInt32();
                            var id = idElement.GetInt32();
                            var name = nameElement.GetString() ?? "";
                            var lowerName = name.ToLowerInvariant();
                            
                            if (itemTypeId == 54) // Only consider Sets
                            {
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Examining Set: ID={id}, Name='{name}'");
                                
                                var priority = 0;
                                
                                // Highest priority: exact match for existing verification cases sets
                                if (lowerName.Contains("verification case") || lowerName.Contains("systems verification") || 
                                    name.Equals("SystemsVerification Cases", StringComparison.OrdinalIgnoreCase) ||
                                    name.Equals("Systems Verification Cases", StringComparison.OrdinalIgnoreCase))
                                {
                                    priority = 200;
                                }
                                // High priority: explicitly verification-related
                                else if (lowerName.Contains("verification") || lowerName.Contains("test case") || lowerName.Contains("verify"))
                                {
                                    priority = 100;
                                }
                                // Medium-high priority: test-related  
                                else if (lowerName.Contains("test") || lowerName.Contains("procedure"))
                                {
                                    priority = 80;
                                }
                                // Lower priority: general purpose but exclude artifacts, documents, requirements sets
                                else if (!lowerName.Contains("artifact") && !lowerName.Contains("document") && 
                                        !lowerName.Contains("drawing") && !lowerName.Contains("spec") &&
                                        !lowerName.Contains("manual") && !lowerName.Contains("report") &&
                                        !lowerName.Contains("requirement") && !lowerName.Contains("requirements"))
                                {
                                    priority = 40;
                                }
                                
                                if (priority > 0)
                                {
                                    candidateSets.Add((id, name, priority));
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Found candidate set for verification cases: ID={id}, Name='{name}', Priority={priority}");
                                }
                                else
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Excluding Set: ID={id}, Name='{name}' (not suitable for verification cases)");
                                }
                            }
                        }
                    }
                    
                    // Check if there are more results to fetch
                    startIndex += resultCount;
                    hasMoreResults = startIndex < totalResults && resultCount > 0;
                }
                
                // Select the highest priority candidate
                if (candidateSets.Any())
                {
                    var best = candidateSets.OrderByDescending(c => c.Priority).ThenBy(c => c.Name).First();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Selected best verification cases container: ID={best.Id}, Name='{best.Name}', Priority={best.Priority}");
                    return (true, best.Id, $"Found suitable set '{best.Name}' for verification cases (priority: {best.Priority})");
                }
                
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] No suitable Sets found for Verification Cases in project {projectId} after checking all items");
                return (false, null, "No suitable Sets found that can accept Verification Cases");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Error finding suitable verification cases set: {ex.Message}");
                return (false, null, $"Error finding suitable verification cases set: {ex.Message}");
            }
        }

        /// <summary>
        /// Find the parent container for a specific component (Systems, Hardware, Software)
        /// </summary>
        private async Task<int?> FindComponentContainerAsync(int projectId, string component, CancellationToken cancellationToken = default)
        {
            try
            {
                var url = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults=50";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (!response.IsSuccessStatusCode) return null;
                
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(content);
                var data = document.RootElement.GetProperty("data");
                
                var lowerComponent = component.ToLowerInvariant();
                
                // Look for Folders that match the component name
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("itemType", out var itemTypeElement) && 
                        item.TryGetProperty("id", out var idElement) &&
                        item.TryGetProperty("fields", out var fieldsElement) &&
                        fieldsElement.TryGetProperty("name", out var nameElement))
                    {
                        var itemTypeId = itemTypeElement.GetInt32();
                        var id = idElement.GetInt32();
                        var name = nameElement.GetString() ?? "";
                        var lowerName = name.ToLowerInvariant();
                        
                        if (itemTypeId == 55 && lowerName.Contains(lowerComponent)) // Folder matching component
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Found {component} component container: ID={id}, Name='{name}'");
                            return id;
                        }
                    }
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] No {component} component container found");
                return null;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Error finding component container: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get or create a verification cases container based on requirement's component location
        /// </summary>
        private async Task<(bool Success, int? ContainerId, string Message)> GetOrCreateVerificationCasesContainerAsync(int projectId, Requirement requirement, CancellationToken cancellationToken = default)
        {
            try
            {
                // Parse component from requirement location
                var component = ParseComponentFromRequirement(requirement);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Requirement belongs to {component} component, looking for verification cases container");
                
                // Look for existing verification cases container in component
                var (foundExisting, existingId, existingMessage) = await FindVerificationCasesSetInComponentAsync(projectId, component, cancellationToken);
                
                if (foundExisting && existingId.HasValue)
                {
                    return (true, existingId, $"Using existing verification cases container in {component}: {existingMessage}");
                }
                
                // Create new verification cases container in component
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] No verification cases container found in {component}, attempting to create or find alternative");
                
                // First try to find any existing Set that can accept Verification Cases
                var (foundGeneric, genericId, genericMessage) = await FindSuitableVerificationCasesSetAsync(projectId, cancellationToken);
                if (foundGeneric && genericId.HasValue)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Found existing suitable set for verification cases: {genericMessage}");
                    return (true, genericId, $"Using existing verification cases container: {genericMessage}");
                }
                
                // Then try to create new verification cases container
                var (createdNew, newId, newMessage) = await CreateVerificationCasesSetInComponentAsync(projectId, component, cancellationToken);
                
                if (createdNew && newId.HasValue)
                {
                    return (true, newId, $"Created new verification cases container in {component}: {newMessage}");
                }
                
                // Fallback to generic container discovery if component-specific approach failed
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Component-specific container creation failed, falling back to generic discovery");
                return await GetTestCaseContainerAsync(projectId, cancellationToken);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Error in component-aware container discovery, falling back to generic: {ex.Message}");
                return await GetTestCaseContainerAsync(projectId, cancellationToken);
            }
        }

        /// <summary>
        /// Get containers in a project to find suitable parent for test cases (legacy method for fallback)
        /// </summary>
        private async Task<(bool Success, int? ContainerId, string Message)> GetTestCaseContainerAsync(int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureAccessTokenAsync();
                
                var url = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults=50";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Getting project containers: {url}");
                
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Failed to get project items: {response.StatusCode} - {errorContent}");
                    return (false, null, $"Failed to get project items: {response.StatusCode}");
                }
                
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Project items response (first 1000 chars): {(content.Length > 1000 ? content.Substring(0, 1000) + "..." : content)}");
                
                using var document = JsonDocument.Parse(content);
                var data = document.RootElement.GetProperty("data");
                
                // Look for containers that could hold test cases
                int? bestContainerId = null;
                string bestContainerName = "";
                
                // First pass: Look for Folders (55) with test-related names - they're more general purpose
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("itemType", out var itemTypeElement) && 
                        item.TryGetProperty("id", out var idElement) &&
                        item.TryGetProperty("fields", out var fieldsElement) &&
                        fieldsElement.TryGetProperty("name", out var nameElement))
                    {
                        var itemTypeId = itemTypeElement.GetInt32();
                        var id = idElement.GetInt32();
                        var name = nameElement.GetString() ?? "";
                        
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Found item: ID={id}, Type={itemTypeId}, Name='{name}'");
                        
                        // Prefer Folders (55) with test-related names
                        if (itemTypeId == 55) // Folder
                        {
                            var lowerName = name.ToLowerInvariant();
                            if (lowerName.Contains("test") || lowerName.Contains("verification") || 
                                lowerName.Contains("case") || lowerName.Contains("verify") ||
                                lowerName.Contains("veri") || lowerName.Contains("procedure"))
                            {
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Found test-related folder: ID={id}, Name='{name}'");
                                bestContainerId = id;
                                bestContainerName = name;
                                break; // Use first test-related folder found
                            }
                        }
                    }
                }
                
                // Second pass: Look for any Folder (55) if no test-specific folder found
                if (!bestContainerId.HasValue)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("itemType", out var itemTypeElement) && 
                            item.TryGetProperty("id", out var idElement) &&
                            item.TryGetProperty("fields", out var fieldsElement) &&
                            fieldsElement.TryGetProperty("name", out var nameElement))
                        {
                            var itemTypeId = itemTypeElement.GetInt32();
                            var id = idElement.GetInt32();
                            var name = nameElement.GetString() ?? "";
                            
                            if (itemTypeId == 55) // Any Folder
                            {
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Found general folder: ID={id}, Name='{name}'");
                                bestContainerId = id;
                                bestContainerName = name;
                                break; // Use first folder found
                            }
                        }
                    }
                }
                
                // Third pass: Look for Sets (54) with test-related names only as last resort
                if (!bestContainerId.HasValue)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("itemType", out var itemTypeElement) && 
                            item.TryGetProperty("id", out var idElement) &&
                            item.TryGetProperty("fields", out var fieldsElement) &&
                            fieldsElement.TryGetProperty("name", out var nameElement))
                        {
                            var itemTypeId = itemTypeElement.GetInt32();
                            var id = idElement.GetInt32();
                            var name = nameElement.GetString() ?? "";
                            
                            if (itemTypeId == 54) // Set
                            {
                                var lowerName = name.ToLowerInvariant();
                                if (lowerName.Contains("test") || lowerName.Contains("verification") || 
                                    lowerName.Contains("case") || lowerName.Contains("verify") ||
                                    lowerName.Contains("veri") || lowerName.Contains("procedure"))
                                {
                                    // Avoid sets that seem to be for artifacts or documents
                                    if (!lowerName.Contains("document") && !lowerName.Contains("artifact") && 
                                        !lowerName.Contains("drawing") && !lowerName.Contains("spec"))
                                    {
                                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Found test-related set: ID={id}, Name='{name}'");
                                        bestContainerId = id;
                                        bestContainerName = name;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                
                if (bestContainerId.HasValue)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Selected best container for Verification Cases: ID={bestContainerId}, Name='{bestContainerName}'");
                    return (true, bestContainerId, $"Found suitable container '{bestContainerName}' (ID={bestContainerId})");
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] No suitable container found in project {projectId}");
                return (false, null, "No suitable container found for test cases");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Error getting test case container: {ex.Message}");
                return (false, null, $"Error getting container: {ex.Message}");
            }
        }

        /// <summary>
        /// Create a test case in Jama Connect
        /// </summary>
        public async Task<(bool Success, string Message, int? TestCaseId)> CreateTestCaseAsync(int projectId, JamaTestCaseRequest testCase, CancellationToken cancellationToken = default)
        {
            return await CreateTestCaseAsync(projectId, testCase, null, cancellationToken);
        }

        /// <summary>
        /// Create a test case in Jama Connect with component-aware container discovery
        /// </summary>
        public async Task<(bool Success, string Message, int? TestCaseId)> CreateTestCaseAsync(int projectId, JamaTestCaseRequest testCase, Requirement? associatedRequirement = null, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureAccessTokenAsync();
                
                // Get test case item type
                var (typeSuccess, itemTypeId) = await GetTestCaseItemTypeAsync(projectId, cancellationToken);
                if (!typeSuccess || !itemTypeId.HasValue)
                {
                    return (false, "Could not determine test case item type for project", null);
                }
                
                // Get container for test case using component-aware discovery if requirement is provided
                var (containerSuccess, containerId, containerMessage) = associatedRequirement != null
                    ? await GetOrCreateVerificationCasesContainerAsync(projectId, associatedRequirement, cancellationToken)
                    : await GetTestCaseContainerAsync(projectId, cancellationToken);
                
                if (!containerSuccess || !containerId.HasValue)
                {
                    if (associatedRequirement != null)
                    {
                        var component = ParseComponentFromRequirement(associatedRequirement);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Component-aware container lookup failed for {component} component: {containerMessage}");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Generic container lookup failed: {containerMessage}");
                    }
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Will attempt to create Verification Case at project root");
                    // For RTU4220-like projects, try creating at project root since containers may be restricted
                }

                // Build test steps description
                var stepsDescription = new StringBuilder();
                if (testCase.Steps?.Any() == true)
                {
                    stepsDescription.AppendLine("Test Steps:");
                    stepsDescription.AppendLine();
                    foreach (var step in testCase.Steps)
                    {
                        if (!string.IsNullOrWhiteSpace(step.Number))
                        {
                            stepsDescription.AppendLine($"**Step {step.Number}:**");
                            stepsDescription.AppendLine($"Action: {step.Action}");
                            stepsDescription.AppendLine($"Expected Result: {step.ExpectedResult}");
                            if (!string.IsNullOrWhiteSpace(step.Notes))
                            {
                                stepsDescription.AppendLine($"Notes: {step.Notes}");
                            }
                            stepsDescription.AppendLine();
                        }
                    }
                }

                // Combine description with steps
                var combinedDescription = string.IsNullOrWhiteSpace(testCase.Description) 
                    ? stepsDescription.ToString()
                    : $"{testCase.Description}\n\n{stepsDescription}";

                // Build request body with proper location
                object requestBody;
                string attemptDescription;
                if (containerId.HasValue)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Creating test case in container {containerId.Value}");
                    attemptDescription = $"in container {containerId.Value}";
                    requestBody = new
                    {
                        project = projectId,
                        itemType = itemTypeId.Value,
                        location = new
                        {
                            parent = containerId.Value
                        },
                        fields = new
                        {
                            name = testCase.Name,
                            description = combinedDescription.Trim()
                        }
                    };
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Creating test case at project root (no container)");
                    attemptDescription = "at project root";
                    requestBody = new
                    {
                        project = projectId,
                        itemType = itemTypeId.Value,
                        fields = new
                        {
                            name = testCase.Name,
                            description = combinedDescription.Trim()
                        }
                    };
                }

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var url = $"{_baseUrl}/rest/v1/items";
                var response = await _httpClient.PostAsync(url, content, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    
                    // If we failed with a container and the error is about location restrictions, try without container
                    if (containerId.HasValue && errorContent.Contains("Invalid document location") && 
                        (errorContent.Contains("cannot be under") || errorContent.Contains("Folder of") || errorContent.Contains("Set of")))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Container creation failed due to location restrictions. Retrying at project root...");
                        
                        // Retry without container
                        var fallbackRequestBody = new
                        {
                            project = projectId,
                            itemType = itemTypeId.Value,
                            fields = new
                            {
                                name = testCase.Name,
                                description = combinedDescription.Trim()
                            }
                        };
                        
                        var fallbackJson = JsonSerializer.Serialize(fallbackRequestBody);
                        var fallbackContent = new StringContent(fallbackJson, Encoding.UTF8, "application/json");
                        var fallbackResponse = await _httpClient.PostAsync(url, fallbackContent, cancellationToken);
                        
                        if (!fallbackResponse.IsSuccessStatusCode)
                        {
                            var fallbackErrorContent = await fallbackResponse.Content.ReadAsStringAsync(cancellationToken);
                            return (false, $"Failed to create test case both in container and at project root. " +
                                          $"Container attempt: {response.StatusCode} - {errorContent}. " +
                                          $"Root attempt: {fallbackResponse.StatusCode} - {fallbackErrorContent}. " +
                                          $"This project may not be configured to accept Verification Cases.", null);
                        }
                        
                        // Success with fallback
                        response = fallbackResponse;
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Successfully created test case at project root after container restriction");
                    }
                    else
                    {
                        return (false, $"Failed to create test case {attemptDescription}: {response.StatusCode} - {errorContent}", null);
                    }
                }
                
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Create test case response: {responseContent}");
                
                // Parse the response to extract the created item ID
                using var responseDoc = JsonDocument.Parse(responseContent);
                var root = responseDoc.RootElement;
                
                int? createdId = null;
                
                // Try to get ID from meta object (Jama API format)
                if (root.TryGetProperty("meta", out var metaElement) &&
                    metaElement.TryGetProperty("id", out var metaIdElement))
                {
                    createdId = metaIdElement.GetInt32();
                }
                // Fallback: try to get ID from root level
                else if (root.TryGetProperty("id", out var rootIdElement))
                {
                    createdId = rootIdElement.GetInt32();
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Parsed test case ID: {createdId}");
                
                return (true, "Test case created successfully", createdId);
            }
            catch (Exception ex)
            {
                return (false, $"Error creating test case: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Import multiple test cases to Jama Connect
        /// </summary>
        public async Task<(bool Success, string Message, List<int> CreatedIds)> ImportTestCasesAsync(int projectId, List<JamaTestCaseRequest> testCases, CancellationToken cancellationToken = default)
        {
            var createdIds = new List<int>();
            var errors = new List<string>();
            
            try
            {
                var importProgress = 0;
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Starting import of {testCases.Count} test cases to project {projectId}");
                
                foreach (var testCase in testCases)
                {
                    var (success, message, testCaseId) = await CreateTestCaseAsync(projectId, testCase, cancellationToken);
                    
                    if (success && testCaseId.HasValue)
                    {
                        createdIds.Add(testCaseId.Value);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Created test case '{testCase.Name}' with ID {testCaseId}");
                    }
                    else
                    {
                        errors.Add($"Failed to create '{testCase.Name}': {message}");
                        TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Failed to create test case '{testCase.Name}': {message}");
                    }
                    
                    importProgress++;
                    
                    // Add small delay between requests to respect rate limits
                    if (importProgress < testCases.Count)
                    {
                        await Task.Delay(200, cancellationToken);
                    }
                }
                
                var overallSuccess = createdIds.Count > 0;
                var resultMessage = overallSuccess 
                    ? $"Successfully created {createdIds.Count} out of {testCases.Count} test cases"
                    : "Failed to create any test cases";
                    
                if (errors.Any())
                {
                    resultMessage += $"\n\nErrors encountered:\n{string.Join("\n", errors)}";
                }
                    
                return (overallSuccess, resultMessage, createdIds);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Error during batch import: {ex.Message}");
                return (false, $"Import failed with error: {ex.Message}", createdIds);
            }
        }

        /// <summary>
        /// Import test case with component-aware placement based on source requirement
        /// </summary>
        public async Task<(bool Success, string Message, int? TestCaseId)> ImportTestCaseFromRequirementAsync(int projectId, JamaTestCaseRequest testCase, Requirement sourceRequirement, CancellationToken cancellationToken = default)
        {
            if (projectId <= 0)
            {
                return (false, "Invalid Jama project ID.", null);
            }

            try
            {
                var component = ParseComponentFromRequirement(sourceRequirement);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Importing test case '{testCase.Name}' for {component} component based on requirement {sourceRequirement.GlobalId ?? sourceRequirement.Item}");
                
                var (success, message, testCaseId) = await CreateTestCaseAsync(projectId, testCase, sourceRequirement, cancellationToken);
                
                if (success && testCaseId.HasValue)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Successfully placed test case '{testCase.Name}' (ID: {testCaseId}) in {component} verification cases");
                }
                
                return (success, message, testCaseId);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Error importing test case from requirement: {ex.Message}");
                return (false, $"Error importing test case: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Persist extracted requirements directly into Jama so the extraction work is not lost if later analysis fails.
        /// </summary>
        public async Task<(int CreatedCount, int FailedCount)> ImportRequirementsToJamaAsync(
            int projectId,
            IReadOnlyList<Requirement> requirements,
            int? preferredParentContainerId = null,
            CancellationToken cancellationToken = default,
            Action<int, int, int, string?>? progressCallback = null)
        {
            if (projectId <= 0 || requirements == null || requirements.Count == 0)
            {
                return (0, 0);
            }

            var createdCount = 0;
            var failedCount = 0;
            const int maxAttemptsPerRequirement = 3;
            var sectionKeys = requirements
                .Select(GetNormalizedSectionKey)
                .ToList();
            var sectionTotals = sectionKeys
                .GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var sectionOrdinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var reqIndex = 0; reqIndex < requirements.Count; reqIndex++)
            {
                var requirement = requirements[reqIndex];
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var sectionKey = sectionKeys[reqIndex];
                sectionOrdinals.TryGetValue(sectionKey, out var currentOrdinal);
                var sectionOrdinal = currentOrdinal + 1;
                sectionOrdinals[sectionKey] = sectionOrdinal;
                requirement.Name = BuildSectionTraceRequirementName(
                    requirement,
                    sectionKey,
                    sectionOrdinal,
                    sectionTotals[sectionKey]);

                var created = false;
                string? lastFailureMessage = null;
                int? createdId = null;
                var lastFailureWasNonRetryable = false;

                for (var attempt = 1; attempt <= maxAttemptsPerRequirement; attempt++)
                {
                    if (attempt > 1)
                    {
                        progressCallback?.Invoke(
                            createdCount + failedCount,
                            requirements.Count,
                            failedCount,
                            $"Retrying '{requirement.Item ?? requirement.Name}' (attempt {attempt}/{maxAttemptsPerRequirement})");
                    }

                    var (success, message, jamaItemId) = await CreateRequirementAsync(projectId, requirement, preferredParentContainerId, cancellationToken);
                    if (success && jamaItemId.HasValue)
                    {
                        created = true;
                        createdId = jamaItemId;
                        break;
                    }

                    lastFailureMessage = message;
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Attempt {attempt}/{maxAttemptsPerRequirement} failed for extracted requirement '{requirement.Item ?? requirement.Name}': {message}");

                    lastFailureWasNonRetryable = IsNonRetryableRequirementCreateFailure(message);
                    if (lastFailureWasNonRetryable)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Non-retryable requirement persistence failure detected for '{requirement.Item ?? requirement.Name}'. Skipping remaining retries for this item.");
                        break;
                    }

                    if (attempt < maxAttemptsPerRequirement)
                    {
                        // Use a much longer delay when Jama is undergoing an upgrade.
                        var delayMs = message?.StartsWith("JAMA_UPGRADING", StringComparison.OrdinalIgnoreCase) == true
                            ? 5000 * attempt
                            : 250 * attempt;
                        await Task.Delay(delayMs, cancellationToken);
                    }
                }

                if (created && createdId.HasValue)
                {
                    createdCount++;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Persisted extracted requirement '{requirement.Item ?? requirement.Name}' to Jama item ID {createdId.Value}");
                }
                else
                {
                    failedCount++;
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Failed to persist extracted requirement '{requirement.Item ?? requirement.Name}' after {maxAttemptsPerRequirement} attempts. Last error: {lastFailureMessage}");

                    if (lastFailureWasNonRetryable)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error("[JamaConnect] Aborting remaining extracted requirement imports due to non-retryable schema failure.");
                        break;
                    }
                }

                progressCallback?.Invoke(
                    createdCount + failedCount,
                    requirements.Count,
                    failedCount,
                    $"Last item: {(requirement.Item ?? requirement.Name)}");

                if (createdCount + failedCount < requirements.Count)
                {
                    await Task.Delay(150, cancellationToken);
                }
            }

            return (createdCount, failedCount);
        }

        /// <summary>
        /// Create a requirement item in Jama Connect.
        /// </summary>
        public async Task<(bool Success, string Message, int? JamaItemId)> CreateRequirementAsync(int projectId, Requirement requirement, int? preferredParentContainerId = null, CancellationToken cancellationToken = default)
        {
            if (projectId <= 0)
            {
                return (false, "Invalid Jama project ID.", null);
            }

            try
            {
                await EnsureAccessTokenAsync();

                var (typeSuccess, itemTypeId) = await GetRequirementItemTypeAsync(projectId, cancellationToken);
                if (!typeSuccess || !itemTypeId.HasValue)
                {
                    return (false, "Could not determine requirement item type for project", null);
                }

                var requirementName = ResolveRequirementName(requirement);
                var description = ResolveRequirementDescription(requirement, requirementName);
                var fields = await BuildCreateRequirementFieldsAsync(
                    projectId,
                    itemTypeId.Value,
                    requirement,
                    requirementName,
                    description,
                    cancellationToken);
                var effectiveParentContainerId = await ResolvePreferredRequirementPlacementContainerAsync(projectId, itemTypeId.Value, preferredParentContainerId, cancellationToken);

                object requestBody;
                if (effectiveParentContainerId.HasValue && effectiveParentContainerId.Value > 0)
                {
                    requestBody = new
                    {
                        project = projectId,
                        itemType = itemTypeId.Value,
                        location = new
                        {
                            parent = effectiveParentContainerId.Value
                        },
                        fields
                    };
                }
                else
                {
                    requestBody = new
                    {
                        project = projectId,
                        itemType = itemTypeId.Value,
                        fields
                    };
                }

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var url = $"{_baseUrl}/rest/v1/items";
                var response = await _httpClient.PostAsync(url, content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);

                    // Detect transient Jama maintenance/upgrade page before other error handling.
                    if (IsJamaUpgradeBanner(errorContent))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Jama is being upgraded. Will retry after back-off.");
                        return (false, "JAMA_UPGRADING: Instance is being upgraded, retry after delay", null);
                    }

                    if (IsRequirementProjectRootLocationError(errorContent))
                    {
                        var locationRetry = await TryCreateRequirementInDiscoveredContainerAsync(
                            projectId,
                            itemTypeId.Value,
                            requirementName,
                            description,
                            fields,
                            cancellationToken);

                        if (locationRetry.Success && locationRetry.JamaItemId.HasValue)
                        {
                            return await FinalizeCreatedRequirementAsync(
                                projectId,
                                requirement,
                                locationRetry.JamaItemId.Value,
                                locationRetry.DocumentKey,
                                locationRetry.GlobalId,
                                "Requirement created successfully after location fallback",
                                cancellationToken);
                        }
                    }

                    var repairedAndRetried = await TryRepairLookupFieldsAndRetryRequirementCreateAsync(
                        projectId,
                        itemTypeId.Value,
                        requirementName,
                        description,
                        fields,
                        effectiveParentContainerId,
                        errorContent,
                        cancellationToken);

                    if (repairedAndRetried.Success && repairedAndRetried.JamaItemId.HasValue)
                    {
                        return await FinalizeCreatedRequirementAsync(
                            projectId,
                            requirement,
                            repairedAndRetried.JamaItemId.Value,
                            repairedAndRetried.DocumentKey,
                            repairedAndRetried.GlobalId,
                            "Requirement created successfully after lookup field repair",
                            cancellationToken);
                    }

                    var requiredFieldRetried = await TryPopulateRequiredFieldsAndRetryRequirementCreateAsync(
                        projectId,
                        itemTypeId.Value,
                        requirementName,
                        description,
                        fields,
                        effectiveParentContainerId,
                        errorContent,
                        cancellationToken);

                    if (requiredFieldRetried.Success && requiredFieldRetried.JamaItemId.HasValue)
                    {
                        return await FinalizeCreatedRequirementAsync(
                            projectId,
                            requirement,
                            requiredFieldRetried.JamaItemId.Value,
                            requiredFieldRetried.DocumentKey,
                            requiredFieldRetried.GlobalId,
                            "Requirement created successfully after required-field repair",
                            cancellationToken);
                    }

                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Requirement create failed for project {projectId}, itemType {itemTypeId.Value}: {response.StatusCode} - {errorContent}");
                    return (false, $"Failed to create requirement item: {response.StatusCode} - {errorContent}", null);
                }

                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                if (TryExtractCreatedItemInfo(response, responseContent, out var createdId, out var documentKey, out var globalId))
                {
                    return await FinalizeCreatedRequirementAsync(
                        projectId,
                        requirement,
                        createdId,
                        documentKey,
                        globalId,
                        "Requirement created successfully",
                        cancellationToken);
                }

                var truncated = string.IsNullOrWhiteSpace(responseContent)
                    ? "<empty>"
                    : responseContent.Length <= 400
                        ? responseContent
                        : responseContent.Substring(0, 400) + "...";
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Could not extract created requirement ID. Response body sample: {truncated}");
                return (false, "Failed to parse response from requirement creation", null);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] Error creating requirement item: {ex.Message}");
                return (false, $"Error creating requirement item: {ex.Message}", null);
            }
        }

        private static string ResolveRequirementName(Requirement requirement)
        {
            return !string.IsNullOrWhiteSpace(requirement.Name)
                ? requirement.Name
                : !string.IsNullOrWhiteSpace(requirement.Item)
                    ? requirement.Item
                    : "Extracted Requirement";
        }

        private static string ResolveRequirementDescription(Requirement requirement, string requirementName)
        {
            return !string.IsNullOrWhiteSpace(requirement.Description)
                ? requirement.Description
                : requirementName;
        }

        private async Task<Dictionary<string, object?>> BuildCreateRequirementFieldsAsync(
            int projectId,
            int itemTypeId,
            Requirement requirement,
            string requirementName,
            string description,
            CancellationToken cancellationToken)
        {
            var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = requirementName,
                ["description"] = description
            };

            var defaultFields = await GetRequirementFieldDefaultsAsync(projectId, itemTypeId, cancellationToken);
            foreach (var kvp in defaultFields)
            {
                if (!fields.ContainsKey(kvp.Key))
                {
                    fields[kvp.Key] = kvp.Value;
                }
            }

            await SanitizeLookupDefaultsForCreateAsync(fields, itemTypeId, cancellationToken);

            if (requirement.IsDerivedFromATP)
            {
                fields["lookup1"] = 1608; // Requirement Type = System
                fields["lookup3"] = 1619; // Derived Requirement = Yes

                TestCaseEditorApp.Services.Logging.Log.Info(
                    "[JamaConnect] Applied derived requirement mapping for item type 193: lookup1=1608, lookup3=1619.");
            }

            await ApplyRequirementEnrichedFieldsAsync(
                projectId,
                itemTypeId,
                requirement,
                fields,
                cancellationToken);

            ApplyUserSelectionRequiredFallbackFields(requirement, itemTypeId, fields);
            ApplyUpstreamSourceFields(requirement, fields);

            return fields;
        }

        private static void ApplyUpstreamSourceFields(Requirement requirement, Dictionary<string, object?> fields)
        {
            var sourceDocumentName = !string.IsNullOrWhiteSpace(requirement.SourceDocumentName)
                ? requirement.SourceDocumentName
                : requirement.AtpDerivation?.SourceDocumentName;
            var sourceReference = !string.IsNullOrWhiteSpace(requirement.TraceReference)
                ? requirement.TraceReference
                : requirement.Item;

            var upstreamSourceParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(sourceDocumentName))
            {
                upstreamSourceParts.Add($"Source document: {sourceDocumentName}");
            }

            if (requirement.SourceAttachmentId.HasValue)
            {
                upstreamSourceParts.Add($"Source attachment ID: {requirement.SourceAttachmentId.Value}");
            }

            if (requirement.SourceJamaItemId.HasValue)
            {
                upstreamSourceParts.Add($"Source item ID: {requirement.SourceJamaItemId.Value}");
            }

            if (!string.IsNullOrWhiteSpace(sourceReference))
            {
                upstreamSourceParts.Add($"Source reference: {sourceReference}");
            }

            if (upstreamSourceParts.Count > 0)
            {
                fields["upstream_cross_instance_relationships$193"] = string.Join("; ", upstreamSourceParts);
            }
        }

        private async Task<(bool Success, string Message, int? JamaItemId)> FinalizeCreatedRequirementAsync(
            int projectId,
            Requirement requirement,
            int createdId,
            string? documentKey,
            string? globalId,
            string successMessage,
            CancellationToken cancellationToken)
        {
            requirement.ApiId = createdId.ToString();
            if (!string.IsNullOrWhiteSpace(documentKey))
            {
                requirement.Item = documentKey;
                requirement.GlobalId = documentKey;
            }
            else if (!string.IsNullOrWhiteSpace(globalId))
            {
                requirement.Item = globalId;
                requirement.GlobalId = globalId;
            }

            await TryCreateTraceabilityRelationshipForDerivedRequirementAsync(
                projectId,
                createdId,
                requirement,
                cancellationToken);

            return (true, successMessage, createdId);
        }

        private async Task<(bool Success, int? JamaItemId, string? DocumentKey, string? GlobalId)> TryRepairLookupFieldsAndRetryRequirementCreateAsync(
            int projectId,
            int itemTypeId,
            string requirementName,
            string description,
            Dictionary<string, object?> fields,
            int? preferredParentContainerId,
            string errorContent,
            CancellationToken cancellationToken)
        {
            // Example error: "Lookup id 556 does not belong to lookup type 1670"
            var mismatchMatch = Regex.Match(errorContent, @"Lookup id\s+(\d+)\s+does not belong to lookup type\s+(\d+)", RegexOptions.IgnoreCase);
            if (!mismatchMatch.Success || !int.TryParse(mismatchMatch.Groups[1].Value, out var invalidLookupId))
            {
                return (false, null, null, null);
            }

            int? lookupTypeId = null;
            if (int.TryParse(mismatchMatch.Groups[2].Value, out var parsedLookupTypeId))
            {
                lookupTypeId = parsedLookupTypeId;
            }

            var candidateFields = fields
                .Where(kvp => kvp.Key.StartsWith("lookup", StringComparison.OrdinalIgnoreCase) && ContainsLookupId(kvp.Value, invalidLookupId))
                .Select(kvp => kvp.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidateFields.Count == 0 && lookupTypeId.HasValue)
            {
                var lookupMappings = await GetLookupFieldPicklistMappingsAsync(itemTypeId, cancellationToken);
                candidateFields = lookupMappings
                    .Where(kvp => kvp.Value == lookupTypeId.Value)
                    .Select(kvp => kvp.Key)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (candidateFields.Count == 0 && fields.ContainsKey("lookup1"))
            {
                candidateFields.Add("lookup1");
            }

            var repairedAny = false;
            foreach (var fieldName in candidateFields)
            {
                int? replacementId = null;

                if (lookupTypeId.HasValue)
                {
                    replacementId = await GetFirstPicklistOptionIdByPicklistIdAsync(lookupTypeId.Value, cancellationToken);
                }

                replacementId ??= await GetFirstPicklistOptionIdAsync(projectId, itemTypeId, fieldName, cancellationToken);

                if (!replacementId.HasValue)
                {
                    continue;
                }

                fields[fieldName] = replacementId.Value;
                repairedAny = true;
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Repaired lookup field '{fieldName}' with picklist option ID {replacementId.Value} after lookup mismatch.");
            }

            if (!repairedAny)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn(
                    $"[JamaConnect] Could not repair lookup mismatch for '{requirementName}'. Invalid lookup id {invalidLookupId}, lookup type {(lookupTypeId?.ToString() ?? "unknown")}. Candidate fields: {string.Join(", ", candidateFields)}");
                return (false, null, null, null);
            }

            object retryBody;
            if (preferredParentContainerId.HasValue && preferredParentContainerId.Value > 0)
            {
                retryBody = new
                {
                    project = projectId,
                    itemType = itemTypeId,
                    location = new
                    {
                        parent = preferredParentContainerId.Value
                    },
                    fields
                };
            }
            else
            {
                retryBody = new
                {
                    project = projectId,
                    itemType = itemTypeId,
                    fields
                };
            }

            var retryJson = JsonSerializer.Serialize(retryBody);
            var retryContent = new StringContent(retryJson, Encoding.UTF8, "application/json");
            var retryUrl = $"{_baseUrl}/rest/v1/items";
            var retryResponse = await _httpClient.PostAsync(retryUrl, retryContent, cancellationToken);
            if (!retryResponse.IsSuccessStatusCode)
            {
                var retryError = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
                if (IsRequirementProjectRootLocationError(retryError))
                {
                    var locationRetry = await TryCreateRequirementInDiscoveredContainerAsync(
                        projectId,
                        itemTypeId,
                        requirementName,
                        description,
                        fields,
                        cancellationToken);

                    if (locationRetry.Success)
                    {
                        return locationRetry;
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Lookup-repair retry failed for '{requirementName}': {retryResponse.StatusCode} - {retryError}");
                return (false, null, null, null);
            }

            var retryResponseContent = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
            if (TryExtractCreatedItemInfo(retryResponse, retryResponseContent, out var retryCreatedId, out var retryDocumentKey, out var retryGlobalId))
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Requirement '{requirementName}' created after lookup field repair.");
                return (true, retryCreatedId, retryDocumentKey, retryGlobalId);
            }

            return (false, null, null, null);
        }

        private async Task<(bool Success, int? JamaItemId, string? DocumentKey, string? GlobalId)> TryPopulateRequiredFieldsAndRetryRequirementCreateAsync(
            int projectId,
            int itemTypeId,
            string requirementName,
            string description,
            Dictionary<string, object?> fields,
            int? preferredParentContainerId,
            string errorContent,
            CancellationToken cancellationToken)
        {
            var requiredFields = ParseMissingRequiredFieldNames(errorContent);
            if (requiredFields.Count == 0)
            {
                return (false, null, null, null);
            }

            var repairedAny = false;
            Dictionary<string, int>? lookupMappings = null;

            foreach (var missingField in requiredFields)
            {
                if (string.IsNullOrWhiteSpace(missingField))
                {
                    continue;
                }

                var fieldName = missingField.Trim();
                var candidateFieldName = ResolveExistingFieldNameForItemType(fields, fieldName, itemTypeId) ?? fieldName;

                if (candidateFieldName.StartsWith("lookup", StringComparison.OrdinalIgnoreCase))
                {
                    var replacementId = await GetFirstPicklistOptionIdAsync(projectId, itemTypeId, candidateFieldName, cancellationToken);
                    if (!replacementId.HasValue)
                    {
                        lookupMappings ??= await GetLookupFieldPicklistMappingsAsync(itemTypeId, cancellationToken);
                        if (lookupMappings.TryGetValue(candidateFieldName, out var picklistId) && picklistId > 0)
                        {
                            replacementId = await GetFirstPicklistOptionIdByPicklistIdAsync(picklistId, cancellationToken);
                        }
                    }

                    if (replacementId.HasValue)
                    {
                        fields[candidateFieldName] = replacementId.Value;
                        repairedAny = true;
                        TestCaseEditorApp.Services.Logging.Log.Warn(
                            $"[JamaConnect] Repaired required lookup field '{candidateFieldName}' with picklist option ID {replacementId.Value} after create validation failure.");
                    }

                    continue;
                }

                if (!fields.TryGetValue(candidateFieldName, out var existingValue) || string.IsNullOrWhiteSpace(existingValue?.ToString()))
                {
                    fields[candidateFieldName] = candidateFieldName.Equals("description", StringComparison.OrdinalIgnoreCase)
                        ? description
                        : "User Selection Required";
                    repairedAny = true;
                    TestCaseEditorApp.Services.Logging.Log.Warn(
                        $"[JamaConnect] Repaired required field '{candidateFieldName}' with placeholder value after create validation failure.");
                }
            }

            if (!repairedAny)
            {
                return (false, null, null, null);
            }

            object retryBody;
            if (preferredParentContainerId.HasValue && preferredParentContainerId.Value > 0)
            {
                retryBody = new
                {
                    project = projectId,
                    itemType = itemTypeId,
                    location = new
                    {
                        parent = preferredParentContainerId.Value
                    },
                    fields
                };
            }
            else
            {
                retryBody = new
                {
                    project = projectId,
                    itemType = itemTypeId,
                    fields
                };
            }

            var retryJson = JsonSerializer.Serialize(retryBody);
            var retryContent = new StringContent(retryJson, Encoding.UTF8, "application/json");
            var retryUrl = $"{_baseUrl}/rest/v1/items";
            var retryResponse = await _httpClient.PostAsync(retryUrl, retryContent, cancellationToken);
            if (!retryResponse.IsSuccessStatusCode)
            {
                var retryError = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
                if (IsRequirementProjectRootLocationError(retryError))
                {
                    var locationRetry = await TryCreateRequirementInDiscoveredContainerAsync(
                        projectId,
                        itemTypeId,
                        requirementName,
                        description,
                        fields,
                        cancellationToken);

                    if (locationRetry.Success)
                    {
                        return locationRetry;
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Required-field repair retry failed for '{requirementName}': {retryResponse.StatusCode} - {retryError}");
                return (false, null, null, null);
            }

            var retryResponseContent = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
            if (TryExtractCreatedItemInfo(retryResponse, retryResponseContent, out var retryCreatedId, out var retryDocumentKey, out var retryGlobalId))
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Requirement '{requirementName}' created after required-field repair.");
                return (true, retryCreatedId, retryDocumentKey, retryGlobalId);
            }

            return (false, null, null, null);
        }

        private static List<string> ParseMissingRequiredFieldNames(string errorContent)
        {
            var fields = new List<string>();
            if (string.IsNullOrWhiteSpace(errorContent))
            {
                return fields;
            }

            var match = Regex.Match(
                errorContent,
                @"required fields\s*\.\s*fields\s*:\s*([A-Za-z0-9_$,\s]+)",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                return fields;
            }

            foreach (var raw in match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    fields.Add(raw.Trim());
                }
            }

            return fields;
        }

        private static bool IsRequirementProjectRootLocationError(string? errorContent)
        {
            if (string.IsNullOrWhiteSpace(errorContent))
            {
                return false;
            }

            return errorContent.Contains("Invalid document location", StringComparison.OrdinalIgnoreCase) &&
                   errorContent.Contains("Requirement cannot be under project", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<(bool Success, int? JamaItemId, string? DocumentKey, string? GlobalId)> TryCreateRequirementInDiscoveredContainerAsync(
            int projectId,
            int itemTypeId,
            string requirementName,
            string description,
            Dictionary<string, object?> fields,
            CancellationToken cancellationToken)
        {
            var candidates = await GetRequirementContainerCandidatesAsync(projectId, cancellationToken);
            if (candidates.Count == 0)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] No requirement containers discovered for project {projectId}; cannot retry location fallback.");
                return (false, null, null, null);
            }

            var url = $"{_baseUrl}/rest/v1/items";
            foreach (var candidate in candidates)
            {
                var retryBody = new
                {
                    project = projectId,
                    itemType = itemTypeId,
                    location = new
                    {
                        parent = candidate.Id
                    },
                    fields
                };

                var retryJson = JsonSerializer.Serialize(retryBody);
                var retryContent = new StringContent(retryJson, Encoding.UTF8, "application/json");
                var retryResponse = await _httpClient.PostAsync(url, retryContent, cancellationToken);

                if (!retryResponse.IsSuccessStatusCode)
                {
                    var retryError = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Location fallback failed for '{requirementName}' in container {candidate.Id} ('{candidate.Name}'): {retryResponse.StatusCode} - {retryError}");
                    continue;
                }

                var retryResponseContent = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
                if (TryExtractCreatedItemInfo(retryResponse, retryResponseContent, out var retryCreatedId, out var retryDocumentKey, out var retryGlobalId))
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Requirement '{requirementName}' created in discovered container {candidate.Id} ('{candidate.Name}').");
                    return (true, retryCreatedId, retryDocumentKey, retryGlobalId);
                }
            }

            return (false, null, null, null);
        }

        private async Task TryCreateTraceabilityRelationshipForDerivedRequirementAsync(
            int projectId,
            int createdRequirementItemId,
            Requirement requirement,
            CancellationToken cancellationToken)
        {
            if (createdRequirementItemId <= 0 || requirement == null)
            {
                return;
            }

            if (!TryExtractSourceItemIdForTraceability(requirement, out var sourceItemId))
            {
                TestCaseEditorApp.Services.Logging.Log.Debug(
                    $"[JamaConnect] Could not determine source item for derived requirement {createdRequirementItemId}. TraceReference='{requirement.TraceReference}'.");
                return;
            }

            if (sourceItemId <= 0 || sourceItemId == createdRequirementItemId)
            {
                return;
            }

            var relationshipTypeId = await GetPreferredRelationshipTypeIdAsync(projectId, cancellationToken);
            var created = await TryCreateRelationshipAsync(sourceItemId, createdRequirementItemId, relationshipTypeId, cancellationToken);
            if (created)
            {
                return;
            }

            // Relationship rules are directional in many Jama projects; retry reversed.
            created = await TryCreateRelationshipAsync(createdRequirementItemId, sourceItemId, relationshipTypeId, cancellationToken);
            if (!created)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn(
                    $"[JamaConnect] Could not create traceability relationship between source item {sourceItemId} and derived requirement {createdRequirementItemId}. Check relationship rules/type configuration.");
            }
        }

        private static bool TryExtractSourceItemIdForTraceability(Requirement requirement, out int sourceItemId)
        {
            sourceItemId = 0;
            var traceReference = requirement.TraceReference;
            if (string.IsNullOrWhiteSpace(traceReference))
            {
                return false;
            }

            if (requirement.SourceJamaItemId.HasValue && requirement.SourceJamaItemId.Value > 0)
            {
                sourceItemId = requirement.SourceJamaItemId.Value;
                return true;
            }

            // Secondary format support: explicit Jama item markers in trace text.
            var jamaIdMatch = Regex.Match(traceReference, @"(?:JAMA|ITEM|ID)[^\d]{0,3}(?<id>\d{2,})", RegexOptions.IgnoreCase);
            if (jamaIdMatch.Success && int.TryParse(jamaIdMatch.Groups["id"].Value, out sourceItemId) && sourceItemId > 0)
            {
                return true;
            }

            return false;
        }

        private async Task<(int? ProjectId, string? ProjectName, string Message)> TryGetFirstAccessibleProjectAsync(CancellationToken cancellationToken)
        {
            try
            {
                var endpoints = new[]
                {
                    $"{_baseUrl}/rest/v1/projects",
                    $"{_baseUrl}/rest/latest/projects"
                };

                foreach (var endpoint in endpoints)
                {
                    var response = await _httpClient.GetAsync(endpoint, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    var projects = TryGetPicklistDataArray(doc.RootElement);
                    if (!projects.HasValue || projects.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var project in projects.Value.EnumerateArray())
                    {
                        var id = GetIntProperty(project, "id");
                        if (!id.HasValue || id.Value <= 0)
                        {
                            continue;
                        }

                        var name = GetStringProperty(project, "name")
                            ?? GetStringProperty(project, "display")
                            ?? GetStringProperty(project, "projectKey");
                        return (id.Value, name, $"Resolved from {endpoint}");
                    }
                }

                return (null, null, "No accessible projects returned from /projects endpoints.");
            }
            catch (Exception ex)
            {
                return (null, null, $"Project probe failed: {ex.Message}");
            }
        }

        private async Task<List<(int Id, string Name)>> GetRelationshipTypeCandidatesAsync(int projectId, CancellationToken cancellationToken)
        {
            var candidates = new List<(int Id, string Name)>();
            if (projectId <= 0)
            {
                return candidates;
            }

            var endpoints = new[]
            {
                $"{_baseUrl}/rest/v1/relationshiptypes?project={projectId}",
                $"{_baseUrl}/rest/v1/projects/{projectId}/relationshiptypes",
                $"{_baseUrl}/rest/v1/relationshiptypes"
            };

            foreach (var endpoint in endpoints)
            {
                try
                {
                    var response = await _httpClient.GetAsync(endpoint, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    JsonElement data;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        data = root;
                    }
                    else if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                    {
                        data = dataProp;
                    }
                    else
                    {
                        continue;
                    }

                    foreach (var rel in data.EnumerateArray())
                    {
                        var id = GetIntProperty(rel, "id");
                        var name = GetStringProperty(rel, "name") ?? string.Empty;
                        if (id.HasValue && id.Value > 0)
                        {
                            candidates.Add((id.Value, name));
                        }
                    }

                    if (candidates.Count > 0)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug(
                        $"[JamaConnect] Relationship type discovery failed for endpoint '{endpoint}': {ex.Message}");
                }
            }

            return candidates
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<int?> GetPreferredRelationshipTypeIdAsync(int projectId, CancellationToken cancellationToken)
        {
            if (projectId <= 0)
            {
                return null;
            }

            lock (_projectRelationshipTypeCache)
            {
                if (_projectRelationshipTypeCache.TryGetValue(projectId, out var cachedId))
                {
                    return cachedId;
                }
            }

            var candidates = await GetRelationshipTypeCandidatesAsync(projectId, cancellationToken);

            int? selected = null;
            if (candidates.Count > 0)
            {
                var priorities = new[]
                {
                    "derived from",
                    "derives",
                    "traceability",
                    "traces to",
                    "satisfies",
                    "related"
                };

                foreach (var preferred in priorities)
                {
                    var hit = candidates.FirstOrDefault(c => c.Name.Contains(preferred, StringComparison.OrdinalIgnoreCase));
                    if (hit.Id > 0)
                    {
                        selected = hit.Id;
                        break;
                    }
                }

                selected ??= candidates[0].Id;
            }

            lock (_projectRelationshipTypeCache)
            {
                _projectRelationshipTypeCache[projectId] = selected;
            }

            if (selected.HasValue)
            {
                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[JamaConnect] Selected relationship type {selected.Value} for project {projectId}.");
            }

            return selected;
        }

        private async Task<bool> TryCreateRelationshipAsync(
            int fromItemId,
            int toItemId,
            int? relationshipTypeId,
            CancellationToken cancellationToken)
        {
            if (fromItemId <= 0 || toItemId <= 0 || !relationshipTypeId.HasValue)
            {
                return false;
            }

            try
            {
                var requestBody = new
                {
                    fromItem = fromItemId,
                    toItem = toItemId,
                    relationshipType = relationshipTypeId.Value
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/rest/v1/relationships", content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info(
                        $"[JamaConnect] Created relationship: fromItem={fromItemId}, toItem={toItemId}, relationshipType={relationshipTypeId.Value}");
                    return true;
                }

                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                if (error.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                TestCaseEditorApp.Services.Logging.Log.Warn(
                    $"[JamaConnect] Relationship create failed ({response.StatusCode}) fromItem={fromItemId}, toItem={toItemId}, relationshipType={relationshipTypeId.Value}: {error}");
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn(
                    $"[JamaConnect] Relationship create exception fromItem={fromItemId}, toItem={toItemId}, relationshipType={relationshipTypeId}: {ex.Message}");
                return false;
            }
        }

        private async Task<int?> ResolvePreferredRequirementPlacementContainerAsync(int projectId, int requirementItemTypeId, int? fallbackParentContainerId, CancellationToken cancellationToken)
        {
            if (_requirementPlacementContainerCache.TryGetValue(projectId, out var cachedContainerId))
            {
                return cachedContainerId;
            }

            var candidates = await GetRequirementContainerCandidatesAsync(projectId, cancellationToken);
            if (candidates.Count > 0)
            {
                var topCandidate = candidates[0];
                if (topCandidate.Score >= 200)
                {
                    _requirementPlacementContainerCache[projectId] = topCandidate.Id;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Using hierarchy-preferred requirement container {topCandidate.Id} ('{topCandidate.Name}') for project {projectId}.");
                    return topCandidate.Id;
                }
            }

            var createdOrFoundContainerId = await EnsurePreferredRequirementContainerExistsAsync(projectId, requirementItemTypeId, cancellationToken);
            if (createdOrFoundContainerId.HasValue)
            {
                _requirementPlacementContainerCache[projectId] = createdOrFoundContainerId.Value;
                return createdOrFoundContainerId.Value;
            }

            if (fallbackParentContainerId.HasValue && fallbackParentContainerId.Value > 0)
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Falling back to provided requirement container {fallbackParentContainerId.Value} for project {projectId}.");
                return fallbackParentContainerId.Value;
            }

            return null;
        }

        private async Task<int?> EnsurePreferredRequirementContainerExistsAsync(int projectId, int requirementItemTypeId, CancellationToken cancellationToken)
        {
            try
            {
                var nodes = await LoadProjectItemNodesAsync(projectId, cancellationToken);
                if (nodes.Count == 0)
                {
                    return null;
                }

                var systemsNode = nodes.Values
                    .Where(n => n.Name.Contains("systems", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n.ParentId.HasValue ? 1 : 0)
                    .ThenBy(n => n.Id)
                    .FirstOrDefault();

                if (systemsNode.Id == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Could not find 'Systems' component in project {projectId}; cannot auto-create preferred requirement path.");
                    return null;
                }

                var requirementsNode = nodes.Values
                    .Where(n => n.ParentId == systemsNode.Id && n.Name.Equals("Requirements", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n.Id)
                    .FirstOrDefault();

                int? requirementsParentId = requirementsNode.Id > 0 ? requirementsNode.Id : null;
                if (!requirementsParentId.HasValue)
                {
                    requirementsParentId = await CreateContainerItemAsync(projectId, 55, "Requirements", systemsNode.Id, cancellationToken);
                    if (!requirementsParentId.HasValue)
                    {
                        return null;
                    }

                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Auto-created 'Requirements' container under 'Systems' (ID {requirementsParentId.Value}).");
                }

                // Requirements should be created directly under Systems/Requirements.
                return requirementsParentId.Value;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed ensuring preferred requirement container path: {ex.Message}");
                return null;
            }
        }

        private async Task<int?> CreateContainerItemAsync(int projectId, int itemTypeId, string name, int parentId, CancellationToken cancellationToken)
        {
            var body = new
            {
                project = projectId,
                itemType = itemTypeId,
                location = new
                {
                    parent = parentId
                },
                fields = new
                {
                    name
                }
            };

            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/rest/v1/items", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed to create container '{name}' (itemType {itemTypeId}) under parent {parentId}: {response.StatusCode} - {error}");
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            return TryExtractCreatedItemInfo(response, responseContent, out var createdId, out _, out _) ? createdId : null;
        }

        private static bool TryExtractCreatedItemInfo(HttpResponseMessage response, string responseContent, out int id, out string? documentKey, out string? globalId)
        {
            if (TryExtractCreatedItemInfo(responseContent, out id, out documentKey, out globalId))
            {
                return true;
            }

            if (TryExtractCreatedItemIdFromResponseHeaders(response, out id))
            {
                documentKey = null;
                globalId = null;
                return true;
            }

            id = 0;
            documentKey = null;
            globalId = null;
            return false;
        }

        private static bool TryExtractCreatedItemInfo(string responseContent, out int id, out string? documentKey, out string? globalId)
        {
            id = 0;
            documentKey = null;
            globalId = null;

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;

                static bool TryGetId(JsonElement element, out int parsedId)
                {
                    parsedId = 0;
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    if (element.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out parsedId))
                    {
                        return parsedId > 0;
                    }

                    if (element.TryGetProperty("id", out idProp) &&
                        idProp.ValueKind == JsonValueKind.String &&
                        int.TryParse(idProp.GetString(), out parsedId))
                    {
                        return parsedId > 0;
                    }

                    return false;
                }

                static void ReadKeys(JsonElement element, out string? parsedDocumentKey, out string? parsedGlobalId)
                {
                    parsedDocumentKey = null;
                    parsedGlobalId = null;
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        return;
                    }

                    if (element.TryGetProperty("documentKey", out var docKeyProp))
                    {
                        parsedDocumentKey = docKeyProp.GetString();
                    }

                    if (element.TryGetProperty("globalId", out var globalIdProp))
                    {
                        parsedGlobalId = globalIdProp.GetString();
                    }
                }

                if (TryGetId(root, out id))
                {
                    ReadKeys(root, out documentKey, out globalId);
                    return id > 0;
                }

                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data))
                {
                    if (TryGetId(data, out id))
                    {
                        ReadKeys(data, out documentKey, out globalId);
                        return id > 0;
                    }

                    if (data.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in data.EnumerateArray())
                        {
                            if (TryGetId(item, out id))
                            {
                                ReadKeys(item, out documentKey, out globalId);
                                return id > 0;
                            }
                        }
                    }
                }

                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("meta", out var meta) && TryGetId(meta, out id))
                {
                    ReadKeys(root, out documentKey, out globalId);
                    return id > 0;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool TryExtractCreatedItemIdFromResponseHeaders(HttpResponseMessage response, out int createdId)
        {
            createdId = 0;

            if (response.Headers.Location != null && TryExtractTrailingNumber(response.Headers.Location.ToString(), out createdId))
            {
                return createdId > 0;
            }

            if (response.Content?.Headers?.ContentLocation != null &&
                TryExtractTrailingNumber(response.Content.Headers.ContentLocation.ToString(), out createdId))
            {
                return createdId > 0;
            }

            foreach (var headerName in new[] { "X-Resource-Id", "X-Item-Id" })
            {
                if (!response.Headers.TryGetValues(headerName, out var values))
                {
                    continue;
                }

                var value = values.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (int.TryParse(value, out createdId) && createdId > 0)
                {
                    return true;
                }

                if (TryExtractTrailingNumber(value, out createdId) && createdId > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractTrailingNumber(string value, out int number)
        {
            number = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var match = Regex.Match(value, @"(\d+)(?!.*\d)");
            return match.Success && int.TryParse(match.Groups[1].Value, out number) && number > 0;
        }

        private async Task<Dictionary<int, (int Id, string Name, int ItemType, int? ParentId)>> LoadProjectItemNodesAsync(int projectId, CancellationToken cancellationToken)
        {
            var nodes = new Dictionary<int, (int Id, string Name, int ItemType, int? ParentId)>();
            var startAt = 0;
            var hasMore = true;
            const int pageSize = 50;

            while (hasMore)
            {
                var includeUrl = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults={pageSize}&startAt={startAt}&include=createdBy,modifiedBy,createdDate,modifiedDate";
                var basicUrl = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults={pageSize}&startAt={startAt}";

                var response = await _httpClient.GetAsync(includeUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    // Some Jama instances reject include/startAt combinations intermittently; retry with basic route.
                    response = await _httpClient.GetAsync(basicUrl, cancellationToken);
                }

                if (!response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Could not enumerate project items for {projectId}: {response.StatusCode}");
                    break;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                var pageCount = 0;
                foreach (var item in data.EnumerateArray())
                {
                    pageCount++;
                    if (!item.TryGetProperty("id", out var idProp) || !idProp.TryGetInt32(out var id))
                    {
                        continue;
                    }

                    if (!item.TryGetProperty("itemType", out var itemTypeProp) || !itemTypeProp.TryGetInt32(out var itemType))
                    {
                        continue;
                    }

                    string name = string.Empty;
                    int? parentId = null;

                    if (item.TryGetProperty("fields", out var fieldsObj) && fieldsObj.ValueKind == JsonValueKind.Object)
                    {
                        if (fieldsObj.TryGetProperty("name", out var nameProp))
                        {
                            name = nameProp.GetString() ?? string.Empty;
                        }

                        if (fieldsObj.TryGetProperty("parentId", out var parentIdProp) && parentIdProp.TryGetInt32(out var parsedParentId))
                        {
                            parentId = parsedParentId;
                        }
                    }

                    if (!parentId.HasValue &&
                        item.TryGetProperty("location", out var locationObj) && locationObj.ValueKind == JsonValueKind.Object &&
                        locationObj.TryGetProperty("parent", out var parentObj) && parentObj.ValueKind == JsonValueKind.Object &&
                        parentObj.TryGetProperty("item", out var parentItemProp) && parentItemProp.TryGetInt32(out var parentItemId))
                    {
                        parentId = parentItemId;
                    }

                    nodes[id] = (id, string.IsNullOrWhiteSpace(name) ? $"Item {id}" : name, itemType, parentId);
                }

                if (pageCount == 0)
                {
                    break;
                }

                startAt += pageCount;
                if (doc.RootElement.TryGetProperty("meta", out var metaObj) &&
                    metaObj.ValueKind == JsonValueKind.Object &&
                    metaObj.TryGetProperty("pageInfo", out var pageInfo) &&
                    pageInfo.ValueKind == JsonValueKind.Object &&
                    pageInfo.TryGetProperty("totalResults", out var totalResultsProp) &&
                    totalResultsProp.TryGetInt32(out var totalResults))
                {
                    hasMore = startAt < totalResults;
                }
                else
                {
                    hasMore = pageCount >= pageSize;
                }
            }

            return nodes;
        }

        /// <summary>
        /// TEMP troubleshooting utility: delete the "Common Requirements" folder and all descendants
        /// from project 686 only. Leaf-first delete order is used to satisfy hierarchy constraints.
        /// </summary>
        public async Task<(bool Success, string Message, int DeletedCount, int? FolderId)> DeleteCommonRequirementsFolderForProject686Async(
            int projectId,
            CancellationToken cancellationToken = default)
        {
            if (projectId != 686)
            {
                return (false, "Safety guard: this temporary operation is restricted to project 686.", 0, null);
            }

            return await WithRetryAsync(async () =>
            {
                await EnsureAccessTokenAsync();

                var nodes = await LoadProjectItemNodesAsync(projectId, cancellationToken);
                if (nodes.Count == 0)
                {
                    return (false, "No project items were loaded; aborting folder deletion.", 0, (int?)null);
                }

                var matchingFolders = nodes.Values
                    .Where(node => IsRequirementContainerItemType(node.ItemType) &&
                                   node.Name.Equals("Common Requirements", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingFolders.Count == 0)
                {
                    return (false, "Folder 'Common Requirements' was not found in project 686.", 0, (int?)null);
                }

                // If multiple matches exist, delete the one with the largest subtree to target the primary container.
                var childrenByParent = nodes.Values
                    .Where(node => node.ParentId.HasValue)
                    .GroupBy(node => node.ParentId!.Value)
                    .ToDictionary(group => group.Key, group => group.Select(n => n.Id).ToList());

                int CountSubtreeSize(int rootId)
                {
                    var count = 1;
                    if (!childrenByParent.TryGetValue(rootId, out var children))
                    {
                        return count;
                    }

                    foreach (var child in children)
                    {
                        count += CountSubtreeSize(child);
                    }

                    return count;
                }

                var targetFolder = matchingFolders
                    .OrderByDescending(folder => CountSubtreeSize(folder.Id))
                    .First();

                var subtreeIds = BuildSubtreeIds(targetFolder.Id, childrenByParent);
                var deleteOrder = subtreeIds
                    .OrderByDescending(id => GetDepth(id, nodes))
                    .ThenByDescending(id => id)
                    .ToList();

                var unlockedCount = 0;
                var unlockFailedCount = 0;
                foreach (var itemId in deleteOrder)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Jama may reject unlock attempts for system-enforced or permission-based locks.
                    // Treat unlock as informational only and continue straight to delete attempts.
                    var unlockResult = await TryUnlockItemAsync(itemId, cancellationToken);
                    if (unlockResult)
                    {
                        unlockedCount++;
                    }
                    else
                    {
                        unlockFailedCount++;
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Unlock pre-pass failed for item {itemId}; continuing to delete attempt.");
                    }
                }

                var deletedCount = 0;
                foreach (var itemId in deleteOrder)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var deleteUrl = $"{_baseUrl}/rest/v1/items/{itemId}";
                    using var response = await _httpClient.DeleteAsync(deleteUrl, cancellationToken);

                    if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        deletedCount++;
                        continue;
                    }

                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Delete failed for item {itemId}: {response.StatusCode} - {TruncateForLog(error, 400)}");
                    var unlockDetail = unlockFailedCount > 0
                        ? $" Unlock attempts failed for {unlockFailedCount} item(s)."
                        : string.Empty;
                    return (false, $"Delete failed at item {itemId}: {response.StatusCode}.{unlockDetail} Response: {TruncateForLog(error, 400)}", deletedCount, targetFolder.Id);
                }

                var successMessage =
                    $"Deleted 'Common Requirements' subtree in project 686. Removed {deletedCount} item(s). " +
                    $"Unlock pre-pass: {unlockedCount} succeeded, {unlockFailedCount} failed; delete continued anyway.";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] {successMessage} FolderId={targetFolder.Id}");
                return (true, successMessage, deletedCount, targetFolder.Id);
            });
        }

        private async Task<bool> TryUnlockItemAsync(int itemId, CancellationToken cancellationToken)
        {
            try
            {
                var url = $"{_baseUrl}/rest/v1/items/{itemId}";
                object[] payloads =
                {
                    new { locked = false },
                    new { fields = new { locked = false } },
                    new { locked = false, fields = new { locked = false } }
                };

                foreach (var payload in payloads)
                {
                    var json = JsonSerializer.Serialize(payload);
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var putResponse = await _httpClient.PutAsync(url, content, cancellationToken);
                    if (putResponse.IsSuccessStatusCode || putResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        return true;
                    }

                    var putText = await putResponse.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Debug(
                        $"[JamaConnect] Unlock PUT attempt for item {itemId} returned {putResponse.StatusCode}: {TruncateForLog(putText, 300)}");

                    using var patchResponse = await _httpClient.PatchAsync(url, content);
                    if (patchResponse.IsSuccessStatusCode || patchResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        return true;
                    }

                    var patchText = await patchResponse.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Debug(
                        $"[JamaConnect] Unlock PATCH attempt for item {itemId} returned {patchResponse.StatusCode}: {TruncateForLog(patchText, 300)}");
                }

                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Unlock attempts exhausted for item {itemId}.");
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Unlock exception for item {itemId}: {ex.Message}");
                return false;
            }
        }

        private static HashSet<int> BuildSubtreeIds(int rootId, Dictionary<int, List<int>> childrenByParent)
        {
            var result = new HashSet<int> { rootId };
            var stack = new Stack<int>();
            stack.Push(rootId);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!childrenByParent.TryGetValue(current, out var children))
                {
                    continue;
                }

                foreach (var child in children)
                {
                    if (result.Add(child))
                    {
                        stack.Push(child);
                    }
                }
            }

            return result;
        }

        private static int GetDepth(int id, Dictionary<int, (int Id, string Name, int ItemType, int? ParentId)> nodes)
        {
            var depth = 0;
            var current = id;
            var seen = new HashSet<int>();

            while (nodes.TryGetValue(current, out var node) && node.ParentId.HasValue)
            {
                if (!seen.Add(current))
                {
                    break;
                }

                current = node.ParentId.Value;
                depth++;
            }

            return depth;
        }

        private async Task<List<(int Id, string Name, int Score)>> GetRequirementContainerCandidatesAsync(int projectId, CancellationToken cancellationToken)
        {
            var candidates = new List<(int Id, string Name, int Score)>();

            try
            {
                var nodes = await LoadProjectItemNodesAsync(projectId, cancellationToken);
                if (nodes.Count == 0)
                {
                    return candidates;
                }

                foreach (var node in nodes.Values)
                {
                    // Folder (55) and Set (54) are preferred requirement containers.
                    if (!IsRequirementContainerItemType(node.ItemType))
                    {
                        continue;
                    }

                    var lower = node.Name.ToLowerInvariant();
                    var ancestry = BuildAncestry(nodes, node.Id)
                        .Select(a => a.ToLowerInvariant())
                        .ToList();
                    var score = CalculateRequirementContainerScore(lower, ancestry);

                    // Keep broad candidates but rank requirement-like containers first.
                    candidates.Add((node.Id, node.Name, score));
                }

                candidates = candidates
                    .OrderByDescending(c => c.Score)
                    .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .ToList();

                if (candidates.Count > 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Discovered {candidates.Count} requirement container candidates in project {projectId}. Top candidate: {candidates[0].Id} '{candidates[0].Name}' (score {candidates[0].Score}).");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Error discovering requirement containers for project {projectId}: {ex.Message}");
            }

            return candidates;
        }

        private static bool IsRequirementContainerItemType(int itemType)
        {
            return itemType == 55 || itemType == 54;
        }

        private static int CalculateRequirementContainerScore(string lowerName, List<string> ancestry)
        {
            var hasSystemsInPath = ancestry.Any(a => a.Contains("systems"));
            var hasRequirementsInPath = ancestry.Any(a => a.Contains("requirements") || a.Contains("requirement"));
            var hasSoftwareInPath = ancestry.Any(a => a.Contains("software"));
            var hasHardwareInPath = ancestry.Any(a => a.Contains("hardware"));
            var hasDhaInPath = ancestry.Any(a => a.Contains("dha"));
            var hasDhaRevisedInPath = ancestry.Any(a => a.Contains("dha revised"));

            var hasPreferredDhaPath = hasSystemsInPath
                && hasRequirementsInPath
                && hasDhaInPath
                && hasDhaRevisedInPath;

            var score = 0;
            if (hasSystemsInPath) score += 180;
            if (hasRequirementsInPath) score += 180;
            if (hasDhaInPath) score += 120;
            if (hasDhaRevisedInPath) score += 200;
            if (hasPreferredDhaPath) score += 600;
            if (lowerName.Contains("requirement")) score += 120;
            if (lowerName.Equals("requirements")) score += 160;
            if (lowerName.Contains("dha revised")) score += 220;
            if (lowerName.Equals("dha")) score += 80;
            if (lowerName.Contains("system")) score += 20;
            if (lowerName.Contains("spec")) score += 20;
            if (lowerName.Contains("test") || lowerName.Contains("verification") || lowerName.Contains("procedure") || lowerName.Contains("case")) score -= 140;
            if (hasSoftwareInPath && !hasSystemsInPath) score -= 50;
            if (hasHardwareInPath && !hasSystemsInPath) score -= 50;
            if (lowerName.Contains("artifact") || lowerName.Contains("drawing") || lowerName.Contains("document")) score -= 60;

            return score;
        }

        private static List<string> BuildAncestry(Dictionary<int, (int Id, string Name, int ItemType, int? ParentId)> nodes, int startId)
        {
            var ancestry = new List<string>();
            var visited = new HashSet<int>();
            var currentId = startId;

            while (nodes.TryGetValue(currentId, out var current) && visited.Add(currentId))
            {
                ancestry.Add(current.Name);
                if (!current.ParentId.HasValue)
                {
                    break;
                }

                currentId = current.ParentId.Value;
            }

            return ancestry;
        }

        private async Task<int?> GetFirstPicklistOptionIdAsync(int projectId, int itemTypeId, string fieldName, CancellationToken cancellationToken)
        {
            var picklistResult = await GetPicklistOptionsForFieldAsync(projectId, itemTypeId, fieldName, cancellationToken);
            if (!picklistResult.EndpointAvailable)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed to load picklist options for field '{fieldName}': {picklistResult.Error}");
            }

            return picklistResult.FirstOptionId;
        }

        private async Task ApplyRequirementEnrichedFieldsAsync(
            int projectId,
            int itemTypeId,
            Requirement requirement,
            Dictionary<string, object?> fields,
            CancellationToken cancellationToken)
        {
            await ApplyValidationMethodsFieldAsync(projectId, itemTypeId, requirement, fields, cancellationToken);
            await ApplyVerificationMethodsFieldAsync(projectId, itemTypeId, requirement, fields, cancellationToken);
            await ApplyRequirementTypeFieldAsync(projectId, itemTypeId, requirement, fields, cancellationToken);
            await ApplyStatusFieldAsync(projectId, itemTypeId, requirement, fields, cancellationToken);
            ApplyAllocationField(requirement, fields);
            ApplyDerivedRequirementDefaultsForItemType193(requirement, itemTypeId, fields);
        }

        private static void ApplyUserSelectionRequiredFallbackFields(
            Requirement requirement,
            int itemTypeId,
            Dictionary<string, object?> fields)
        {
            // Populate key writable text fields. Missing values are marked for human follow-up.
            TrySetTextFieldOrPlaceholder(fields, itemTypeId, "heading", requirement.Heading);
            TrySetTextFieldOrPlaceholder(fields, itemTypeId, "rationale", requirement.Rationale);
            TrySetTextFieldOrPlaceholder(fields, itemTypeId, "project_defined", requirement.ProjectDefined);
            TrySetTextFieldOrPlaceholder(fields, itemTypeId, "change_driver", requirement.ChangeDriver);
            TrySetTextFieldOrPlaceholder(fields, itemTypeId, "compliance_rationale", requirement.ComplianceRationale);
            TrySetTextFieldOrPlaceholder(fields, itemTypeId, "safety_rationale", requirement.SafetyRationale);
            TrySetTextFieldOrPlaceholder(fields, itemTypeId, "security_rationale", requirement.SecurityRationale);
            TrySetTextFieldOrPlaceholder(fields, itemTypeId, "robust_rationale", requirement.RobustRationale);
        }

        private async Task ApplyRequirementTypeFieldAsync(
            int projectId,
            int itemTypeId,
            Requirement requirement,
            Dictionary<string, object?> fields,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(requirement.RequirementType))
            {
                return;
            }

            // Prefer explicit requirement_type field if available.
            var typeFieldName = ResolveFieldNameForItemType(fields, "requirement_type", itemTypeId);
            var typeOptionId = await ResolvePicklistOptionIdByCandidatesAsync(
                projectId,
                itemTypeId,
                typeFieldName,
                new[] { requirement.RequirementType },
                cancellationToken);

            if (typeOptionId.HasValue)
            {
                fields[typeFieldName] = typeOptionId.Value;
                return;
            }

            // Fallback to common lookup1 mapping for item type 193 style schemas.
            if (fields.ContainsKey("lookup1"))
            {
                var lookupOptionId = await ResolvePicklistOptionIdByCandidatesAsync(
                    projectId,
                    itemTypeId,
                    "lookup1",
                    new[] { requirement.RequirementType },
                    cancellationToken);

                if (lookupOptionId.HasValue)
                {
                    fields["lookup1"] = lookupOptionId.Value;
                }
            }
        }

        private async Task ApplyStatusFieldAsync(
            int projectId,
            int itemTypeId,
            Requirement requirement,
            Dictionary<string, object?> fields,
            CancellationToken cancellationToken)
        {
            var status = !string.IsNullOrWhiteSpace(requirement.Status)
                ? requirement.Status
                : requirement.RelationshipStatus;

            if (string.IsNullOrWhiteSpace(status))
            {
                return;
            }

            var statusFieldName = ResolveFieldNameForItemType(fields, "status", itemTypeId);
            var statusOptionId = await ResolvePicklistOptionIdByCandidatesAsync(
                projectId,
                itemTypeId,
                statusFieldName,
                new[] { status },
                cancellationToken);

            if (statusOptionId.HasValue)
            {
                fields[statusFieldName] = statusOptionId.Value;
            }
        }

        private static void TrySetTextFieldOrPlaceholder(
            Dictionary<string, object?> fields,
            int itemTypeId,
            string baseFieldName,
            string? fieldValue)
        {
            var fieldName = ResolveExistingFieldNameForItemType(fields, baseFieldName, itemTypeId);
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                return;
            }

            if (!IsLikelyTextField(fields.TryGetValue(fieldName!, out var existing) ? existing : null))
            {
                // Picklists/lookups remain blank if no confident mapping is available.
                return;
            }

            fields[fieldName!] = string.IsNullOrWhiteSpace(fieldValue)
                ? "User Selection Required"
                : fieldValue.Trim();
        }

        private static void ApplyDerivedRequirementDefaultsForItemType193(
            Requirement requirement,
            int itemTypeId,
            Dictionary<string, object?> fields)
        {
            if (itemTypeId != 193 || !requirement.IsDerivedFromATP)
            {
                return;
            }

            // Decoder-ring defaults validated against picklists for item type 193.
            fields["lookup1"] = 1608; // Requirement Type = System
            fields["lookup3"] = 1619; // Derived Requirement = Yes

            var validationField = ResolveFieldNameForItemType(fields, "validation_methods", itemTypeId);
            fields[validationField] = new[] { 1649 }; // Validation Method/s = Traceability

            var verificationField = ResolveFieldNameForItemType(fields, "verification_methods", itemTypeId);
            fields[verificationField] = new[] { 1612 }; // Verification Method/s = Unassigned

            if (!fields.TryGetValue("text2", out var existingAllocation) || string.IsNullOrWhiteSpace(existingAllocation?.ToString()))
            {
                fields["text2"] = "Systems"; // Allocation/s
            }
        }

        private async Task ApplyValidationMethodsFieldAsync(
            int projectId,
            int itemTypeId,
            Requirement requirement,
            Dictionary<string, object?> fields,
            CancellationToken cancellationToken)
        {
            var validationMethods = requirement.ValidationMethods
                .Where(m => m != ValidationMethod.Unassigned)
                .Distinct()
                .ToList();

            if (validationMethods.Count == 0)
            {
                return;
            }

            var fieldName = ResolveFieldNameForItemType(fields, "validation_methods", itemTypeId);
            var resolvedIds = new List<int>();

            foreach (var method in validationMethods)
            {
                var optionId = await ResolvePicklistOptionIdByCandidatesAsync(
                    projectId,
                    itemTypeId,
                    fieldName,
                    GetValidationMethodPicklistNames(method),
                    cancellationToken);

                if (optionId.HasValue)
                {
                    resolvedIds.Add(optionId.Value);
                }
            }

            if (resolvedIds.Count == 0 && validationMethods.Contains(ValidationMethod.Traceability))
            {
                // Keep known stable fallback for Traceability if tenant lookup APIs are unavailable.
                resolvedIds.Add(1649);
            }

            if (resolvedIds.Count > 0)
            {
                fields[fieldName] = resolvedIds.Distinct().ToArray();
                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[JamaConnect] Applied validation methods for '{requirement.Item ?? requirement.Name}': {string.Join(", ", validationMethods)}");
            }
        }

        private async Task ApplyVerificationMethodsFieldAsync(
            int projectId,
            int itemTypeId,
            Requirement requirement,
            Dictionary<string, object?> fields,
            CancellationToken cancellationToken)
        {
            var verificationMethods = requirement.VerificationMethods
                .Where(m => m != VerificationMethod.Unassigned)
                .Distinct()
                .ToList();

            if (verificationMethods.Count == 0)
            {
                return;
            }

            var fieldName = ResolveFieldNameForItemType(fields, "verification_methods", itemTypeId);
            var resolvedIds = new List<int>();

            foreach (var method in verificationMethods)
            {
                var optionId = await ResolvePicklistOptionIdByCandidatesAsync(
                    projectId,
                    itemTypeId,
                    fieldName,
                    GetVerificationMethodPicklistNames(method),
                    cancellationToken);

                if (optionId.HasValue)
                {
                    resolvedIds.Add(optionId.Value);
                }
            }

            if (resolvedIds.Count > 0)
            {
                fields[fieldName] = resolvedIds.Distinct().ToArray();
                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[JamaConnect] Applied verification methods for '{requirement.Item ?? requirement.Name}': {string.Join(", ", verificationMethods)}");
            }
        }

        private static void ApplyAllocationField(Requirement requirement, Dictionary<string, object?> fields)
        {
            var allocation = requirement.Allocation switch
            {
                AllocationTarget.Hardware => "Hardware",
                AllocationTarget.Software => "Software",
                AllocationTarget.Both => "Both",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(allocation) && !string.IsNullOrWhiteSpace(requirement.Allocations))
            {
                allocation = requirement.Allocations;
            }

            if (string.IsNullOrWhiteSpace(allocation))
            {
                return;
            }

            fields["text2"] = allocation;
        }

        private static string ResolveFieldNameForItemType(
            Dictionary<string, object?> fields,
            string baseFieldName,
            int itemTypeId)
        {
            var exact = $"{baseFieldName}${itemTypeId}";
            if (fields.ContainsKey(exact))
            {
                return exact;
            }

            var suffixed = fields.Keys.FirstOrDefault(k =>
                k.StartsWith(baseFieldName + "$", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(suffixed))
            {
                return suffixed;
            }

            return exact;
        }

        private static string? ResolveExistingFieldNameForItemType(
            Dictionary<string, object?> fields,
            string baseFieldName,
            int itemTypeId)
        {
            var exact = $"{baseFieldName}${itemTypeId}";
            if (fields.ContainsKey(exact))
            {
                return exact;
            }

            var bare = fields.Keys.FirstOrDefault(k =>
                string.Equals(k, baseFieldName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(bare))
            {
                return bare;
            }

            return fields.Keys.FirstOrDefault(k =>
                k.StartsWith(baseFieldName + "$", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsLikelyTextField(object? value)
        {
            if (value == null)
            {
                return false;
            }

            if (value is string)
            {
                return true;
            }

            if (value is JsonElement json)
            {
                return json.ValueKind == JsonValueKind.String || json.ValueKind == JsonValueKind.Null || json.ValueKind == JsonValueKind.Undefined;
            }

            return value is not Array && value is not IEnumerable<int>;
        }

        private static string BuildSectionTraceRequirementName(
            Requirement requirement,
            string sectionKey,
            int sectionOrdinal,
            int sectionTotal)
        {
            var prefix = sectionKey.Equals("UNK", StringComparison.OrdinalIgnoreCase)
                ? $"UNK {sectionOrdinal}"
                : sectionTotal > 1
                    ? $"{sectionKey}.{sectionOrdinal}"
                    : sectionKey;

            var title = BuildSectionTitle(requirement);
            if (string.IsNullOrWhiteSpace(title))
            {
                return prefix;
            }

            return NormalizeRequirementName($"{prefix} - {title}");
        }

        private static string BuildSectionTitle(Requirement requirement)
        {
            var candidates = new[]
            {
                requirement.SetName,
                GetFolderLeafName(requirement.FolderPath),
                requirement.RequirementType,
                requirement.Heading
            };

            var chosen = candidates.FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(c)
                && !LooksLikeSourcePrefix(c, requirement.SourceSection)
                && !LooksLikeSourcePrefix(c, requirement.SourcePrefix)
                && !LooksLikeSyntheticDocumentId(c)) ?? "Requirement";

            var cleanedTitle = RemoveSourcePrefix(chosen);
            cleanedTitle = RemoveSourcePrefix(cleanedTitle);
            cleanedTitle = RemoveDuplicateLeadingToken(cleanedTitle);
            var normalized = NormalizeRequirementName(cleanedTitle);

            var description = requirement.Description?.Trim();
            if (!string.IsNullOrWhiteSpace(description) &&
                string.Equals(normalized, NormalizeRequirementName(description), StringComparison.OrdinalIgnoreCase))
            {
                return "Requirement";
            }

            return string.IsNullOrWhiteSpace(normalized) ? "Requirement" : normalized;
        }

        private static bool LooksLikeSyntheticDocumentId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Regex.IsMatch(value.Trim(), @"^DOC-\d{2,}$", RegexOptions.IgnoreCase);
        }

        private static string GetNormalizedSectionKey(Requirement requirement)
        {
            var isDeterministicFallback = requirement.TagList?.Any(tag =>
                    tag.Equals("DeterministicFallback", StringComparison.OrdinalIgnoreCase)) == true
                || (!string.IsNullOrWhiteSpace(requirement.RequirementType)
                    && requirement.RequirementType.StartsWith("Deterministic", StringComparison.OrdinalIgnoreCase));

            if (isDeterministicFallback)
            {
                return "UNK";
            }

            var candidates = new[]
            {
                requirement.SourcePrefix,
                requirement.SourceSection,
                requirement.Heading,
                requirement.SetName,
                requirement.FolderPath
            };

            foreach (var candidate in candidates)
            {
                var section = ExtractSectionIdentifier(candidate);
                if (!string.IsNullOrWhiteSpace(section))
                {
                    return section!;
                }
            }

            return "UNK";
        }

        private static string? ExtractSectionIdentifier(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var sourceLabeled = Regex.Match(value, @"\b(?:source|section|sec\.?|clause|id)\s*:\s*(?<sec>\d+(?:\.\d+)+|[A-Za-z][A-Za-z0-9]*(?:[_-][A-Za-z0-9]+)+)\b", RegexOptions.IgnoreCase);
            if (sourceLabeled.Success)
            {
                return sourceLabeled.Groups["sec"].Value.Trim().Trim('.');
            }

            var explicitSection = Regex.Match(value, @"\b(?:section|sec\.?|clause|id)\s*(?<sec>\d+(?:\.\d+)+|[A-Za-z][A-Za-z0-9]*(?:[_-][A-Za-z0-9]+)+)\b", RegexOptions.IgnoreCase);
            if (explicitSection.Success)
            {
                return explicitSection.Groups["sec"].Value.Trim().Trim('.');
            }

            var headingStyle = Regex.Match(value, @"(?:^|[\r\n])\s*(?<sec>\d+(?:\.\d+)+)\b", RegexOptions.IgnoreCase);
            if (headingStyle.Success)
            {
                return headingStyle.Groups["sec"].Value.Trim().Trim('.');
            }

            if (!value.Any(char.IsWhiteSpace))
            {
                var identifier = Regex.Match(value, @"^(?<sec>[A-Za-z][A-Za-z0-9]*(?:[_-][A-Za-z0-9]+)+)$", RegexOptions.IgnoreCase);
                if (identifier.Success)
                {
                    return identifier.Groups["sec"].Value.Trim().Trim('.');
                }
            }

            var standalone = Regex.Match(value, @"\b(?<sec>\d+(?:\.\d+){1,})\b");
            if (standalone.Success)
            {
                return standalone.Groups["sec"].Value.Trim().Trim('.');
            }

            return null;
        }

        private static bool LooksLikeSourcePrefix(string? value, string? sourcePrefix)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var extracted = ExtractSectionIdentifier(value);
            if (string.IsNullOrWhiteSpace(extracted))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(sourcePrefix))
            {
                return true;
            }

            return string.Equals(extracted, sourcePrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string RemoveSourcePrefix(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            var stripped = Regex.Replace(trimmed, @"^(?:source|section|sec\.?|clause|id)?\s*[:\-–—]?\s*(?:\d+(?:\.\d+)+|[A-Za-z][A-Za-z0-9]*(?:[_-][A-Za-z0-9]+)+)[\s:\-–—]+", string.Empty, RegexOptions.IgnoreCase);
            return string.IsNullOrWhiteSpace(stripped) ? trimmed : stripped;
        }

        private static string RemoveDuplicateLeadingToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            var deduped = Regex.Replace(
                trimmed,
                @"^(?<token>[A-Za-z][A-Za-z0-9_-]{1,})\s*[-:–—]\s*\k<token>\b\s*[-:–—]?\s*",
                "${token} - ",
                RegexOptions.IgnoreCase);

            return deduped.Trim();
        }

        private static string NormalizeRequirementName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var name = Regex.Replace(value.Trim(), @"\s+", " ");
            name = Regex.Replace(name, @"[\r\n\t]+", " ");
            if (name.Length > 120)
            {
                name = name[..120].Trim();
            }

            return name;
        }

        private static string? GetFolderLeafName(string? folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return null;
            }

            var separators = new[] { '/', '\\' };
            var parts = folderPath.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? null : parts[^1].Trim();
        }

        private async Task<int?> ResolvePicklistOptionIdByCandidatesAsync(
            int projectId,
            int itemTypeId,
            string fieldName,
            IEnumerable<string> candidateNames,
            CancellationToken cancellationToken)
        {
            foreach (var candidateName in candidateNames.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var optionId = await GetPicklistOptionIdByNameAsync(
                    projectId,
                    itemTypeId,
                    fieldName,
                    candidateName,
                    cancellationToken);

                if (optionId.HasValue)
                {
                    return optionId;
                }
            }

            return null;
        }

        private static IEnumerable<string> GetValidationMethodPicklistNames(ValidationMethod method)
        {
            return method switch
            {
                ValidationMethod.EngineeringJudgement => new[] { "Engineering Judgement", "EngineeringJudgement" },
                ValidationMethod.NotApplicable => new[] { "Not Applicable", "N/A", "NotApplicable" },
                _ => new[] { method.ToString() }
            };
        }

        private static IEnumerable<string> GetVerificationMethodPicklistNames(VerificationMethod method)
        {
            return method switch
            {
                VerificationMethod.ServiceHistory => new[] { "Service History", "ServiceHistory" },
                VerificationMethod.TestUnintendedFunction => new[] { "Test for Unintended Function", "Test Unintended Function", "TestUnintendedFunction" },
                VerificationMethod.VerifiedAtAnotherLevel => new[] { "Verified at Another Level", "Verified At Another Level", "VerifiedAtAnotherLevel" },
                _ => new[] { method.ToString() }
            };
        }

        private async Task<int?> GetPicklistOptionIdByNameAsync(
            int projectId,
            int itemTypeId,
            string fieldName,
            string optionName,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(fieldName) || string.IsNullOrWhiteSpace(optionName))
            {
                return null;
            }

            foreach (var candidateField in BuildPicklistFieldCandidates(fieldName, itemTypeId))
            {
                try
                {
                    var endpointUrls = BuildPicklistEndpointCandidates(projectId, itemTypeId, candidateField);
                    foreach (var url in endpointUrls)
                    {
                        var response = await _httpClient.GetAsync(url, cancellationToken);
                        if (!response.IsSuccessStatusCode)
                        {
                            continue;
                        }

                        var json = await response.Content.ReadAsStringAsync(cancellationToken);
                        using var doc = JsonDocument.Parse(json);
                        var dataArray = TryGetPicklistDataArray(doc.RootElement);
                        if (!dataArray.HasValue || dataArray.Value.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        foreach (var option in dataArray.Value.EnumerateArray())
                        {
                            if (!option.TryGetProperty("id", out var idProp) || !idProp.TryGetInt32(out var optionId))
                            {
                                continue;
                            }

                            var candidateName = GetStringProperty(option, "name")
                                ?? GetStringProperty(option, "display")
                                ?? GetStringProperty(option, "value");

                            if (string.Equals(candidateName, optionName, StringComparison.OrdinalIgnoreCase))
                            {
                                return optionId;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug(
                        $"[JamaConnect] Failed resolving picklist option '{optionName}' for field '{candidateField}': {ex.Message}");
                }
            }

            return null;
        }

        private async Task<int?> GetFirstPicklistOptionIdByPicklistIdAsync(int picklistId, CancellationToken cancellationToken)
        {
            if (picklistId <= 0)
            {
                return null;
            }

            try
            {
                var url = $"{_baseUrl}/rest/v1/picklists/{picklistId}/options";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Picklist options endpoint failed for picklist {picklistId}: {response.StatusCode}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var dataArray = TryGetPicklistDataArray(doc.RootElement);
                if (!dataArray.HasValue || dataArray.Value.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                foreach (var option in dataArray.Value.EnumerateArray())
                {
                    if (option.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var optionId))
                    {
                        return optionId;
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Could not load picklist options for picklist {picklistId}: {ex.Message}");
            }

            return null;
        }

        private async Task<(bool EndpointAvailable, int OptionCount, int? FirstOptionId, string? FirstOptionName, HashSet<int> OptionIds, string? Error, string ResolvedFieldName)> GetPicklistOptionsForFieldAsync(
            int projectId,
            int itemTypeId,
            string fieldName,
            CancellationToken cancellationToken)
        {
            var optionIds = new HashSet<int>();
            int? firstOptionId = null;
            string? firstOptionName = null;
            string? lastError = null;

            foreach (var candidateField in BuildPicklistFieldCandidates(fieldName, itemTypeId))
            {
                try
                {
                    var endpointUrls = BuildPicklistEndpointCandidates(projectId, itemTypeId, candidateField);
                    foreach (var url in endpointUrls)
                    {
                        var response = await _httpClient.GetAsync(url, cancellationToken);
                        if (!response.IsSuccessStatusCode)
                        {
                            lastError = response.StatusCode.ToString();
                            continue;
                        }

                        var json = await response.Content.ReadAsStringAsync(cancellationToken);
                        using var doc = JsonDocument.Parse(json);
                        var dataArray = TryGetPicklistDataArray(doc.RootElement);
                        if (!dataArray.HasValue || dataArray.Value.ValueKind != JsonValueKind.Array)
                        {
                            lastError = "No data array returned.";
                            continue;
                        }

                        foreach (var option in dataArray.Value.EnumerateArray())
                        {
                            if (!option.TryGetProperty("id", out var idProp) || !idProp.TryGetInt32(out var optionId))
                            {
                                continue;
                            }

                            optionIds.Add(optionId);
                            if (!firstOptionId.HasValue)
                            {
                                firstOptionId = optionId;
                                if (option.TryGetProperty("name", out var nameProp))
                                {
                                    firstOptionName = nameProp.GetString();
                                }
                            }
                        }

                        if (optionIds.Count == 0)
                        {
                            lastError = "No picklist option entries returned.";
                            continue;
                        }

                        return (true, optionIds.Count, firstOptionId, firstOptionName, optionIds, null, candidateField);
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }

            return (false, 0, null, null, optionIds, lastError ?? "NotFound", fieldName);
        }

        private static IEnumerable<string> BuildPicklistFieldCandidates(string fieldName, int itemTypeId)
        {
            var candidates = new List<string>();

            static bool IsLookupAlias(string value)
            {
                if (!value.StartsWith("lookup", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var suffix = value.Substring("lookup".Length);
                return int.TryParse(suffix, out _);
            }

            if (!string.IsNullOrWhiteSpace(fieldName))
            {
                fieldName = fieldName.Trim();

                if (itemTypeId > 0 && !fieldName.Contains("$", StringComparison.Ordinal))
                {
                    // Prefer item-type-scoped custom field names first, then the raw alias.
                    candidates.Add($"{fieldName}${itemTypeId}");

                    if (IsLookupAlias(fieldName))
                    {
                        // Some Jama instances expose lookup fields with explicit item-type suffix only.
                        candidates.Add($"lookup{fieldName.Substring("lookup".Length)}${itemTypeId}");
                    }
                }

                candidates.Add(fieldName);
            }

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private IEnumerable<string> BuildPicklistEndpointCandidates(int projectId, int itemTypeId, string candidateField)
        {
            var encodedField = Uri.EscapeDataString(candidateField);

            // Different Jama deployments expose picklist lookup APIs with slightly different routes/query names.
            // Try known variants from most specific to broadest.
            return new[]
            {
                $"{_baseUrl}/rest/v1/projects/{projectId}/picklistoptions?filteredField={encodedField}",
                $"{_baseUrl}/rest/v1/projects/{projectId}/picklistoptions?fieldName={encodedField}",
                $"{_baseUrl}/rest/v1/picklistoptions?project={projectId}&filteredField={encodedField}",
                $"{_baseUrl}/rest/v1/picklistoptions?project={projectId}&fieldName={encodedField}",
                $"{_baseUrl}/rest/v1/itemtypes/{itemTypeId}/picklistoptions?filteredField={encodedField}",
                $"{_baseUrl}/rest/v1/itemtypes/{itemTypeId}/picklistoptions?fieldName={encodedField}"
            };
        }

        private static bool TryExtractFieldsPayloadFromItemType(JsonElement root, out string fieldsJson)
        {
            fieldsJson = string.Empty;

            JsonElement? FindFieldsArray(JsonElement node)
            {
                if (node.ValueKind == JsonValueKind.Object)
                {
                    if (node.TryGetProperty("fields", out var fieldsProp))
                    {
                        if (fieldsProp.ValueKind == JsonValueKind.Array)
                        {
                            return fieldsProp;
                        }

                        if (fieldsProp.ValueKind == JsonValueKind.Object &&
                            fieldsProp.TryGetProperty("data", out var nestedData) &&
                            nestedData.ValueKind == JsonValueKind.Array)
                        {
                            return nestedData;
                        }
                    }

                    if (node.TryGetProperty("fieldDefinitions", out var defsProp) && defsProp.ValueKind == JsonValueKind.Array)
                    {
                        return defsProp;
                    }

                    if (node.TryGetProperty("data", out var dataProp))
                    {
                        var fromData = FindFieldsArray(dataProp);
                        if (fromData.HasValue)
                        {
                            return fromData;
                        }
                    }

                    foreach (var property in node.EnumerateObject())
                    {
                        var fromProperty = FindFieldsArray(property.Value);
                        if (fromProperty.HasValue)
                        {
                            return fromProperty;
                        }
                    }
                }
                else if (node.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in node.EnumerateArray())
                    {
                        var fromElement = FindFieldsArray(element);
                        if (fromElement.HasValue)
                        {
                            return fromElement;
                        }
                    }
                }

                return null;
            }

            var fieldsArray = FindFieldsArray(root);
            if (!fieldsArray.HasValue)
            {
                return false;
            }

            var wrappedPayload = JsonSerializer.Serialize(new { data = fieldsArray.Value });
            fieldsJson = wrappedPayload;
            return true;
        }

        private static JsonElement? TryGetPicklistDataArray(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                return root;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                {
                    return dataProp;
                }

                if (root.TryGetProperty("results", out var resultsProp) && resultsProp.ValueKind == JsonValueKind.Array)
                {
                    return resultsProp;
                }
            }

            return null;
        }

        private static JsonElement? TryGetFieldsDataArray(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("fields", out var fieldsProp))
                {
                    if (fieldsProp.ValueKind == JsonValueKind.Array)
                    {
                        return fieldsProp;
                    }

                    if (fieldsProp.ValueKind == JsonValueKind.Object &&
                        fieldsProp.TryGetProperty("data", out var fieldsDataProp) &&
                        fieldsDataProp.ValueKind == JsonValueKind.Array)
                    {
                        return fieldsDataProp;
                    }
                }

                if (root.TryGetProperty("data", out var dataProp))
                {
                    if (dataProp.ValueKind == JsonValueKind.Array)
                    {
                        return dataProp;
                    }

                    var nestedFromData = TryGetFieldsDataArray(dataProp);
                    if (nestedFromData.HasValue)
                    {
                        return nestedFromData;
                    }
                }
            }

            return null;
        }

        private async Task<(bool EndpointAvailable, int OptionCount, int? FirstOptionId, string? FirstOptionName, HashSet<int> OptionIds, string? Error)> GetPicklistOptionsByPicklistIdAsync(
            int picklistId,
            CancellationToken cancellationToken)
        {
            var optionIds = new HashSet<int>();
            int? firstOptionId = null;
            string? firstOptionName = null;

            if (picklistId <= 0)
            {
                return (false, 0, null, null, optionIds, "Invalid picklist ID.");
            }

            try
            {
                var url = $"{_baseUrl}/rest/v1/picklists/{picklistId}/options";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return (false, 0, null, null, optionIds, response.StatusCode.ToString());
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var dataArray = TryGetPicklistDataArray(doc.RootElement);
                if (!dataArray.HasValue || dataArray.Value.ValueKind != JsonValueKind.Array)
                {
                    return (false, 0, null, null, optionIds, "No data array returned.");
                }

                foreach (var option in dataArray.Value.EnumerateArray())
                {
                    if (!option.TryGetProperty("id", out var idProp) || !idProp.TryGetInt32(out var optionId))
                    {
                        continue;
                    }

                    optionIds.Add(optionId);
                    if (!firstOptionId.HasValue)
                    {
                        firstOptionId = optionId;
                        if (option.TryGetProperty("name", out var nameProp))
                        {
                            firstOptionName = nameProp.GetString();
                        }
                    }
                }

                if (optionIds.Count == 0)
                {
                    return (false, 0, null, null, optionIds, "No picklist option entries returned.");
                }

                return (true, optionIds.Count, firstOptionId, firstOptionName, optionIds, null);
            }
            catch (Exception ex)
            {
                return (false, 0, null, null, optionIds, ex.Message);
            }
        }

        private async Task<Dictionary<string, int>> GetLookupFieldPicklistMappingsAsync(int itemTypeId, CancellationToken cancellationToken)
        {
            var mappings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var endpoints = new[]
            {
                $"{_baseUrl}/rest/v1/itemtypes/{itemTypeId}",
                $"{_baseUrl}/rest/v1/itemtypes/{itemTypeId}/fields"
            };

            foreach (var endpoint in endpoints)
            {
                try
                {
                    var response = await _httpClient.GetAsync(endpoint, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    ExtractLookupFieldPicklistMappings(doc.RootElement, mappings, itemTypeId);
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Could not load lookup field mapping from '{endpoint}': {ex.Message}");
                }
            }

            return mappings;
        }

        private static void ExtractLookupFieldPicklistMappings(JsonElement root, Dictionary<string, int> mappings, int itemTypeId)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("data", out var dataProp))
                {
                    ExtractLookupFieldPicklistMappings(dataProp, mappings, itemTypeId);
                }

                if (root.TryGetProperty("fields", out var fieldsProp))
                {
                    ExtractLookupFieldPicklistMappings(fieldsProp, mappings, itemTypeId);
                }

                var fieldName = GetStringProperty(root, "fieldName")
                    ?? GetStringProperty(root, "name")
                    ?? GetStringProperty(root, "apiName")
                    ?? GetStringProperty(root, "key");

                var picklistId = GetIntProperty(root, "pickList")
                    ?? GetIntProperty(root, "picklist")
                    ?? GetIntProperty(root, "pickListId")
                    ?? GetIntProperty(root, "picklistId")
                    ?? GetIntProperty(root, "lookupType")
                    ?? GetIntProperty(root, "lookupTypeId");

                if (!string.IsNullOrWhiteSpace(fieldName) &&
                    fieldName.StartsWith("lookup", StringComparison.OrdinalIgnoreCase) &&
                    picklistId.HasValue &&
                    picklistId.Value > 0)
                {
                    mappings[fieldName] = picklistId.Value;

                    if (fieldName.EndsWith($"${itemTypeId}", StringComparison.OrdinalIgnoreCase))
                    {
                        var bare = fieldName.Substring(0, fieldName.Length - (itemTypeId.ToString().Length + 1));
                        if (!string.IsNullOrWhiteSpace(bare))
                        {
                            mappings[bare] = picklistId.Value;
                        }
                    }
                }

                foreach (var prop in root.EnumerateObject())
                {
                    ExtractLookupFieldPicklistMappings(prop.Value, mappings, itemTypeId);
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    ExtractLookupFieldPicklistMappings(element, mappings, itemTypeId);
                }
            }
        }

        private static string? GetStringProperty(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var prop))
            {
                return null;
            }

            return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
        }

        private static int? GetIntProperty(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var prop))
            {
                return null;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value))
            {
                return value;
            }

            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static bool? GetBoolProperty(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var prop))
            {
                return null;
            }

            if (prop.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (prop.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                if (bool.TryParse(value, out var parsedBool))
                {
                    return parsedBool;
                }

                if (int.TryParse(value, out var parsedInt))
                {
                    return parsedInt != 0;
                }
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var number))
            {
                return number != 0;
            }

            return null;
        }

        private static void CollectPicklistIds(JsonElement root, HashSet<int> ids)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    var name = property.Name;
                    if (name.Equals("pickList", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("picklist", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("pickListId", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("picklistId", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("lookupType", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("lookupTypeId", StringComparison.OrdinalIgnoreCase))
                    {
                        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var id) && id > 0)
                        {
                            ids.Add(id);
                        }
                        else if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), out var parsedId) && parsedId > 0)
                        {
                            ids.Add(parsedId);
                        }
                    }

                    CollectPicklistIds(property.Value, ids);
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    CollectPicklistIds(element, ids);
                }
            }
        }

        private static string TryPrettyJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return "<empty>";
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return json;
            }
        }

        public async Task<JamaLookupFieldProbeReport> ProbeRequirementLookupFieldsAsync(
            int projectId,
            int maxLookupFields = 20,
            CancellationToken cancellationToken = default)
        {
            var report = new JamaLookupFieldProbeReport
            {
                ProjectId = projectId,
                GeneratedAtUtc = DateTime.UtcNow
            };

            if (projectId <= 0)
            {
                report.Success = false;
                report.Message = "Invalid Jama project ID.";
                return report;
            }

            if (maxLookupFields < 1)
            {
                maxLookupFields = 1;
            }

            try
            {
                await EnsureAccessTokenAsync();

                var (typeSuccess, requirementItemTypeId) = await GetRequirementItemTypeAsync(projectId, cancellationToken);
                if (!typeSuccess || !requirementItemTypeId.HasValue)
                {
                    report.Success = false;
                    report.Message = "Could not determine requirement item type for lookup probe.";
                    return report;
                }

                report.RequirementItemTypeId = requirementItemTypeId.Value;

                var defaultFields = await GetRequirementFieldDefaultsAsync(projectId, requirementItemTypeId.Value, cancellationToken);
                var fieldPicklistMappings = await GetLookupFieldPicklistMappingsAsync(requirementItemTypeId.Value, cancellationToken);
                var candidateFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var field in defaultFields.Keys)
                {
                    if (field.StartsWith("lookup", StringComparison.OrdinalIgnoreCase))
                    {
                        candidateFields.Add(field);
                    }
                }

                foreach (var field in fieldPicklistMappings.Keys)
                {
                    candidateFields.Add(field);
                }

                if (candidateFields.Count == 0)
                {
                    for (var i = 1; i <= maxLookupFields; i++)
                    {
                        candidateFields.Add($"lookup{i}");
                    }
                }

                foreach (var fieldName in candidateFields.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var result = new JamaLookupFieldProbeResult
                    {
                        FieldName = fieldName
                    };

                    if (defaultFields.TryGetValue(fieldName, out var defaultValue))
                    {
                        result.CurrentDefaultLookupId = TryExtractLookupId(defaultValue);
                    }

                    HashSet<int> optionIds;

                    if (fieldPicklistMappings.TryGetValue(fieldName, out var picklistId) && picklistId > 0)
                    {
                        var picklistByIdResult = await GetPicklistOptionsByPicklistIdAsync(picklistId, cancellationToken);
                        result.EndpointAvailable = picklistByIdResult.EndpointAvailable;
                        result.OptionCount = picklistByIdResult.OptionCount;
                        result.FirstOptionId = picklistByIdResult.FirstOptionId;
                        result.FirstOptionName = picklistByIdResult.FirstOptionName;
                        result.Error = picklistByIdResult.Error;
                        result.ResolvedFieldName = $"{fieldName} -> picklist:{picklistId}";
                        optionIds = picklistByIdResult.OptionIds;
                    }
                    else
                    {
                        var picklistResult = await GetPicklistOptionsForFieldAsync(projectId, requirementItemTypeId.Value, fieldName, cancellationToken);
                        result.EndpointAvailable = picklistResult.EndpointAvailable;
                        result.OptionCount = picklistResult.OptionCount;
                        result.FirstOptionId = picklistResult.FirstOptionId;
                        result.FirstOptionName = picklistResult.FirstOptionName;
                        result.Error = picklistResult.Error;
                        result.ResolvedFieldName = picklistResult.ResolvedFieldName;
                        optionIds = picklistResult.OptionIds;
                    }

                    foreach (var optionId in optionIds.Take(5))
                    {
                        result.SampleOptionIds.Add(optionId);
                    }

                    if (result.CurrentDefaultLookupId.HasValue)
                    {
                        result.IsCurrentDefaultValid = optionIds.Contains(result.CurrentDefaultLookupId.Value);
                    }

                    report.Fields.Add(result);
                }

                var fieldsWithOptions = report.Fields.Count(f => f.EndpointAvailable && f.OptionCount > 0);
                var invalidDefaults = report.Fields.Count(f => f.IsCurrentDefaultValid == false);

                report.Success = true;
                report.Message = $"Lookup probe complete. Fields checked: {report.Fields.Count}, fields with options: {fieldsWithOptions}, invalid defaults: {invalidDefaults}.";
                return report;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Lookup probe failed for project {projectId}: {ex.Message}");
                report.Success = false;
                report.Message = $"Lookup probe failed: {ex.Message}";
                return report;
            }
        }

        private static bool ContainsLookupId(object? value, int lookupId)
        {
            if (value == null)
            {
                return false;
            }

            if (value is JsonElement json)
            {
                if (json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out var n))
                {
                    return n == lookupId;
                }

                if (json.ValueKind == JsonValueKind.String && int.TryParse(json.GetString(), out var sParsed))
                {
                    return sParsed == lookupId;
                }

                if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var idVal))
                {
                    return idVal == lookupId;
                }
            }

            if (value is int i)
            {
                return i == lookupId;
            }

            if (value is long l)
            {
                return l == lookupId;
            }

            if (value is double d)
            {
                return Math.Abs(d - lookupId) < 0.0001;
            }

            if (value is string s && int.TryParse(s, out var parsed))
            {
                return parsed == lookupId;
            }

            if (value is System.Collections.IDictionary dict)
            {
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    if (entry.Key is string key && (key.Equals("id", StringComparison.OrdinalIgnoreCase) || key.Equals("lookup", StringComparison.OrdinalIgnoreCase) || key.Equals("value", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (ContainsLookupId(entry.Value, lookupId))
                        {
                            return true;
                        }
                    }

                    if (ContainsLookupId(entry.Value, lookupId))
                    {
                        return true;
                    }
                }
            }

            if (value is System.Collections.IEnumerable enumerable && value is not string)
            {
                foreach (var item in enumerable)
                {
                    if (ContainsLookupId(item, lookupId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int? TryExtractLookupId(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is JsonElement json)
            {
                if (json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out var n))
                {
                    return n;
                }

                if (json.ValueKind == JsonValueKind.String && int.TryParse(json.GetString(), out var sParsed))
                {
                    return sParsed;
                }

                if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var idVal))
                {
                    return idVal;
                }
            }

            if (value is int i)
            {
                return i;
            }

            if (value is long l)
            {
                return (int)l;
            }

            if (value is double d)
            {
                return (int)d;
            }

            if (value is string s && int.TryParse(s, out var parsed))
            {
                return parsed;
            }

            if (value is System.Collections.IDictionary dict)
            {
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    if (entry.Key is string key && (key.Equals("id", StringComparison.OrdinalIgnoreCase) || key.Equals("lookup", StringComparison.OrdinalIgnoreCase) || key.Equals("value", StringComparison.OrdinalIgnoreCase)))
                    {
                        var prioritized = TryExtractLookupId(entry.Value);
                        if (prioritized.HasValue)
                        {
                            return prioritized;
                        }
                    }

                    var nestedDict = TryExtractLookupId(entry.Value);
                    if (nestedDict.HasValue)
                    {
                        return nestedDict;
                    }
                }
            }

            if (value is System.Collections.IEnumerable enumerable && value is not string)
            {
                foreach (var item in enumerable)
                {
                    var nested = TryExtractLookupId(item);
                    if (nested.HasValue)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private async Task SanitizeLookupDefaultsForCreateAsync(
            Dictionary<string, object?> fields,
            int itemTypeId,
            CancellationToken cancellationToken)
        {
            var fieldPicklistMappings = await GetLookupFieldPicklistMappingsAsync(itemTypeId, cancellationToken);
            if (fieldPicklistMappings.Count == 0)
            {
                return;
            }

            var picklistOptionsCache = new Dictionary<int, (bool EndpointAvailable, int OptionCount, int? FirstOptionId, string? FirstOptionName, HashSet<int> OptionIds, string? Error)>();

            foreach (var fieldName in fields.Keys.Where(k => k.StartsWith("lookup", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                if (!fieldPicklistMappings.TryGetValue(fieldName, out var picklistId) || picklistId <= 0)
                {
                    continue;
                }

                var currentLookupId = TryExtractLookupId(fields[fieldName]);
                if (!currentLookupId.HasValue)
                {
                    continue;
                }

                if (!picklistOptionsCache.TryGetValue(picklistId, out var options))
                {
                    options = await GetPicklistOptionsByPicklistIdAsync(picklistId, cancellationToken);
                    picklistOptionsCache[picklistId] = options;
                }

                if (!options.EndpointAvailable || options.OptionIds.Count == 0)
                {
                    continue;
                }

                if (options.OptionIds.Contains(currentLookupId.Value))
                {
                    continue;
                }

                if (options.FirstOptionId.HasValue)
                {
                    fields[fieldName] = options.FirstOptionId.Value;
                    TestCaseEditorApp.Services.Logging.Log.Warn(
                        $"[JamaConnect] Replaced invalid default lookup value for '{fieldName}' (picklist {picklistId}): {currentLookupId.Value} -> {options.FirstOptionId.Value}");
                }
            }

            foreach (var mapping in fieldPicklistMappings)
            {
                var fieldName = mapping.Key;
                var picklistId = mapping.Value;
                if (picklistId <= 0)
                {
                    continue;
                }

                if (fields.TryGetValue(fieldName, out var existing) && TryExtractLookupId(existing).HasValue)
                {
                    continue;
                }

                if (!picklistOptionsCache.TryGetValue(picklistId, out var options))
                {
                    options = await GetPicklistOptionsByPicklistIdAsync(picklistId, cancellationToken);
                    picklistOptionsCache[picklistId] = options;
                }

                if (!options.EndpointAvailable || !options.FirstOptionId.HasValue)
                {
                    continue;
                }

                fields[fieldName] = options.FirstOptionId.Value;
                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[JamaConnect] Applied fallback lookup value for missing field '{fieldName}' from picklist {picklistId}: {options.FirstOptionId.Value}");
            }
        }

        private static bool IsNonRetryableRequirementCreateFailure(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            // Upgrade/maintenance banner is transient — must NOT be treated as non-retryable.
            if (message.StartsWith("JAMA_UPGRADING", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return message.Contains("No requirement item type found", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Could not determine requirement item type", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsJamaUpgradeBanner(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;
            return content.Contains("being upgraded", StringComparison.OrdinalIgnoreCase)
                || content.Contains("Jama Cloud is being upgraded", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<Dictionary<string, object?>> GetRequirementFieldDefaultsAsync(int projectId, int itemTypeId, CancellationToken cancellationToken)
        {
            var cacheKey = $"{projectId}:{itemTypeId}";
            lock (_requirementFieldDefaultsCache)
            {
                if (_requirementFieldDefaultsCache.TryGetValue(cacheKey, out var cached))
                {
                    return new Dictionary<string, object?>(cached, StringComparer.OrdinalIgnoreCase);
                }
            }

            var defaults = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Scan multiple existing items to find stable non-null values for required custom fields.
                var url = $"{_baseUrl}/rest/v1/items?project={projectId}&itemType={itemTypeId}&maxResults=25";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return defaults;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var result = JsonDocument.Parse(json);

                if (!result.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    return defaults;
                }

                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object ||
                        !item.TryGetProperty("fields", out var fieldsElement) ||
                        fieldsElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var property in fieldsElement.EnumerateObject())
                    {
                        // Jama custom required fields can be named lookup* or field* depending on project schema.
                        if (!property.Name.StartsWith("lookup", StringComparison.OrdinalIgnoreCase) &&
                            !property.Name.StartsWith("field", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (defaults.ContainsKey(property.Name))
                        {
                            continue;
                        }

                        object? converted;
                        if (property.Name.StartsWith("lookup", StringComparison.OrdinalIgnoreCase))
                        {
                            converted = NormalizeLookupFieldValue(property.Value);
                        }
                        else
                        {
                            converted = ConvertJsonElementToSerializableObject(property.Value);
                        }

                        if (IsMeaningfulFieldValue(converted))
                        {
                            defaults[property.Name] = converted;
                        }
                    }
                }

                if (defaults.Count > 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info(
                        $"[JamaConnect] Loaded default requirement fields for project {projectId}, itemType {itemTypeId}: {string.Join(", ", defaults.Keys)}");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn(
                        $"[JamaConnect] No non-null custom default fields discovered for project {projectId}, itemType {itemTypeId}. Required lookup fields may need explicit mapping.");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Could not load requirement field defaults: {ex.Message}");
            }

            if (defaults.Count > 0)
            {
                lock (_requirementFieldDefaultsCache)
                {
                    _requirementFieldDefaultsCache[cacheKey] = defaults;
                }
            }

            return new Dictionary<string, object?>(defaults, StringComparer.OrdinalIgnoreCase);
        }

        private static object? ConvertJsonElementToSerializableObject(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var i) ? i : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Object => element.EnumerateObject()
                    .ToDictionary(prop => prop.Name, prop => ConvertJsonElementToSerializableObject(prop.Value)),
                JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToSerializableObject).ToList(),
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        private static object? NormalizeLookupFieldValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.Undefined => null,
                JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString()) ? null : element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var i) ? i : element.GetDouble(),
                JsonValueKind.Object => NormalizeLookupObject(element),
                JsonValueKind.Array => NormalizeLookupArray(element),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        private static object? NormalizeLookupObject(JsonElement element)
        {
            if (element.TryGetProperty("id", out var idProp))
            {
                var normalizedId = NormalizeLookupFieldValue(idProp);
                if (normalizedId != null)
                {
                    return normalizedId;
                }
            }

            if (element.TryGetProperty("value", out var valueProp))
            {
                var normalizedValue = NormalizeLookupFieldValue(valueProp);
                if (normalizedValue != null)
                {
                    return normalizedValue;
                }
            }

            return null;
        }

        private static object? NormalizeLookupArray(JsonElement element)
        {
            var normalizedValues = element
                .EnumerateArray()
                .Select(NormalizeLookupFieldValue)
                .Where(IsMeaningfulFieldValue)
                .ToList();

            if (normalizedValues.Count == 0)
            {
                return null;
            }

            return normalizedValues;
        }

        private static bool IsMeaningfulFieldValue(object? value)
        {
            if (value == null)
            {
                return false;
            }

            if (value is string s)
            {
                return !string.IsNullOrWhiteSpace(s);
            }

            if (value is System.Collections.IEnumerable enumerable && value is not string)
            {
                foreach (var _ in enumerable)
                {
                    return true;
                }

                return false;
            }

            return true;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _sessionClient?.Dispose();
        }
    }

    // DTOs for Jama Connect API responses
    public class JamaProjectsResponse
    {
        public List<JamaProject> Data { get; set; } = new();
    }

    public class JamaProject
    {
        public int Id { get; set; }
        public string ProjectKey { get; set; } = "";
        public bool IsFolder { get; set; }
        public string CreatedDate { get; set; } = "";
        public string ModifiedDate { get; set; } = "";
        public int CreatedBy { get; set; }
        public int ModifiedBy { get; set; }
        public JamaProjectFields? Fields { get; set; }
        public string Type { get; set; } = "";
        
        // Convenience properties for UI binding
        public string Name => Fields?.Name ?? "";
        public string Key => ProjectKey;
        public string Description => Fields?.Description ?? "";
    }
    
    public class JamaProjectFields
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ProjectKey { get; set; } = "";
        public string Text1 { get; set; } = "";
    }
    
    /// <summary>
    /// Custom JSON converter that handles status fields that can be either string or number
    /// Different Jama instances/configurations may return status as string ("Active") or number (1)
    /// </summary>
    public class FlexibleStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.Number:
                    return reader.GetInt32().ToString();
                case JsonTokenType.Null:
                    return null;
                default:
                    throw new JsonException($"Unable to convert token type '{reader.TokenType}' to string.");
            }
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }

    public class JamaItemsResponse
    {
        public List<JamaItem> Data { get; set; } = new();
        public JamaMeta? Meta { get; set; }
    }

    public class JamaItem
    {
        public int Id { get; set; }
        public string? DocumentKey { get; set; }
        public string? GlobalId { get; set; }
        public int? Project { get; set; }
        public int? ItemType { get; set; }
        public JamaItemFields? Fields { get; set; }
        
        // Direct properties that exist in actual Jama response
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Status { get; set; }
        public string? Item { get; set; }  // The Item ID like "DECAGON-CMP-7"
        
        // User metadata properties (for individual item calls)
        public int CreatedBy { get; set; }
        public int ModifiedBy { get; set; }
        public string CreatedDate { get; set; } = "";
        public string ModifiedDate { get; set; } = "";
        public int Version { get; set; }
        
        // Resolved user names (populated after user lookup)
        public string CreatedByName { get; set; } = "";
        public string ModifiedByName { get; set; } = "";
    }

    public class JamaItemFields
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Status { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Priority { get; set; }
        
        // Enhanced field mapping for common Jama fields
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Version { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? CreatedBy { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? ModifiedBy { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? CreatedDate { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? ModifiedDate { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? KeyCharacteristics { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Safety { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Security { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Derived { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? ExportControlled { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? FDAL { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Set { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Heading { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? ChangeDriver { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? LockedBy { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Comments { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Attachments { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? DownstreamLinks { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? UpstreamLinks { get; set; }
        
        // Add more fields as needed based on your Jama configuration
    }

    /// <summary>
    /// User information from Jama
    /// </summary>
    public class JamaUser
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public bool Active { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 🚨 CRITICAL OAUTH CODE - DO NOT MODIFY WITHOUT EXPLICIT CONFIRMATION 🚨
    // ═══════════════════════════════════════════════════════════════════════════════
    // This OAuth implementation has been fixed multiple times due to JSON serialization
    // issues. The JsonPropertyName attributes are CRITICAL and must match the exact
    // field names returned by Jama's OAuth API (snake_case).
    // 
    // CHANGE HISTORY:
    // - 2026-01-23: Fixed missing JsonPropertyName attributes causing auth failures
    // - Previous issues: OAuth token deserialization returning null AccessToken
    // 
    // ⚠️  BEFORE MAKING ANY CHANGES:
    // 1. Test OAuth authentication thoroughly 
    // 2. Verify token response JSON structure matches these property names
    // 3. Confirm with maintainer before modifying
    // ═══════════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// OAuth token response from Jama
    /// </summary>
    public class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]  // 🚨 CRITICAL: Must match Jama API response exactly
        public string? AccessToken { get; set; }
        
        [JsonPropertyName("token_type")]   // 🚨 CRITICAL: Must match Jama API response exactly
        public string? TokenType { get; set; }
        
        [JsonPropertyName("expires_in")]   // 🚨 CRITICAL: Must match Jama API response exactly
        public int ExpiresIn { get; set; }
        
        [JsonPropertyName("refresh_token")] // 🚨 CRITICAL: Must match Jama API response exactly
        public string? RefreshToken { get; set; }
        
        /// <summary>
        /// Validate that the OAuth response has the required token
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(AccessToken);
    }

    /// <summary>
    /// User response wrapper from Jama API
    /// </summary>
    public class JamaUserResponse
    {
        public JamaUser? Data { get; set; }
        public JamaMeta? Meta { get; set; }
        public List<object>? Links { get; set; }
    }

    /// <summary>
    /// Item response wrapper from Jama API
    /// </summary>
    public class JamaItemResponse
    {
        public JamaItem? Data { get; set; }
        public JamaMeta? Meta { get; set; }
        public List<object>? Links { get; set; }
    }

    /// <summary>
    /// Activity information from Jama for tracking changes
    /// </summary>
    public class JamaActivity
    {
        public int Id { get; set; }
        public string Action { get; set; } = "";
        public DateTime Date { get; set; }
        public string UserName { get; set; } = "";
        public string ItemName { get; set; } = "";
        public int ItemId { get; set; }
    }

    /// <summary>
    /// Response containing list of activities
    /// </summary>
    public class JamaActivityListResponse
    {
        public List<JamaActivity> Data { get; set; } = new();
        public JamaMeta? Meta { get; set; }
    }

    /// <summary>
    /// Metadata for Jama API responses
    /// </summary>
    public class JamaMeta
    {
        public JamaPageInfo? PageInfo { get; set; }
    }

    /// <summary>
    /// Pagination information from Jama API responses
    /// </summary>
    public class JamaPageInfo
    {
        public int StartIndex { get; set; }
        public int ResultCount { get; set; }
        public int TotalResults { get; set; }
    }

    /// <summary>
    /// Enhanced enum decoding implementation following Architectural Guide AI patterns
    /// </summary>
    public partial class JamaConnectService
    {
        /// <summary>
        /// Convert Jama items to requirements with enhanced enum decoding
        /// Follows Architectural Guide AI patterns for service implementation
        /// </summary>
        public async Task<List<Requirement>> ConvertToRequirementsWithEnumDecodingAsync(List<JamaItem> items, int projectId, CancellationToken cancellationToken = default)
        {
            if (items == null || items.Count == 0) return new List<Requirement>();

            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Converting {items.Count} items with enhanced enum decoding");
                
                // Start with standard conversion and enhance with enum decoding
                var requirements = await ConvertToRequirementsAsync(items);
                
                // Build enum lookup table for this project
                var enumLookups = await BuildEnumLookupTableAsync(projectId, cancellationToken);
                
                // Enhance each requirement with decoded enum values
                foreach (var requirement in requirements)
                {
                    await EnhanceRequirementWithDecodedFieldsAsync(requirement, items, enumLookups);
                }

                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Successfully enhanced {requirements.Count} requirements");
                return requirements;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Enhanced conversion failed: {ex.Message}");
                // Graceful fallback to standard conversion
                return await ConvertToRequirementsAsync(items);
            }
        }

        /// <summary>
        /// Build enum lookup table for enhanced field decoding
        /// </summary>
        private async Task<Dictionary<string, Dictionary<int, string>>> BuildEnumLookupTableAsync(int projectId, CancellationToken cancellationToken)
        {
            var lookupTable = new Dictionary<string, Dictionary<int, string>>();
            
            try
            {
                // Define key fields that need enum decoding
                var enumFields = new[] 
                {
                    "verification_methods$193", "validation_methods$193", "validation_evidence$193",
                    "validation_conclusion$193", "project_defined$193", "doors_relationship$193",
                    "robust_requirement$193"
                };

                foreach (var fieldName in enumFields)
                {
                    try
                    {
                        var url = $"{_baseUrl}/rest/v1/projects/{projectId}/picklistoptions?filteredField={fieldName}";
                        var response = await _httpClient.GetAsync(url, cancellationToken);
                        
                        if (response?.IsSuccessStatusCode == true)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            var doc = JsonDocument.Parse(json);
                            
                            if (doc.RootElement.TryGetProperty("data", out var dataArray))
                            {
                                var enumValues = new Dictionary<int, string>();
                                
                                foreach (var item in dataArray.EnumerateArray())
                                {
                                    if (item.TryGetProperty("id", out var idProp) &&
                                        item.TryGetProperty("name", out var nameProp) &&
                                        idProp.TryGetInt32(out var id))
                                    {
                                        enumValues[id] = nameProp.GetString() ?? $"[ID {id}]";
                                    }
                                }
                                
                                if (enumValues.Count > 0)
                                {
                                    lookupTable[fieldName] = enumValues;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed to load enum values for {fieldName}: {ex.Message}");
                    }
                }
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Built lookup table for {lookupTable.Count} enum fields");
                return lookupTable;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Error building enum lookup: {ex.Message}");
                return new Dictionary<string, Dictionary<int, string>>();
            }
        }

        /// <summary>
        /// Enhance a requirement with decoded enum field values
        /// </summary>
        private Task EnhanceRequirementWithDecodedFieldsAsync(Requirement requirement, List<JamaItem> originalItems, Dictionary<string, Dictionary<int, string>> enumLookups)
        {
            try
            {
                // Find the original Jama item for this requirement
                var jamaItem = originalItems.FirstOrDefault(item =>
                    item.Id.ToString() == requirement.ApiId ||
                    item.GlobalId == requirement.GlobalId);

                if (jamaItem?.Fields != null)
                {
                    // Get the fields as a dynamic object to access custom field IDs
                    var fieldsJson = System.Text.Json.JsonSerializer.Serialize(jamaItem.Fields);
                    using var jsonDoc = JsonDocument.Parse(fieldsJson);
                    var fields = jsonDoc.RootElement;

                    // Enhance with decoded enum values for known fields
                    requirement.VerificationMethodText = GetFieldValue(fields, "verification_methods$193", enumLookups);
                    requirement.ValidationMethodText = GetFieldValue(fields, "validation_methods$193", enumLookups);
                    requirement.ValidationEvidence = GetFieldValue(fields, "validation_evidence$193", enumLookups);
                    requirement.ValidationConclusion = GetFieldValue(fields, "validation_conclusion$193", enumLookups);
                    requirement.ProjectDefined = GetFieldValue(fields, "project_defined$193", enumLookups);
                    requirement.DoorsRelationship = GetFieldValue(fields, "doors_relationship$193", enumLookups);
                    requirement.RobustRequirement = GetFieldValue(fields, "robust_requirement$193", enumLookups);

                    // Populate other enhanced fields from known properties
                    requirement.KeyCharacteristics = jamaItem.Fields.KeyCharacteristics ?? string.Empty;
                    requirement.Fdal = jamaItem.Fields.FDAL ?? string.Empty;

                    // Try to get robust rationale from dynamic fields
                    if (fields.TryGetProperty("robust_rationale$193", out var rationale))
                    {
                        requirement.RobustRationale = rationale.GetString() ?? string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Error enhancing requirement {requirement.GlobalId}: {ex.Message}");
            }

            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Get field value with enum decoding if available
        /// </summary>
        private string GetFieldValue(JsonElement fields, string fieldName, Dictionary<string, Dictionary<int, string>> enumLookups)
        {
            try
            {
                if (fields.TryGetProperty(fieldName, out var fieldValue))
                {
                    return DecodeEnumField(fieldValue, fieldName, enumLookups);
                }
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Decode enum field value using lookup table
        /// </summary>
        private string DecodeEnumField(JsonElement fieldValue, string fieldName, Dictionary<string, Dictionary<int, string>> enumLookups)
        {
            try
            {
                if (enumLookups.TryGetValue(fieldName, out var enumDict))
                {
                    // Handle single integer value
                    if (fieldValue.ValueKind == JsonValueKind.Number && fieldValue.TryGetInt32(out var singleId))
                    {
                        return enumDict.TryGetValue(singleId, out var value) ? value : $"[Unknown: {singleId}]";
                    }
                    
                    // Handle array of integers
                    if (fieldValue.ValueKind == JsonValueKind.Array)
                    {
                        var decodedValues = new List<string>();
                        foreach (var arrayElement in fieldValue.EnumerateArray())
                        {
                            if (arrayElement.TryGetInt32(out var id))
                            {
                                decodedValues.Add(enumDict.TryGetValue(id, out var val) ? val : $"[Unknown: {id}]");
                            }
                        }
                        return string.Join(", ", decodedValues);
                    }
                    
                    // Handle string representation of array like "[1614, 1649]"
                    if (fieldValue.ValueKind == JsonValueKind.String)
                    {
                        var valueStr = fieldValue.GetString();
                        if (!string.IsNullOrEmpty(valueStr) && valueStr.Contains("["))
                        {
                            var ids = ExtractIdsFromArrayString(valueStr);
                            var decodedValues = ids.Select(id => enumDict.TryGetValue(id, out var val) ? val : $"[Unknown: {id}]").ToList();
                            return string.Join(", ", decodedValues);
                        }
                        
                        // Try to parse as single integer
                        if (int.TryParse(valueStr, out var stringId))
                        {
                            return enumDict.TryGetValue(stringId, out var value) ? value : $"[Unknown: {stringId}]";
                        }
                        
                        return valueStr ?? string.Empty;
                    }
                }
                
                // Return the raw value if no enum lookup available
                return fieldValue.ValueKind == JsonValueKind.String ? 
                       fieldValue.GetString() ?? string.Empty : 
                       fieldValue.ToString();
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Error decoding {fieldName}: {ex.Message}");
                return fieldValue.ValueKind == JsonValueKind.String ? 
                       fieldValue.GetString() ?? string.Empty : 
                       fieldValue.ToString();
            }
        }

        /// <summary>
        /// Extract integer IDs from array string format
        /// </summary>
        private List<int> ExtractIdsFromArrayString(string arrayString)
        {
            var ids = new List<int>();
            try
            {
                // Remove brackets and split by comma
                var cleanString = arrayString.Trim('[', ']', ' ');
                var parts = cleanString.Split(',', StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var part in parts)
                {
                    if (int.TryParse(part.Trim(), out var id))
                    {
                        ids.Add(id);
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Error parsing array string '{arrayString}': {ex.Message}");
            }
            return ids;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Attachment Models
    // ═══════════════════════════════════════════════════════════════════════════════

    public class JamaAttachmentsResponse
    {
        public List<JamaAttachment> Data { get; set; } = new();
        public JamaResponseMeta? Meta { get; set; }
    }

    public class JamaAttachment
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string FileName { get; set; } = "";
        public string MimeType { get; set; } = "";
        public long FileSize { get; set; }
        public int Item { get; set; }  // ID of the item this is attached to
        public int CreatedBy { get; set; }
        public string CreatedDate { get; set; } = "";

        // Index validation metadata for scrape safety checks.
        public AttachmentIndexValidationState IndexValidationState { get; set; } = AttachmentIndexValidationState.Unknown;
        public string IndexValidationMessage { get; set; } = string.Empty;
        public bool ScrapeBlocked { get; set; }
        
        // Computed properties for UI
        public string DisplayName
        {
            get
            {
                var baseName = !string.IsNullOrWhiteSpace(Name)
                    ? Name
                    : !string.IsNullOrWhiteSpace(FileName)
                        ? FileName
                        : $"Attachment {Id}";

                return ScrapeBlocked ? $"{baseName} [Re-index required]" : baseName;
            }
        }
        public string FileSizeDisplay => FormatFileSize(FileSize);
        public bool IsPdf => MimeType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) ?? false;
        public bool IsWord => MimeType?.Contains("word", StringComparison.OrdinalIgnoreCase) ?? 
                              MimeType?.Contains("msword", StringComparison.OrdinalIgnoreCase) ?? 
                              FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ||
                              FileName.EndsWith(".doc", StringComparison.OrdinalIgnoreCase);
        public bool IsExcel => MimeType?.Contains("excel", StringComparison.OrdinalIgnoreCase) ?? 
                               MimeType?.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase) ??
                               FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                               FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase);
        public bool IsText => (MimeType?.Contains("text", StringComparison.OrdinalIgnoreCase) == true) ||
                              (MimeType?.Contains("plain", StringComparison.OrdinalIgnoreCase) == true) ||
                              FileName?.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) == true ||
                              FileName?.EndsWith(".md", StringComparison.OrdinalIgnoreCase) == true ||
                              FileName?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true;
        public bool IsSupportedDocument => IsPdf || IsWord || IsExcel || IsText;
        
        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public class JamaResponseMeta
    {
        public JamaPageInfo? PageInfo { get; set; }
    }

    public class JamaLookupFieldProbeReport
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ProjectId { get; set; }
        public int? RequirementItemTypeId { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
        public List<JamaLookupFieldProbeResult> Fields { get; set; } = new();
    }

    public class JamaLookupFieldProbeResult
    {
        public string FieldName { get; set; } = string.Empty;
        public string? ResolvedFieldName { get; set; }
        public bool EndpointAvailable { get; set; }
        public int OptionCount { get; set; }
        public int? FirstOptionId { get; set; }
        public string? FirstOptionName { get; set; }
        public int? CurrentDefaultLookupId { get; set; }
        public bool? IsCurrentDefaultValid { get; set; }
        public string? Error { get; set; }
        public List<int> SampleOptionIds { get; set; } = new();
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Abstract Items Models (for cookbook-compliant attachment discovery)
    // ═══════════════════════════════════════════════════════════════════════════════

    public class JamaAbstractItemsResponse
    {
        public List<JamaAbstractItem> Data { get; set; } = new();
        public JamaMeta? Meta { get; set; }
    }

    public class JamaAbstractItem
    {
        public int Id { get; set; }
        public string DocumentKey { get; set; } = "";
        public string GlobalId { get; set; } = "";
        public int ItemType { get; set; }
        public int Project { get; set; }
        public string CreatedDate { get; set; } = "";
        public string ModifiedDate { get; set; } = "";
        public string LastActivityDate { get; set; } = "";
        public int CreatedBy { get; set; }
        public int ModifiedBy { get; set; }
        public JamaItemFields? Fields { get; set; }
        public string Type { get; set; } = "";
    }

    /// <summary>
    /// Request model for creating test cases in Jama Connect
    /// </summary>
    public class JamaTestCaseRequest
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<JamaTestStep> Steps { get; set; } = new();
        public string AssociatedRequirements { get; set; } = "";
        public string Tags { get; set; } = "";
        public string Priority { get; set; } = "Medium";
        public string TestType { get; set; } = "Functional";
    }

    /// <summary>
    /// Test step model for Jama Connect test cases
    /// </summary>
    public class JamaTestStep
    {
        public string Number { get; set; } = "";
        public string Action { get; set; } = "";
        public string ExpectedResult { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    /// <summary>
    /// Response from Jama Connect item creation
    /// </summary>
    public class JamaCreateItemResponse
    {
        public int Id { get; set; }
        public string? DocumentKey { get; set; }
        public string? GlobalId { get; set; }
        public string? Location { get; set; }
    }
}
