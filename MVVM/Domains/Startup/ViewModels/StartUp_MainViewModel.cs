using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.Startup.Mediators;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;
using System.Text;
using System.Windows;
using System.Linq;

namespace TestCaseEditorApp.MVVM.Domains.Startup.ViewModels
{
    /// <summary>
    /// StartUp MainWorkspace ViewModel - Following AI Guide patterns
    /// </summary>
    public partial class StartUp_MainViewModel : BaseDomainViewModel
    {
        private new readonly IStartupMediator _mediator;
        
        [ObservableProperty]
        private string title = "Systems ATE APP";
        
        [ObservableProperty]
        private string description = "Generate comprehensive test cases using AI-powered analysis. Import requirements, analyze context, and automatically create detailed test scenarios to ensure thorough coverage of your application's functionality.";
        
        // TEMPORARY: Jama Troubleshooting Properties
        [ObservableProperty]
        private string statusMessage = string.Empty;
        
        [ObservableProperty]
        private string statusColor = "#CCCCCC";
        
        [ObservableProperty]
        private Visibility statusVisibility = Visibility.Collapsed;
        
        [ObservableProperty]
        private string resultsText = string.Empty;
        
        [ObservableProperty]
        private Visibility resultsVisibility = Visibility.Collapsed;
        
        [ObservableProperty]
        private string logText = string.Empty;
        
        [ObservableProperty]
        private Visibility logsVisibility = Visibility.Collapsed;
        
        [ObservableProperty]
        private Visibility copyButtonVisibility = Visibility.Visible;
        
        // Individual field display properties
        [ObservableProperty]
        private string sampleCreatedBy = "(Not loaded)";
        
        [ObservableProperty]
        private string sampleProject = "(Not loaded)";
        
        [ObservableProperty]
        private string sampleRequirementDetail = "(Not loaded)";
        
        [ObservableProperty]
        private Visibility fieldDisplayVisibility = Visibility.Collapsed;
        
        private readonly JamaConnectService _jamaService;
        
        public StartUp_MainViewModel(
            IStartupMediator mediator,
            ILogger<StartUp_MainViewModel> logger,
            JamaConnectService jamaConnectService)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _jamaService = jamaConnectService ?? throw new ArgumentNullException(nameof(jamaConnectService));
        }
        
