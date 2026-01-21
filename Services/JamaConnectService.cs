using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for integrating with Jama Connect REST API
    /// Provides direct access to requirements and test case management
    /// </summary>
    public class JamaConnectService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string? _username;
        private readonly string? _password;
        private readonly string? _apiToken;
        private readonly string? _clientId;
        private readonly string? _clientSecret;
        private string? _accessToken;
        private JsonDocument? _lastApiResponseJson;
        
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
        }

        /// <summary>
        /// Initialize with OAuth client credentials
        /// </summary>
        public JamaConnectService(string baseUrl, string clientId, string clientSecret, bool isOAuth)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _clientId = clientId;
            _clientSecret = clientSecret;
            _httpClient = new HttpClient();
            
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

        private HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Set authentication for non-OAuth methods
            if (!string.IsNullOrEmpty(_apiToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
            }
            else if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
            {
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_username}:{_password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
            // OAuth will set Authorization header dynamically after getting access token

            return client;
        }

        /// <summary>
        /// <summary>
        /// Ensure we have a valid access token
        /// </summary>
        private async Task<bool> EnsureAccessTokenAsync()
        {
            if (string.IsNullOrEmpty(_accessToken))
                return await GetOAuthTokenAsync();
                
            return true;
        }

        /// <summary>
        /// Get OAuth access token
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
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth content length: {content.Length}");
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth response content: {content}");
                
                try
                {
                    var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(content);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Deserialized token response - AccessToken is null: {tokenResponse?.AccessToken == null}");
                    
                    _accessToken = tokenResponse?.AccessToken;
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
                    
                    var hasToken = !string.IsNullOrEmpty(_accessToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Token obtained: {hasToken}");
                    if (hasToken)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Token preview: {_accessToken?.Substring(0, Math.Min(20, _accessToken.Length))}...");
                    }
                    return hasToken;
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Failed to deserialize OAuth response: {ex.Message}");
                    return false;
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth failed: {errorContent}");
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
        /// Get list of projects accessible to the current user
        /// </summary>
        public async Task<List<JamaProject>> GetProjectsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Ensure we have a valid access token for OAuth
                await EnsureAccessTokenAsync();
                
                var response = await _httpClient.GetAsync($"{_baseUrl}/rest/v1/projects", cancellationToken);
                
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
        /// Get requirements from a specific project
        /// </summary>
        public async Task<List<JamaItem>> GetRequirementsAsync(int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                // Ensure we have a valid access token for OAuth
                await EnsureAccessTokenAsync();
                
                // Get items from the project - we'll filter for requirements after getting the data
                var url = $"{_baseUrl}/rest/v1/items?project={projectId}";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Fetching items from: {url}");
                
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Raw JSON response: {json.Substring(0, Math.Min(500, json.Length))}...");
                    
                    // RICH CONTENT INVESTIGATION: Write full API response to file for analysis
                    try
                    {
                        var debugFile = Path.Combine(Environment.CurrentDirectory, $"jama_api_response_{projectId}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                        await File.WriteAllTextAsync(debugFile, json, cancellationToken);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Full API response saved to: {debugFile}");
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error(ex, "[JamaConnect] Failed to save API response to file");
                    }
                    
                    var result = JsonSerializer.Deserialize<JamaItemsResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    // ALSO parse the JSON to access dynamic fields
                    _lastApiResponseJson?.Dispose(); // Dispose previous document
                    _lastApiResponseJson = null;
                    try
                    {
                        _lastApiResponseJson = JsonDocument.Parse(json);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Parsed JSON document for rich content extraction");
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed to parse JSON document for rich content extraction: {ex.Message}");
                    }
                    
                    var allItems = result?.Data ?? new List<JamaItem>();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Retrieved {allItems.Count} total items from project {projectId}");
                    
                    // Log analysis of item types to understand what we're getting
                    var itemTypeGroups = allItems.GroupBy(i => i.ItemType).OrderByDescending(g => g.Count());
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item types found:");
                    foreach (var group in itemTypeGroups.Take(5))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"  Type {group.Key}: {group.Count()} items");
                    }
                    
                    // Log details of first few items to understand structure
                    for (int i = 0; i < Math.Min(3, allItems.Count); i++)
                    {
                        var item = allItems[i];
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {i}: Id={item.Id}, DocumentKey={item.DocumentKey}, ItemType={item.ItemType}, Fields={item.Fields != null}");
                        if (item.Fields != null)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"  Fields: Name='{item.Fields.Name}', Description='{item.Fields.Description}', Status='{item.Fields.Status}'");
                        }
                    }
                    
                    // For now, return all items - we'll improve filtering as we learn more about the item types
                    // Common Jama item types: Requirements are often type 53, 55, etc. but this varies by instance
                    return allItems;
                }
                else
                {
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
                await EnsureAccessTokenAsync();
                
                // Get all items from the project to count them
                // This is simple and reliable, though potentially slower for very large projects
                var url = $"{_baseUrl}/rest/v1/items?project={projectId}";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var result = JsonSerializer.Deserialize<JamaItemsResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    var count = result?.Data?.Count ?? 0;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Project {projectId} has {count} items");
                    return count;
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"Failed to get requirement count for project {projectId}: {response.StatusCode}");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Error getting requirement count for project {projectId}");
                return 0;
            }
        }

        /// <summary>
        /// Parse all fields in a Jama item for rich content (HTML tables, paragraphs, etc.)
        /// This method works with the raw JSON API response to access dynamic field names
        /// </summary>
        private async Task<RequirementLooseContent> ParseAllFieldsForRichContentFromJsonAsync(JsonElement itemElement, string description, int itemId)
        {
            var looseContent = new RequirementLooseContent()
            {
                Paragraphs = new List<string>(),
                Tables = new List<LooseTable>()
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

            // Process each field for HTML content
            int totalTablesFound = 0;
            int totalParagraphsFound = 0;
            int htmlFieldsFound = 0;

            foreach (var (fieldName, content) in fieldsToScan)
            {
                if (content.Contains("<") && content.Contains(">"))
                {
                    htmlFieldsFound++;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Processing HTML content in field '{fieldName}' ({content.Length} chars)");

                    var fieldContent = ParseHtmlContent(content, itemId);
                    
                    // Merge tables
                    foreach (var table in fieldContent.Tables)
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

                    // Merge paragraphs
                    foreach (var paragraph in fieldContent.Paragraphs)
                    {
                        if (!string.IsNullOrWhiteSpace(paragraph))
                        {
                            // Optionally prefix with field name for context
                            var contextualParagraph = fieldName != "description" 
                                ? $"[{fieldName}] {paragraph}" 
                                : paragraph;
                            looseContent.Paragraphs.Add(contextualParagraph);
                            totalParagraphsFound++;
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(content))
                {
                    // Plain text content - add as paragraph with context
                    var contextualParagraph = fieldName != "description" 
                        ? $"[{fieldName}] {content.Trim()}" 
                        : content.Trim();
                    looseContent.Paragraphs.Add(contextualParagraph);
                    totalParagraphsFound++;
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
            // Try to use JSON data for better field access
            if (jsonLookup.TryGetValue(itemId, out JsonElement jsonElement))
            {
                return await ParseAllFieldsForRichContentFromJsonAsync(jsonElement, description, itemId);
            }
            else
            {
                // Fallback to basic content parsing from description only
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Item {itemId}: No JSON data available, using basic description parsing");
                return ParseHtmlContent(description, itemId);
            }
        }

        /// <summary>
        /// Parse all fields in a Jama item for rich content (HTML tables, paragraphs, etc.)
        /// </summary>
        private RequirementLooseContent ParseAllFieldsForRichContent(JamaItem item, string description)
        {
            var looseContent = new RequirementLooseContent()
            {
                Paragraphs = new List<string>(),
                Tables = new List<LooseTable>()
            };

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id}: Scanning all fields for rich content");

            var fieldsToScan = new List<(string name, string content)>();

            // Add description field
            if (!string.IsNullOrWhiteSpace(description))
            {
                fieldsToScan.Add(("description", description));
            }

            // Scan all custom fields in the Fields object using reflection
            if (item.Fields != null)
            {
                var fieldsType = item.Fields.GetType();
                var properties = fieldsType.GetProperties();

                foreach (var property in properties)
                {
                    try
                    {
                        var value = property.GetValue(item.Fields);
                        if (value is string stringValue && !string.IsNullOrWhiteSpace(stringValue))
                        {
                            fieldsToScan.Add((property.Name, stringValue));
                        }
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {item.Id}: Failed to read property {property.Name}: {ex.Message}");
                    }
                }
            }

            // Also scan the raw fields collection if it exists as a dictionary
            try
            {
                // Check if the JamaItem has additional fields as a dictionary
                var itemType = item.GetType();
                var fieldsProperty = itemType.GetProperty("Fields");
                if (fieldsProperty?.GetValue(item) is IDictionary<string, object> fieldsDictionary)
                {
                    foreach (var kvp in fieldsDictionary)
                    {
                        if (kvp.Value is string stringValue && !string.IsNullOrWhiteSpace(stringValue))
                        {
                            fieldsToScan.Add((kvp.Key, stringValue));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {item.Id}: Failed to scan fields dictionary: {ex.Message}");
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id}: Found {fieldsToScan.Count} fields to scan for HTML content");

            // Process each field for HTML content
            int totalTablesFound = 0;
            int totalParagraphsFound = 0;
            int htmlFieldsFound = 0;

            foreach (var (fieldName, content) in fieldsToScan)
            {
                if (content.Contains("<") && content.Contains(">"))
                {
                    htmlFieldsFound++;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id}: Processing HTML content in field '{fieldName}' ({content.Length} chars)");

                    var fieldContent = ParseHtmlContent(content, item.Id);
                    
                    // Merge tables
                    foreach (var table in fieldContent.Tables)
                    {
                        // Set a descriptive title indicating which field this came from
                        if (string.IsNullOrWhiteSpace(table.EditableTitle) || table.EditableTitle.StartsWith("Table from Jama Item"))
                        {
                            table.EditableTitle = $"Table from {fieldName} (Item {item.Id})";
                        }
                        looseContent.Tables.Add(table);
                        totalTablesFound++;
                    }

                    // Merge paragraphs
                    foreach (var paragraph in fieldContent.Paragraphs)
                    {
                        if (!string.IsNullOrWhiteSpace(paragraph))
                        {
                            // Optionally prefix with field name for context
                            var contextualParagraph = fieldName != "description" 
                                ? $"[{fieldName}] {paragraph}" 
                                : paragraph;
                            looseContent.Paragraphs.Add(contextualParagraph);
                            totalParagraphsFound++;
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(content))
                {
                    // Plain text content - add as paragraph with context
                    var contextualParagraph = fieldName != "description" 
                        ? $"[{fieldName}] {content.Trim()}" 
                        : content.Trim();
                    looseContent.Paragraphs.Add(contextualParagraph);
                    totalParagraphsFound++;
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id}: Rich content parsing complete - " +
                $"{htmlFieldsFound} HTML fields, {totalTablesFound} tables, {totalParagraphsFound} paragraphs");

            return looseContent;
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
                    // Plain text content - add as single paragraph
                    looseContent.Paragraphs.Add(htmlContent.Trim());
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Plain text content, added as single paragraph");
                    return looseContent;
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Parsing HTML content for rich elements");

                // Load HTML content
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                // Extract tables
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
                    }
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Extracted {looseContent.Tables.Count} tables from HTML");
                }

                // Extract paragraphs (exclude content within tables)
                ExtractParagraphs(doc.DocumentNode, looseContent.Paragraphs, itemId);

                // If no paragraphs were extracted but we have content, fall back to the full HTML as text
                if (looseContent.Paragraphs.Count == 0 && !string.IsNullOrWhiteSpace(doc.DocumentNode.InnerText))
                {
                    var plainText = doc.DocumentNode.InnerText.Trim();
                    if (!string.IsNullOrWhiteSpace(plainText))
                    {
                        looseContent.Paragraphs.Add(plainText);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Fallback to plain text extraction");
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: HTML parsing complete - {looseContent.Tables.Count} tables, {looseContent.Paragraphs.Count} paragraphs");
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
                        table.ColumnHeaders.Add(cell.InnerText.Trim());
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
                            var cellText = cell.InnerText.Trim();
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
                            row.Add(cell.InnerText.Trim());
                        }
                        table.Rows.Add(row);
                    }
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
                        var text = pNode.InnerText.Trim();
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
                            var text = divNode.InnerText.Trim();
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
                            var text = li.InnerText.Trim();
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
            
            // Create a lookup for JSON elements by item ID for rich content parsing
            Dictionary<int, JsonElement> jsonItemLookup = new Dictionary<int, JsonElement>();
            if (_lastApiResponseJson != null)
            {
                try
                {
                    if (_lastApiResponseJson.RootElement.TryGetProperty("data", out JsonElement dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var jsonItem in dataElement.EnumerateArray())
                        {
                            if (jsonItem.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.Number)
                            {
                                var id = idElement.GetInt32();
                                jsonItemLookup[id] = jsonItem;
                            }
                        }
                    }
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Created JSON lookup for {jsonItemLookup.Count} items");
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed to create JSON lookup: {ex.Message}");
                }
            }
            
            foreach (var item in jamaItems)
            {
                // Enhanced field mapping with better fallbacks
                var itemId = item.Item ?? item.DocumentKey ?? item.GlobalId ?? $"JAMA-{item.Id}";
                
                // Access Name and Description directly from the item, not from Fields
                var name = item.Name;
                var description = item.Description;
                
                // ENHANCED DEBUGGING: Log the actual API response structure
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] RAW API ITEM {item.Id}: " +
                    $"Name='{item.Name}', Description='{item.Description}', " +
                    $"Fields.Name='{item.Fields?.Name}', Fields.Description='{item.Fields?.Description}', " +
                    $"ItemType={item.ItemType}, DocumentKey='{item.DocumentKey}', GlobalId='{item.GlobalId}'");
                
                // RICH CONTENT INVESTIGATION: Check if Description contains HTML
                if (!string.IsNullOrEmpty(description))
                {
                    var hasHtml = description.Contains("<") && description.Contains(">");
                    var hasTable = description.Contains("<table", StringComparison.OrdinalIgnoreCase);
                    var hasParagraphs = description.Contains("<p>", StringComparison.OrdinalIgnoreCase);
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id} Description analysis: HTML={hasHtml}, Tables={hasTable}, Paragraphs={hasParagraphs}");
                    
                    if (hasHtml)
                    {
                        // Log first 500 chars of HTML content for analysis
                        var preview = description.Length > 500 ? description.Substring(0, 500) + "..." : description;
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id} HTML Preview: {preview}");
                    }
                }
                
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
                
                // Enhanced debugging for field mapping
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {item.Id}: " +
                    $"Item='{item.Item}', DocumentKey='{item.DocumentKey}', GlobalId='{item.GlobalId}', " +
                    $"ItemType={item.ItemType}, Name='{item.Name}', " +
                    $"Description.Length={item.Description?.Length ?? 0}");
                
                var requirement = new Requirement
                {
                    Item = itemId,
                    Name = name ?? "",  // Use actual name or empty string
                    Description = description ?? "",
                    GlobalId = item.GlobalId ?? "",
                    Status = item.Status ?? item.Fields?.Status ?? "",
                    RequirementType = item.ItemType?.ToString() ?? "Unknown",
                    Project = item.Project?.ToString() ?? "",
                    
                    // Enhanced field mapping from Jama
                    Version = item.Fields?.Version ?? "",
                    CreatedBy = item.Fields?.CreatedBy ?? "",
                    ModifiedBy = item.Fields?.ModifiedBy ?? "",
                    CreatedDateRaw = item.Fields?.CreatedDate ?? "",
                    ModifiedDateRaw = item.Fields?.ModifiedDate ?? "",
                    KeyCharacteristics = item.Fields?.KeyCharacteristics ?? "",
                    Fdal = item.Fields?.FDAL ?? "",
                    DerivedRequirement = item.Fields?.Derived ?? "",
                    ExportControlled = item.Fields?.ExportControlled ?? "",
                    SetName = item.Fields?.Set ?? "",
                    Heading = item.Fields?.Heading ?? "",
                    ChangeDriver = item.Fields?.ChangeDriver ?? "",
                    LastLockedBy = item.Fields?.LockedBy ?? "",
                    
                    // Initialize LooseContent with HTML parsing for rich content from ALL fields
                    LooseContent = await GetRichContentForItemAsync(item.Id, jsonItemLookup, description ?? "")
                };
                
                requirements.Add(requirement);
            }
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Converted {requirements.Count} requirements successfully");
            
            // Log summary of field population
            var withNames = requirements.Count(r => !r.Name.StartsWith("Item "));
            var withDescriptions = requirements.Count(r => !string.IsNullOrWhiteSpace(r.Description));
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Field population: {withNames} with real names, {withDescriptions} with descriptions");
            
            // RICH CONTENT INVESTIGATION: Write converted requirements to file for analysis
            try
            {
                var outputFile = Path.Combine(Environment.CurrentDirectory, $"jama_converted_requirements_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                var json = JsonSerializer.Serialize(requirements, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(outputFile, json);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Converted requirements saved to: {outputFile}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[JamaConnect] Failed to save converted requirements to file");
            }
            
            return requirements;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
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

    // OAuth token response
    public class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";
        
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "";
        
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}