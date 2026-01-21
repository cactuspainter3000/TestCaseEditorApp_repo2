using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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
                    
                    var result = JsonSerializer.Deserialize<JamaItemsResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
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
        /// Convert Jama items to our Requirement model
        /// </summary>
        public List<Requirement> ConvertToRequirements(List<JamaItem> jamaItems)
        {
            var requirements = new List<Requirement>();
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Converting {jamaItems.Count} Jama items to requirements");
            
            foreach (var item in jamaItems)
            {
                // Enhanced field mapping with better fallbacks
                var itemId = item.Item ?? item.DocumentKey ?? item.GlobalId ?? $"JAMA-{item.Id}";
                
                // Access Name and Description directly from the item, not from Fields
                var name = item.Name;
                var description = item.Description;
                
                // If name is empty, try to create a meaningful name from available data
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"Item {itemId}";
                }
                
                // Enhanced debugging for field mapping
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {item.Id}: " +
                    $"Item='{item.Item}', DocumentKey='{item.DocumentKey}', GlobalId='{item.GlobalId}', " +
                    $"ItemType={item.ItemType}, Name='{item.Name}', " +
                    $"Description.Length={item.Description?.Length ?? 0}");
                
                var requirement = new Requirement
                {
                    Item = itemId,
                    Name = name,
                    Description = description ?? "",
                    GlobalId = item.GlobalId ?? "",
                    Status = item.Status ?? item.Fields?.Status ?? "",
                    RequirementType = item.ItemType?.ToString() ?? "Unknown",
                    Project = item.Project?.ToString() ?? "",
                };
                
                requirements.Add(requirement);
            }
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Converted {requirements.Count} requirements successfully");
            
            // Log summary of field population
            var withNames = requirements.Count(r => !r.Name.StartsWith("Item "));
            var withDescriptions = requirements.Count(r => !string.IsNullOrWhiteSpace(r.Description));
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Field population: {withNames} with real names, {withDescriptions} with descriptions");
            
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