        // TEMPORARY: Jama Testing Command
        [RelayCommand]
        private async Task TestJama()
        {
            try
            {
                IsBusy = true;
                ClearLogs();
                LogMessage("🚀 Starting Jama Connect test...");
                
                StatusMessage = "Connecting to Jama...";
                StatusColor = "#FFA500";
                StatusVisibility = Visibility.Visible;
                ResultsVisibility = Visibility.Collapsed;
                LogsVisibility = Visibility.Visible;
                
                // NEW: Add authentication diagnostics
                await DiagnoseAuthentication();
                
                LogMessage($"✅ JamaConnectService available: {_jamaService.GetType().Name}");
                StatusMessage = "📡 Downloading requirements with Enhanced User Metadata from test project...";
                LogMessage("📡 Calling GetRequirementsWithUserMetadataAsync(636) for testing...");
                
                // Get requirements with enhanced user metadata (NOTE: Using project 636 for startup testing only)
                var jamaItems = await _jamaService.GetRequirementsWithUserMetadataAsync(636);
                LogMessage($"📦 Retrieved {jamaItems.Count} enhanced Jama items with user metadata");
                LogMessage($"🔍 DEBUG: First item enhanced check - ID: {jamaItems.FirstOrDefault()?.Id}, CreatedByName: '{jamaItems.FirstOrDefault()?.CreatedByName ?? "NULL"}'");
                
                // NEW: Test individual item endpoint based on API documentation
                if (jamaItems.Any())
                {
                    await TestIndividualItemEndpoint(jamaItems.First().Id);
                    
                    // NEW: Test user lookup functionality
                    await TestUserLookup(43408); // Use the known user ID from individual endpoint
                }
                
                LogMessage("🔄 Converting to Requirements format...");
                var requirements = await _jamaService.ConvertToRequirementsAsync(jamaItems);
                LogMessage($"✅ Converted to {requirements.Count} Requirements objects");
                
                StatusMessage = $"✅ Enhanced Success! Retrieved {requirements.Count} requirements with user metadata";
                StatusColor = "#4CAF50";
                
                // Create detailed results text
                var results = new StringBuilder();
                results.AppendLine($"=== ENHANCED JAMA CONNECT PARSING ANALYSIS ===");
                results.AppendLine($"Project: 636 (Decagon)");
                results.AppendLine($"Total Requirements: {requirements.Count}");
                results.AppendLine($"Enhanced with User Metadata: YES");
                results.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                results.AppendLine();
                
                // Analyze rich content
                LogMessage($"🔍 Analyzing rich content...");
                var withTables = requirements.Where(r => r.LooseContent?.Tables?.Count > 0).ToList();
                var withParagraphs = requirements.Where(r => r.LooseContent?.Paragraphs?.Count > 0).ToList();
                
                // Test CleanHtmlText method
                LogMessage("🧪 Testing CleanHtmlText method...");
                var testHtml = "Signal Name Voltage Expected Current CCA Connector: Pin # +3.3 V +3.3 V +/- 0.1 V &lt; 3 Amps P1: 7-9, 129-130 +1.8";
                if (_jamaService != null)
                {
                    // Use reflection to call private CleanHtmlText method
                    var cleanMethod = _jamaService.GetType().GetMethod("CleanHtmlText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (cleanMethod != null)
                    {
                        var cleanedResult = cleanMethod.Invoke(_jamaService, new[] { testHtml }) as string;
                        LogMessage($"🧪 Original: {testHtml}");
                        LogMessage($"🧪 Cleaned:  {cleanedResult}");
                        LogMessage($"🧪 CleanHtmlText working: {!(cleanedResult?.Contains("&lt;") == true) && !(cleanedResult?.Contains("&gt;") == true) && !(cleanedResult?.Contains("&amp;") == true)}");
                    }
                }
                
                // HTML Entity Analysis
                var withHtmlEntities = requirements.Where(r => 
                    (r.Description?.Contains("&gt;") == true) ||
                    (r.Description?.Contains("&lt;") == true) ||
                    (r.Description?.Contains("&amp;") == true) ||
                    (r.Description?.Contains("&#") == true) ||
                    (r.Name?.Contains("&gt;") == true) ||
                    (r.Name?.Contains("&lt;") == true) ||
                    (r.Name?.Contains("&amp;") == true) ||
                    (r.Name?.Contains("&#") == true)).ToList();
                
                LogMessage($"📊 Parsing analysis:");
                LogMessage($"   • {withTables.Count} requirements have tables");
                LogMessage($"   • {withParagraphs.Count} requirements have paragraphs");
                LogMessage($"   • {withHtmlEntities.Count} requirements have HTML entities");
                
                // Set sample field values from first requirement for display
                if (requirements.Any())
                {
                    var firstReq = requirements.First();
                    SampleCreatedBy = firstReq.CreatedBy ?? "[Empty]";
                    SampleProject = firstReq.Project ?? "[Empty]";
                    SampleRequirementDetail = (firstReq.Description?.Length > 200 ? 
                        firstReq.Description.Substring(0, 200) + "..." : 
                        firstReq.Description) ?? "[Empty]";
                    FieldDisplayVisibility = Visibility.Visible;
                    
                    LogMessage($"📋 Sample field values set from {firstReq.Item}");
                }
                
                results.AppendLine($"=== PARSING ISSUE ANALYSIS ===");
                results.AppendLine($"Requirements with HTML Entities: {withHtmlEntities.Count}");
                results.AppendLine($"Requirements with Tables: {withTables.Count}");
                results.AppendLine($"Requirements with Paragraphs: {withParagraphs.Count}");
                results.AppendLine();
                
                // Enhanced field mapping analysis with user metadata
                var withCreatedBy = requirements.Where(r => !string.IsNullOrWhiteSpace(r.CreatedBy)).Count();
                var withCreatedDate = requirements.Count(r => r.CreatedDate.HasValue);
                var withModifiedDate = requirements.Count(r => r.ModifiedDate.HasValue);
                var withProject = requirements.Where(r => !string.IsNullOrWhiteSpace(r.Project)).Count();
                var withRequirementType = requirements.Where(r => !string.IsNullOrWhiteSpace(r.RequirementType)).Count();
                var withKeyCharacteristics = requirements.Where(r => !string.IsNullOrWhiteSpace(r.KeyCharacteristics)).Count();
                
                results.AppendLine($"=== ENHANCED FIELD MAPPING ANALYSIS ===");
                results.AppendLine($"Requirements with CreatedBy Names: {withCreatedBy}/{requirements.Count}");
                results.AppendLine($"Requirements with Created Dates: {withCreatedDate}/{requirements.Count}");
                results.AppendLine($"Requirements with Modified Dates: {withModifiedDate}/{requirements.Count}");
                results.AppendLine($"Requirements with Project: {withProject}/{requirements.Count}");
                results.AppendLine($"Requirements with RequirementType: {withRequirementType}/{requirements.Count}");
                results.AppendLine($"Requirements with KeyCharacteristics: {withKeyCharacteristics}/{requirements.Count}");
                results.AppendLine();
                
                // User metadata success analysis
                if (withCreatedBy > 0)
                {
                    results.AppendLine($"✅ USER METADATA SUCCESS - {withCreatedBy} CreatedBy names resolved!");
                    var firstWithUser = requirements.FirstOrDefault(r => !string.IsNullOrEmpty(r.CreatedBy));
                    if (firstWithUser != null)
                    {
                        results.AppendLine($"Sample: {firstWithUser.Item} created by '{firstWithUser.CreatedBy}'");
                        if (firstWithUser.CreatedDate.HasValue)
                        {
                            results.AppendLine($"Created: {firstWithUser.CreatedDate:yyyy-MM-dd HH:mm:ss}");
                        }
                    }
                    results.AppendLine();
                }
                
                // HTML Entity Details
                if (withHtmlEntities.Any())
                {
                    results.AppendLine($"=== HTML ENTITY ISSUES (CRITICAL) ===");
                    results.AppendLine($"Found {withHtmlEntities.Count} requirements with unprocessed HTML entities:");
                    foreach (var req in withHtmlEntities.Take(5))
                    {
                        results.AppendLine($"• {req.Item} ({req.GlobalId}):");
                        if (req.Name?.Contains("&") == true)
                        {
                            results.AppendLine($"  NAME: {req.Name}");
                        }
                        if (req.Description?.Contains("&") == true)
                        {
                            var desc = req.Description.Length > 200 ? req.Description.Substring(0, 200) + "..." : req.Description;
                            results.AppendLine($"  DESC: {desc}");
                        }
                        results.AppendLine();
                    }
                } else {
                    results.AppendLine($"✅ NO HTML ENTITY ISSUES - CleanHtmlText working correctly");
                    results.AppendLine();
                }
                
                // Show table details
                if (withTables.Any())
                {
                    results.AppendLine($"=== TABLE PARSING DETAILS ===");
                    var tablesWithHeaders = withTables.Where(r => r.LooseContent?.Tables?.Any(t => t.ColumnHeaders?.Any() == true) == true).Count();
                    var tablesWithRows = withTables.Where(r => r.LooseContent?.Tables?.Any(t => t.Rows?.Any() == true) == true).Count();
                    
                    results.AppendLine($"Tables with headers: {tablesWithHeaders}/{withTables.Count}");
                    results.AppendLine($"Tables with rows: {tablesWithRows}/{withTables.Count}");
                    results.AppendLine();
                    
                    foreach (var req in withTables.Take(5))
                    {
                        results.AppendLine($"• {req.Item} ({req.GlobalId}): {req.LooseContent?.Tables?.Count} table(s)");
                        if (req.LooseContent?.Tables?.Any() == true)
                        {
                            var table = req.LooseContent.Tables.First();
                            results.AppendLine($"  Title: '{table.EditableTitle ?? "NO_TITLE"}'");
                            results.AppendLine($"  Headers: {(table.ColumnHeaders?.Any() == true ? string.Join(" | ", table.ColumnHeaders) : "NO_HEADERS")}");
                            results.AppendLine($"  Rows: {table.Rows?.Count ?? 0}");
                            if (table.Rows?.Any() == true)
                            {
                                var firstRow = table.Rows.First();
                                var cells = firstRow?.Take(3)?.Select(c => string.IsNullOrEmpty(c) ? "[EMPTY]" : (c.Length > 15 ? c.Substring(0, 15) + "..." : c)) ?? new[] { "[NO_CELLS]" };
                                results.AppendLine($"  Sample Row: {string.Join(" | ", cells)}");
                            }
                        }
                        results.AppendLine();
                    }
                } else {
                    results.AppendLine($"❌ NO TABLES FOUND - Table parsing may have issues");
                    results.AppendLine();
                }
                
                // Rich content extraction issues
                var missingContent = requirements.Where(r => 
                    string.IsNullOrWhiteSpace(r.Description) && 
                    (r.LooseContent?.Tables?.Count == 0 || r.LooseContent?.Tables == null) &&
                    (r.LooseContent?.Paragraphs?.Count == 0 || r.LooseContent?.Paragraphs == null)).ToList();
                
                if (missingContent.Any())
                {
                    results.AppendLine($"=== MISSING CONTENT ISSUES ===");
                    results.AppendLine($"Requirements with no description, tables, or paragraphs: {missingContent.Count}");
                    foreach (var req in missingContent.Take(5))
                    {
                        results.AppendLine($"• {req.Item} ({req.GlobalId}): {req.Name}");
                    }
                    results.AppendLine();
                }
                
                // Show detailed sample requirements with full details AND raw Jama data
                results.AppendLine($"=== DETAILED SAMPLE REQUIREMENTS ===");
                foreach (var req in requirements.Take(3))
                {
                    results.AppendLine($"REQUIREMENT: {req.Item} ({req.GlobalId})");
                    results.AppendLine($"Name: {req.Name ?? "NULL"}");
                    results.AppendLine($"CreatedBy: {req.CreatedBy ?? "NULL"}");
                    results.AppendLine($"Project: {req.Project ?? "NULL"}");
                    results.AppendLine($"RequirementType: {req.RequirementType ?? "NULL"}");
                    results.AppendLine($"Status: {req.Status ?? "NULL"}");
                    results.AppendLine($"Description Length: {req.Description?.Length ?? 0} chars");
                    if (!string.IsNullOrEmpty(req.Description))
                    {
                        var desc = req.Description.Length > 300 ? req.Description.Substring(0, 300) + "..." : req.Description;
                        results.AppendLine($"RequirementDetail Preview: {desc.Replace("\n", "\\n").Replace("\r", "\\r")}");
                    }
                    results.AppendLine($"KeyCharacteristics: {req.KeyCharacteristics ?? "NULL"}");
                    results.AppendLine($"LooseContent Tables: {req.LooseContent?.Tables?.Count ?? 0}");
                    results.AppendLine($"LooseContent Paragraphs: {req.LooseContent?.Paragraphs?.Count ?? 0}");
                    
                    // ADD: Show raw Jama API data for this requirement to debug field mapping
                    var correspondingJamaItem = jamaItems.FirstOrDefault(j => j.GlobalId == req.GlobalId || j.Item == req.Item);
                    if (correspondingJamaItem != null)
                    {
                        results.AppendLine($"RAW JAMA DATA:");
                        results.AppendLine($"  Jama.Fields.CreatedBy: {correspondingJamaItem.Fields?.CreatedBy ?? "NULL"}"); 
                        results.AppendLine($"  Jama.Project: {correspondingJamaItem.Project?.ToString() ?? "NULL"}");
                        results.AppendLine($"  Jama.Fields.ModifiedBy: {correspondingJamaItem.Fields?.ModifiedBy ?? "NULL"}");
                        results.AppendLine($"  Jama.Status: {correspondingJamaItem.Status ?? "NULL"}");
                        results.AppendLine($"  Jama.Fields.Status: {correspondingJamaItem.Fields?.Status ?? "NULL"}");
                        results.AppendLine($"  Jama.Fields.CreatedDate: {correspondingJamaItem.Fields?.CreatedDate ?? "NULL"}");
                        results.AppendLine($"  Jama.Fields.ModifiedDate: {correspondingJamaItem.Fields?.ModifiedDate ?? "NULL"}");
                    }
                    else
                    {
                        results.AppendLine($"RAW JAMA DATA: No matching Jama item found");
                    }
                    if (req.LooseContent?.Tables?.Any() == true)
                    {
                        var table = req.LooseContent.Tables.First();
                        results.AppendLine($"First Table Title: '{table.EditableTitle ?? "NO_TITLE"}'");
                        results.AppendLine($"First Table Headers: {table.ColumnHeaders?.Count ?? 0} headers");
                        results.AppendLine($"First Table Rows: {table.Rows?.Count ?? 0} rows");
                    }
                    results.AppendLine("---");
                    results.AppendLine();
                }
                
                ResultsText = results.ToString();
                ResultsVisibility = Visibility.Visible;
                CopyButtonVisibility = Visibility.Visible;
                
                LogMessage("✅ Analysis complete! Click 'Copy All Results & Logs' to copy data for analysis.");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Error: {ex.Message}";
                StatusColor = "#FF6B6B";
                
                ResultsText = $"ERROR DETAILS:\n{ex}";
                ResultsVisibility = Visibility.Visible;
            }
            finally
            {
                IsBusy = false;
            }
        }
        
        // ENHANCED: Jama Testing Command with User Metadata
        [RelayCommand]
        private async Task TestEnhancedJama()
        {
            try
            {
                IsBusy = true;
                ClearLogs();
                LogMessage("🚀 Starting Enhanced Jama Connect test with user metadata...");
                
                StatusMessage = "Connecting to Jama with Enhanced Import...";
                StatusColor = "#FF9800";
                StatusVisibility = Visibility.Visible;
                ResultsVisibility = Visibility.Collapsed;
                LogsVisibility = Visibility.Visible;
                
                await DiagnoseAuthentication();
                
                LogMessage($"✅ JamaConnectService available: {_jamaService.GetType().Name}");
                StatusMessage = "📡 Downloading requirements with Enhanced User Metadata from test project...";
                LogMessage("📡 Calling GetRequirementsWithUserMetadataAsync(636) for testing...");
                
                // Get requirements with enhanced user metadata (NOTE: Using project 636 for startup testing only)
                var jamaItems = await _jamaService.GetRequirementsWithUserMetadataAsync(636);
                LogMessage($"📦 Retrieved {jamaItems.Count} enhanced Jama items with user metadata");
                
                LogMessage("🔄 Converting to Requirements format...");
                var requirements = await _jamaService.ConvertToRequirementsAsync(jamaItems);
                LogMessage($"✅ Converted to {requirements.Count} Requirements objects");
                
                StatusMessage = $"✅ Enhanced Success! Retrieved {requirements.Count} requirements with user metadata";
                StatusColor = "#4CAF50";
                
                // Create enhanced analysis results
                var results = new StringBuilder();
                results.AppendLine($"=== ENHANCED JAMA CONNECT PARSING ANALYSIS ===");
                results.AppendLine($"Project: 636 (Decagon)");
                results.AppendLine($"Total Requirements: {requirements.Count}");
                results.AppendLine($"Enhanced with User Metadata: YES");
                results.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                results.AppendLine();
                
                // Enhanced field mapping analysis
                var withCreatedBy = requirements.Where(r => !string.IsNullOrWhiteSpace(r.CreatedBy)).Count();
                var withCreatedDate = requirements.Count(r => r.CreatedDate.HasValue);
                var withProject = requirements.Where(r => !string.IsNullOrWhiteSpace(r.Project)).Count();
                
                results.AppendLine($"=== ENHANCED USER METADATA ANALYSIS ===");
                results.AppendLine($"Requirements with CreatedBy Names: {withCreatedBy}/{requirements.Count}");
                results.AppendLine($"Requirements with Created Dates: {withCreatedDate}/{requirements.Count}");
                results.AppendLine($"Requirements with Project: {withProject}/{requirements.Count}");
                results.AppendLine();
                
                if (withCreatedBy > 0)
                {
                    results.AppendLine($"✅ USER METADATA SUCCESS - {withCreatedBy} CreatedBy names resolved!");
                    var firstWithUser = requirements.FirstOrDefault(r => !string.IsNullOrEmpty(r.CreatedBy));
                    if (firstWithUser != null)
                    {
                        results.AppendLine($"Sample: {firstWithUser.Item} created by '{firstWithUser.CreatedBy}'");
                    }
                }
                else
                {
                    results.AppendLine($"❌ USER METADATA ISSUE - No CreatedBy names found");
                }
                results.AppendLine();
                
                ResultsText = results.ToString();
                ResultsVisibility = Visibility.Visible;
                CopyButtonVisibility = Visibility.Visible;
                
                LogMessage("✅ Enhanced analysis complete! User metadata integration tested.");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Error: {ex.Message}";
                StatusColor = "#FF6B6B";
                
                ResultsText = $"ERROR DETAILS:\n{ex}";
                ResultsVisibility = Visibility.Visible;
            }
            finally
            {
                IsBusy = false;
            }
        }
        
        /// <summary>
        /// Add a timestamped log message
        /// </summary>
        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            LogText += $"[{timestamp}] {message}\n";
        }
        
