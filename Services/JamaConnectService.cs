using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
        private DateTime _tokenExpiry = DateTime.MinValue;
        
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
            _httpClient = CreateHttpClient();
        }

        /// <summary>
        /// Initialize with username/password
        /// </summary>
        public JamaConnectService(string baseUrl, string username, string password)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _username = username;
            _password = password;
            _httpClient = CreateHttpClient();
        }

        /// <summary>
        /// Initialize with OAuth client credentials
        /// </summary>
        public JamaConnectService(string baseUrl, string clientId, string clientSecret, bool isOAuth)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _clientId = clientId;
            _clientSecret = clientSecret;
            _httpClient = CreateHttpClient();
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

            // Handle common Jama path variations
            if (!string.IsNullOrEmpty(baseUrl) && !baseUrl.EndsWith("/rest"))
            {
                // Check if we need to add /contour for certain Jama instances
                if (baseUrl.Contains("rockwellcollins.com") && !baseUrl.Contains("/contour"))
                {
                    baseUrl = baseUrl.TrimEnd('/') + "/contour";
                }
            }

            if (!string.IsNullOrEmpty(apiToken))
            {
                return new JamaConnectService(baseUrl, apiToken);
            }
            else if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
            {
                return new JamaConnectService(baseUrl, clientId, clientSecret, true);
            }
            else if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
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
        /// Get OAuth access token using client credentials
        /// </summary>
        private async Task<bool> EnsureAccessTokenAsync()
        {
            // If using OAuth and token is expired or missing, get a new one
            if (!string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_clientSecret))
            {
                if (string.IsNullOrEmpty(_accessToken) || DateTime.UtcNow >= _tokenExpiry)
                {
                    return await GetOAuthTokenAsync();
                }
            }
            return true;
        }

        /// <summary>
        /// Get OAuth access token from Jama
        /// </summary>
        private async Task<bool> GetOAuthTokenAsync()
        {
            try
            {
                var tokenUrl = $"{_baseUrl}/rest/oauth/token";
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaOAuth] Attempting token exchange at: {tokenUrl}");
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaOAuth] Using client ID: {_clientId}");
                
                // According to Jama docs, OAuth uses Basic Auth with client ID/secret
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_clientId}:{_clientSecret}"));
                
                using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaOAuth] Sending token request with Basic Auth...");
                
                var response = await _httpClient.SendAsync(request);
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaOAuth] Response status: {response.StatusCode}");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaOAuth] Token response: {json}");
                    
                    var tokenData = JsonSerializer.Deserialize<OAuthTokenResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    if (tokenData?.AccessToken != null)
                    {
                        _accessToken = tokenData.AccessToken;
                        _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenData.ExpiresIn - 60); // Refresh 1 min early
                        
                        // Update HTTP client authorization header
                        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                        
                        return true;
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    TestCaseEditorApp.Services.Logging.Log.Info($"OAuth token request failed: {response.StatusCode} - {errorContent}");
                }
                
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to get OAuth token: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test connection to Jama Connect API
        /// </summary>
        public async Task<(bool Success, string Message)> TestConnectionAsync()
        {
            try
            {
                if (!IsConfigured)
                {
                    return (false, "Service not configured. Missing base URL or credentials.");
                }

                // For OAuth, try to get a token first
                if (!string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_clientSecret))
                {
                    var tokenSuccess = await EnsureAccessTokenAsync();
                    if (!tokenSuccess)
                    {
                        return (false, "Failed to authenticate with OAuth credentials - check client ID/secret and OAuth endpoint");
                    }
                }

                var testUrl = $"{_baseUrl}/rest/v1/users/current";
                var response = await _httpClient.GetAsync(testUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    return (true, $"Successfully connected to Jama Connect at {_baseUrl}");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    
                    // Check if this is an OAuth scope issue
                    if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError && 
                        errorContent.Contains("IndexOutOfBounds"))
                    {
                        return (false, $"Connection failed: OAuth client has insufficient permissions. Contact your Jama administrator to add 'read' scope to OAuth client. Current error: {response.StatusCode}");
                    }
                    
                    return (false, $"Connection test failed: {response.StatusCode} - {response.ReasonPhrase}. Response: {errorContent}");
                }
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Network error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"Unexpected error: {ex.Message}");
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
                    var result = JsonSerializer.Deserialize<JamaProjectsResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
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
                
                // Get items of type "requirement" from the project
                var url = $"{_baseUrl}/rest/v1/items?project={projectId}";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var result = JsonSerializer.Deserialize<JamaItemsResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    return result?.Data ?? new List<JamaItem>();
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
        /// Convert Jama items to our Requirement model
        /// </summary>
        public List<Requirement> ConvertToRequirements(List<JamaItem> jamaItems)
        {
            var requirements = new List<Requirement>();
            
            foreach (var item in jamaItems)
            {
                var requirement = new Requirement
                {
                    Item = item.DocumentKey ?? $"JAMA-{item.Id}",
                    Name = item.Fields?.Name ?? "Untitled Requirement",
                    Description = item.Fields?.Description ?? "",
                    GlobalId = item.GlobalId ?? "",
                    // Map other fields as needed
                    Status = item.Fields?.Status ?? "",
                    RequirementType = item.ItemType?.ToString() ?? "Requirement",
                    Project = item.Project?.ToString() ?? "",
                    // Add more field mappings as needed
                };
                
                requirements.Add(requirement);
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
        public string Name { get; set; } = "";
        public string Key { get; set; } = "";
        public string Description { get; set; } = "";
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
    }

    public class JamaItemFields
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Status { get; set; }
        public string? Priority { get; set; }
        // Add more fields as needed based on your Jama configuration
    }

    // OAuth token response
    public class OAuthTokenResponse
    {
        public string AccessToken { get; set; } = "";
        public string TokenType { get; set; } = "";
        public int ExpiresIn { get; set; }
    }
}