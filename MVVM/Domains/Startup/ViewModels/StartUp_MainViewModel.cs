using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
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
        
        private readonly JamaConnectService? _jamaService;
        
        public StartUp_MainViewModel(
            IStartupMediator mediator,
            ILogger<StartUp_MainViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            
            // TEMPORARY: Try to get JamaConnectService for troubleshooting
            try
            {
                _jamaService = App.ServiceProvider?.GetService(typeof(JamaConnectService)) as JamaConnectService;
            }
            catch (Exception ex)
            {
                // Ignore - troubleshooting interface will handle missing service
            }
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
                
                if (_jamaService == null)
                {
                    StatusMessage = "❌ JamaConnectService not available";
                    StatusColor = "#FF6B6B";
                    LogMessage("❌ ERROR: JamaConnectService not configured or available");
                    LogMessage("   Check environment variables: JAMA_BASE_URL, JAMA_CLIENT_ID, JAMA_CLIENT_SECRET");
                    return;
                }
                
                LogMessage($"✅ JamaConnectService available: {_jamaService.GetType().Name}");
                StatusMessage = "📡 Downloading requirements from Project 636...";
                LogMessage("📡 Calling GetRequirementsAsync(636)...");
                
                // Get requirements from Decagon project (636)
                var jamaItems = await _jamaService.GetRequirementsAsync(636);
                LogMessage($"📦 Retrieved {jamaItems.Count} raw Jama items");
                
                LogMessage("🔄 Converting to Requirements format...");
                var requirements = await _jamaService.ConvertToRequirementsAsync(jamaItems);
                LogMessage($"✅ Converted to {requirements.Count} Requirements objects");
                
                StatusMessage = $"✅ Success! Retrieved {requirements.Count} requirements";
                StatusColor = "#4CAF50";
                
                // Create detailed results text
                var results = new StringBuilder();
                results.AppendLine($"=== JAMA CONNECT TEST RESULTS ===");
                results.AppendLine($"Project: 636 (Decagon)");
                results.AppendLine($"Total Requirements: {requirements.Count}");
                results.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                results.AppendLine();
                
                // Analyze rich content
                LogMessage("🔍 Analyzing rich content...");
                var withTables = requirements.Where(r => r.LooseContent?.Tables?.Count > 0).ToList();
                var withParagraphs = requirements.Where(r => r.LooseContent?.Paragraphs?.Count > 0).ToList();
                
                LogMessage($"📊 Rich content analysis:");
                LogMessage($"   • {withTables.Count} requirements have tables");
                LogMessage($"   • {withParagraphs.Count} requirements have paragraphs");
                
                results.AppendLine($"=== RICH CONTENT ANALYSIS ===");
                results.AppendLine($"Requirements with Tables: {withTables.Count}");
                results.AppendLine($"Requirements with Paragraphs: {withParagraphs.Count}");
                results.AppendLine();
                
                // Show table details
                if (withTables.Any())
                {
                    results.AppendLine($"=== TABLE DETAILS ===");
                    foreach (var req in withTables.Take(5))
                    {
                        results.AppendLine($"• {req.Item} ({req.GlobalId}): {req.LooseContent?.Tables?.Count} table(s)");
                        if (req.LooseContent?.Tables?.Any() == true)
                        {
                            var table = req.LooseContent.Tables.First();
                            results.AppendLine($"  Title: {table.EditableTitle}");
                            results.AppendLine($"  Headers: {string.Join(" | ", table.ColumnHeaders ?? new List<string>())}");
                            results.AppendLine($"  Rows: {table.Rows?.Count ?? 0}");
                        }
                        results.AppendLine();
                    }
                }
                
                // Show sample requirements
                results.AppendLine($"=== SAMPLE REQUIREMENTS ===");
                foreach (var req in requirements.Take(10))
                {
                    results.AppendLine($"• {req.Item}: {req.Name}");
                    if (!string.IsNullOrEmpty(req.Description))
                    {
                        var desc = req.Description.Length > 100 ? req.Description.Substring(0, 100) + "..." : req.Description;
                        results.AppendLine($"  Description: {desc.Replace("\n", " ").Replace("\r", "")}");
                    }
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
        
        protected override bool CanSave() => !IsBusy;
        protected override bool CanCancel() => true;
        protected override bool CanRefresh() => !IsBusy;
    }
}