        /// <summary>
        /// Clear the log display
        /// </summary>
        private void ClearLogs()
        {
            LogText = string.Empty;
        }
        
        // TEMPORARY: Copy All Data Command
        [RelayCommand]
        private void CopyAllData()
        {
            try
            {
                var copyData = new StringBuilder();
                copyData.AppendLine("=== JAMA CONNECT TROUBLESHOOTING DATA ===");
                copyData.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                copyData.AppendLine();
                
                // Add status
                copyData.AppendLine("=== STATUS ===");
                copyData.AppendLine($"Current Status: {StatusMessage}");
                copyData.AppendLine();
                
                // Add results
                if (!string.IsNullOrWhiteSpace(ResultsText))
                {
                    copyData.AppendLine("=== RESULTS ===");
                    copyData.AppendLine(ResultsText);
                    copyData.AppendLine();
                }
                
                // Add logs
                if (!string.IsNullOrWhiteSpace(LogText))
                {
                    copyData.AppendLine("=== LIVE LOGS ===");
                    copyData.AppendLine(LogText);
                    copyData.AppendLine();
                }
                
                copyData.AppendLine("=== END TROUBLESHOOTING DATA ===");
                
                // Copy to clipboard
                Clipboard.SetText(copyData.ToString());
                
                // Brief feedback
                var originalStatus = StatusMessage;
                var originalColor = StatusColor;
                StatusMessage = "📋 Copied to clipboard!";
                StatusColor = "#4CAF50";
                
                // Reset status after 2 seconds
                Task.Delay(2000).ContinueWith(_ => 
                {
                    Application.Current.Dispatcher.Invoke(() => 
                    {
                        StatusMessage = originalStatus;
                        StatusColor = originalColor;
                    });
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Copy failed: {ex.Message}";
                StatusColor = "#FF6B6B";
            }
        }
        
        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====
        
        protected override async Task SaveAsync()
        {
            await Task.Delay(100);
        }
        
        protected override void Cancel()
        {
            Title = "Systems ATE APP";
        }
        
        protected override async Task RefreshAsync()
        {
            await Task.Delay(50);
        }
        
        /// <summary>
        /// Test individual item endpoint for user metadata availability
        /// Based on API documentation findings: /abstractitems/{id}?include=createdBy
        /// </summary>
        private async Task TestIndividualItemEndpoint(int itemId)
        {
            try
            {
            if (_jamaService != null)
            {
                LogMessage($"🔬 TESTING INDIVIDUAL ITEM ENDPOINT for Item ID: {itemId}");
                
                // Use reflection to access the HttpClient from JamaConnectService
                var httpClientField = _jamaService!.GetType().GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var baseUrlField = _jamaService.GetType().GetField("_baseUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (httpClientField?.GetValue(_jamaService) is HttpClient httpClient && 
                    baseUrlField?.GetValue(_jamaService) is string baseUrl)
                {
                    // Test 1: Individual item with include parameter
                    var individualUrl = $"{baseUrl}/rest/v1/abstractitems/{itemId}?include=createdBy,modifiedBy";
                    LogMessage($"🌐 API URL: {individualUrl}");
                    
                    var response = await httpClient.GetAsync(individualUrl);
                    LogMessage($"📊 Response Status: {response.StatusCode}");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        LogMessage($"📄 Response Length: {json.Length} characters");
                        
                        // Parse and examine the response structure
                        try
                        {
                            var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
                            var root = jsonDoc.RootElement;
                            
                            // Check main data section
                            if (root.TryGetProperty("data", out var dataElement))
                            {
                                LogMessage($"📋 Data section found");
                                
                                if (dataElement.TryGetProperty("createdBy", out var createdByElement))
                                {
                                    LogMessage($"👤 CreatedBy in data: {createdByElement}");
                                }
                                else
                                {
                                    LogMessage($"❌ No createdBy in data section");
                                }
                            }
                            
                            // Check linked section (where full objects should be)
                            if (root.TryGetProperty("linked", out var linkedElement))
                            {
                                LogMessage($"🔗 Linked section found: {linkedElement}");
                                // Look for user objects in linked section
                            }
                            else
                            {
                                LogMessage($"❌ No linked section found");
                            }
                            
                            // Log first 500 chars of response for inspection
                            var preview = json.Length > 500 ? json.Substring(0, 500) + "..." : json;
                            LogMessage($"📄 Response Preview: {preview}");
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"❌ JSON Parse Error: {ex.Message}");
                        }
                    }
                    else
                    {
                        LogMessage($"❌ API Call Failed: {response.StatusCode} - {response.ReasonPhrase}");
                        var errorContent = await response.Content.ReadAsStringAsync();
                        LogMessage($"❌ Error Content: {errorContent}");
                    }
                }
                else
                {
                    LogMessage($"❌ Could not access JamaConnectService internals for individual endpoint test");
                }
            }
            else
            {
                LogMessage($"❌ JamaConnectService not available for individual endpoint test");
            }
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Individual item endpoint test failed: {ex.Message}");
        }
        }
        
        /// <summary>
        /// Diagnose authentication setup and test OAuth token acquisition
        /// </summary>
        private async Task DiagnoseAuthentication()
        {
            try
            {
                LogMessage("🔐 AUTHENTICATION DIAGNOSTICS");
                
                // Check environment variables
                var baseUrl = Environment.GetEnvironmentVariable("JAMA_BASE_URL");
                var clientId = Environment.GetEnvironmentVariable("JAMA_CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("JAMA_CLIENT_SECRET");
                
                LogMessage($"📍 JAMA_BASE_URL: {baseUrl ?? "[NOT SET]"}");
                LogMessage($"🆔 JAMA_CLIENT_ID: {clientId ?? "[NOT SET]"}");
                LogMessage($"🔑 JAMA_CLIENT_SECRET: {(!string.IsNullOrEmpty(clientSecret) ? "[SET - " + clientSecret.Length + " chars]" : "[NOT SET]")}");
                
                if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    LogMessage("❌ CRITICAL: Missing required environment variables!");
                    LogMessage("   Set JAMA_BASE_URL, JAMA_CLIENT_ID, JAMA_CLIENT_SECRET");
                    return;
                }
                
                // Test OAuth token acquisition manually
                LogMessage("🔄 Testing OAuth token acquisition...");
                await TestOAuthTokenManually(baseUrl, clientId, clientSecret);
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Authentication diagnosis failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Manually test OAuth token acquisition with detailed logging
        /// </summary>
        private async Task TestOAuthTokenManually(string baseUrl, string clientId, string clientSecret)
        {
            try
            {
                using var httpClient = new HttpClient();
                var tokenUrl = $"{baseUrl}/rest/oauth/token";
                LogMessage($"🌐 Token URL: {tokenUrl}");
                
                var authBytes = System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}");
                var authHeader = Convert.ToBase64String(authBytes);
                LogMessage($"🔐 Auth Header Length: {authHeader.Length} characters");
                
                var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
                
                var form = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("scope", "token_information")
                ]);
                
                request.Content = form;
                LogMessage("📤 Sending OAuth request...");
                
                var response = await httpClient.SendAsync(request);
                LogMessage($"📥 OAuth Response: {response.StatusCode} - {response.ReasonPhrase}");
                
                var content = await response.Content.ReadAsStringAsync();
                LogMessage($"📄 Response Length: {content.Length} characters");
                
                if (response.IsSuccessStatusCode)
                {
                    LogMessage("✅ OAuth token request successful!");
                    var preview = content.Length > 200 ? content.Substring(0, 200) + "..." : content;
                    LogMessage($"📋 Response Preview: {preview}");
                }
                else
                {
                    LogMessage("❌ OAuth token request failed!");
                    LogMessage($"📋 Error Response: {content}");
                    
                    // Common OAuth errors
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        LogMessage("🚨 UNAUTHORIZED: Check client ID and secret");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        LogMessage("🚨 NOT FOUND: Check base URL and OAuth endpoint");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Manual OAuth test failed: {ex.Message}");
            }
        }
        
        // ENHANCED: Test new cookbook-based API features
        [RelayCommand]
        private async Task TestEnhancedFeatures()
        {
            try
            {
                IsBusy = true;
                ClearLogs();
                LogMessage("🚀 Testing Enhanced Jama API Features (Cookbook Patterns)...");
                
                StatusMessage = "Testing enhanced features...";
                StatusColor = "#0066CC";
                StatusVisibility = Visibility.Visible;
                
                if (_jamaService == null)
                {
                    LogMessage("❌ JamaConnectService not available");
                    return;
                }
                
                await TestAbstractItemsSearch();
                await TestFileDownload();
                await TestActivitiesTracking();
                await TestEnhancedRequirements();
                
                LogMessage("✅ All enhanced feature tests completed!");
                StatusMessage = "Enhanced features test completed successfully!";
                StatusColor = "#00AA00";
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Enhanced features test failed: {ex.Message}");
                StatusMessage = "Enhanced features test failed";
                StatusColor = "#FF0000";
            }
            finally
            {
                IsBusy = false;
            }
        }
        
        private async Task TestAbstractItemsSearch()
        {
            LogMessage("🔍 TESTING ABSTRACT ITEMS SEARCH");
            
            try 
            {
                // Test enhanced search with filtering
                var searchResults = await _jamaService!.SearchAbstractItemsAsync(
                    projectId: 45,
                    contains: "requirement",
                    maxResults: 5,
                    includeParams: new List<string> { "data.createdBy", "data.modifiedBy" }
                );
                
                LogMessage($"✅ Abstract search found {searchResults.Count} items");
                
                foreach (var req in searchResults.Take(3))
                {
                    LogMessage($"   📄 {req.Name} (ID: {req.Id})");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Abstract search failed: {ex.Message}");
            }
        }
        
        private async Task TestFileDownload()
        {
            LogMessage("📁 TESTING FILE DOWNLOAD CAPABILITIES");
            
            try
            {
                // Test extraction of attachment URLs from rich text
                var sampleRichText = "<img src=\"https://testjama.com/attachment/123/sample.png\"> <a href=\"https://testjama.com/attachment/456/doc.pdf\">Document</a>";
                var attachmentUrls = _jamaService!.ExtractAttachmentUrls(sampleRichText);
                
                LogMessage($"✅ Extracted {attachmentUrls.Count} attachment URLs from rich text");
                
                foreach (var url in attachmentUrls)
                {
                    LogMessage($"   🔗 Found attachment: {url}");
                }
                
                // Note: Can't actually download without valid URLs, but the pattern is ready
                LogMessage("📋 File download patterns ready for real attachment URLs");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ File download test failed: {ex.Message}");
            }
        }
        
        private async Task TestActivitiesTracking()
        {
            LogMessage("📊 TESTING ACTIVITIES TRACKING");
            
            try
            {
                // Test activities for change detection
                var activities = await _jamaService!.GetActivitiesAsync(
                    projectId: 45,
                    maxResults: 5
                );
                
                LogMessage($"✅ Retrieved {activities.Count} recent activities");
                
                foreach (var activity in activities.Take(3))
                {
                    LogMessage($"   📅 {activity.Date:HH:mm}: {activity.Action} by {activity.UserName}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Activities tracking failed: {ex.Message}");
            }
        }
        
        private async Task TestEnhancedRequirements()
        {
            LogMessage("⚡ TESTING ENHANCED REQUIREMENTS RETRIEVAL");
            
            try
            {
                // Test enhanced requirements with include parameters
                var enhancedReqs = await _jamaService!.GetRequirementsEnhancedAsync(45);
                
                LogMessage($"✅ Enhanced retrieval found {enhancedReqs.Count} requirements");
                LogMessage($"   Using retry logic, rate limiting, and include parameters");
                
                var sampleReq = enhancedReqs.FirstOrDefault();
                if (sampleReq != null)
                {
                    LogMessage($"📋 Sample requirement: {sampleReq.Name}");
                    LogMessage($"   Created by: {sampleReq.CreatedBy ?? "(unknown)"}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Enhanced requirements test failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Test user lookup functionality with the known user ID from individual endpoint
        /// </summary>
        private async Task TestUserLookup(int userId = 43408)
        {
            try
            {
                if (_jamaService != null)
                {
                    LogMessage($"👤 TESTING USER LOOKUP for User ID: {userId}");
                    
                    // Test the new GetUserAsync method
                    var user = await _jamaService!.GetUserAsync(userId);
                
                if (user != null)
                {
                    LogMessage($"✅ User lookup successful!");
                    LogMessage($"📋 User Details:");
                    LogMessage($"   🆔 ID: {user.Id}");
                    LogMessage($"   👤 Name: {user.FirstName} {user.LastName}");
                    LogMessage($"   📧 Email: {user.Email}");
                    LogMessage($"   🏢 Username: {user.Username}");
                    LogMessage($"   ✅ Active: {user.Active}");
                }
                else
                {
                    LogMessage($"❌ User lookup failed - returned null");
                }
                
                // Test the new GetItemWithUserMetadataAsync method
                LogMessage($"🔬 TESTING ITEM WITH USER METADATA");
                
                // Use a known item ID (18747356 from previous tests)
                var itemWithUser = await _jamaService!.GetItemWithUserMetadataAsync(18747356);
                
                if (itemWithUser != null)
                {
                    LogMessage($"✅ Item with user metadata successful!");
                    LogMessage($"📋 Item Details:");
                    LogMessage($"   🆔 Item ID: {itemWithUser.Id}");
                    LogMessage($"   👤 CreatedBy ID: {itemWithUser.CreatedBy}");
                    LogMessage($"   👤 CreatedBy Name: {itemWithUser.CreatedByName}");
                    LogMessage($"   📅 Created Date: {itemWithUser.CreatedDate}");
                    LogMessage($"   🔄 ModifiedBy ID: {itemWithUser.ModifiedBy}");
                    LogMessage($"   🔄 ModifiedBy Name: {itemWithUser.ModifiedByName}");
                    LogMessage($"   📅 Modified Date: {itemWithUser.ModifiedDate}");
                    
                    if (!string.IsNullOrEmpty(itemWithUser.CreatedByName))
                    {
                        LogMessage($"🎉 SUCCESS: User name resolution working! CreatedBy resolved to: '{itemWithUser.CreatedByName}'");
                    }
                    else
                    {
                        LogMessage($"⚠️ WARNING: User name resolution returned empty - check user lookup");
                    }
                }
                else
                {
                    LogMessage($"❌ Item with user metadata failed - returned null");
                }
            }
            else
            {
                LogMessage($"❌ JamaConnectService not available for user lookup test");
            }
        }
        catch (Exception ex)
        {
            LogMessage($"❌ User lookup test failed: {ex.Message}");
        }
        }
        
        protected override bool CanSave() => !IsBusy;
        protected override bool CanCancel() => true;
        protected override bool CanRefresh() => !IsBusy;
    }
}