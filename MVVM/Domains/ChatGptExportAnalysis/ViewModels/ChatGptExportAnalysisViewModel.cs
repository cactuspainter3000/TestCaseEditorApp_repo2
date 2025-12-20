using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.ChatGptExportAnalysis.ViewModels
{
    /// <summary>
    /// Domain ViewModel responsible for ChatGPT export and analysis functionality.
    /// Extracted from MainViewModel to handle all ChatGPT-related export operations.
    /// </summary>
    public partial class ChatGptExportAnalysisViewModel : ObservableObject
    {
        private readonly ChatGptExportService _chatGptExportService;
        private readonly ILogger<ChatGptExportAnalysisViewModel>? _logger;

        // Status callback to communicate with parent ViewModel
        private readonly Action<string, int>? _setTransientStatus;
        
        // Data access delegates from MainViewModel
        private readonly Func<Requirement?> _getCurrentRequirement;
        private readonly Func<IEnumerable<Requirement>> _getRequirements;

        [ObservableProperty]
        private string? lastChatGptExportFilePath;

        public ChatGptExportAnalysisViewModel(
            ChatGptExportService chatGptExportService,
            Func<Requirement?> getCurrentRequirement,
            Func<IEnumerable<Requirement>> getRequirements,
            Action<string, int>? setTransientStatus = null,
            ILogger<ChatGptExportAnalysisViewModel>? logger = null)
        {
            _chatGptExportService = chatGptExportService ?? throw new ArgumentNullException(nameof(chatGptExportService));
            _getCurrentRequirement = getCurrentRequirement ?? throw new ArgumentNullException(nameof(getCurrentRequirement));
            _getRequirements = getRequirements ?? throw new ArgumentNullException(nameof(getRequirements));
            _setTransientStatus = setTransientStatus;
            _logger = logger;

            InitializeCommands();
        }

        private void InitializeCommands()
        {
            ExportCurrentForChatGptCommand = new RelayCommand(
                () => ExportCurrentRequirementForChatGpt(), 
                () => _getCurrentRequirement() != null);
                
            ExportSelectedForChatGptCommand = new RelayCommand(
                () => ExportSelectedRequirementsForChatGpt());
                
            BatchAnalyzeCommand = new RelayCommand(
                () => BatchAnalyzeAllRequirements(), 
                () => _getRequirements().Any());
        }

        #region Commands

        public ICommand ExportCurrentForChatGptCommand { get; private set; } = null!;
        public ICommand ExportSelectedForChatGptCommand { get; private set; } = null!;
        public ICommand BatchAnalyzeCommand { get; private set; } = null!;

        #endregion

        #region Export Methods

        /// <summary>
        /// Exports the current requirement for ChatGPT analysis.
        /// </summary>
        public void ExportCurrentRequirementForChatGpt()
        {
            try
            {
                var currentRequirement = _getCurrentRequirement();
                if (currentRequirement == null)
                {
                    _setTransientStatus?.Invoke("No requirement selected for export.", 3);
                    return;
                }

                // Export to clipboard
                bool clipboardSuccess = _chatGptExportService.ExportAndCopy(currentRequirement, includeAnalysisRequest: true);
                
                // Also save to file
                string formattedText = _chatGptExportService.ExportSingleRequirement(currentRequirement, includeAnalysisRequest: true);
                string fileName = $"Requirement_{currentRequirement.Item?.Replace("/", "_").Replace("\\", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.md";
                string filePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
                
                try
                {
                    System.IO.File.WriteAllText(filePath, formattedText);
                    LastChatGptExportFilePath = filePath;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Saved single requirement to file: {filePath}");
                }
                catch (Exception fileEx)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[CHATGPT EXPORT] Failed to save single requirement file: {fileEx.Message}");
                }
                
                if (clipboardSuccess)
                {
                    _setTransientStatus?.Invoke($"✅ Requirement {currentRequirement.Item} exported to clipboard and saved to {fileName}!", 5);
                }
                else
                {
                    _setTransientStatus?.Invoke($"⚠️ Export saved to {fileName} but clipboard copy failed.", 4);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to export current requirement for ChatGPT");
                _setTransientStatus?.Invoke("Error exporting requirement for ChatGPT.", 3);
            }
        }

        /// <summary>
        /// Exports selected/all requirements for ChatGPT analysis.
        /// </summary>
        public void ExportSelectedRequirementsForChatGpt()
        {
            try
            {
                // For now, export all requirements - could be extended to support selection
                var requirementsToExport = _getRequirements().ToList();
                
                if (!requirementsToExport.Any())
                {
                    _setTransientStatus?.Invoke("No requirements available for export.", 3);
                    return;
                }

                bool success = _chatGptExportService.ExportAndCopyMultiple(requirementsToExport, includeAnalysisRequest: true);
                
                if (success)
                {
                    _setTransientStatus?.Invoke($"{requirementsToExport.Count} requirements exported to clipboard for ChatGPT analysis!", 4);
                }
                else
                {
                    _setTransientStatus?.Invoke("Failed to export requirements to clipboard.", 3);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to export requirements for ChatGPT");
                _setTransientStatus?.Invoke("Error exporting requirements for ChatGPT.", 3);
            }
        }

        /// <summary>
        /// Batch export requirements for ChatGPT analysis in background after import.
        /// Shows progress notifications and exports requirements in ChatGPT-ready format.
        /// </summary>
        public void BatchExportRequirementsForChatGpt(List<Requirement> requirements)
        {
            if (!requirements.Any())
                return;

            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Starting export for {requirements.Count} requirements");
                
                // Show progress notification
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _setTransientStatus?.Invoke($"Exporting {requirements.Count} requirements for ChatGPT analysis...", 3);
                });

                // Export requirements using the service
                string formattedText = _chatGptExportService.ExportMultipleRequirements(requirements, includeAnalysisRequest: true);
                
                // Save to file and copy to clipboard
                bool clipboardSuccess = _chatGptExportService.CopyToClipboard(formattedText);
                
                // Optionally save to file as well
                string fileName = $"Requirements_Export_{DateTime.Now:yyyyMMdd_HHmmss}.md";
                string filePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
                
                try
                {
                    System.IO.File.WriteAllText(filePath, formattedText);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Saved to file: {filePath}");
                    
                    // Update the last exported file path on UI thread
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        LastChatGptExportFilePath = filePath;
                    });
                }
                catch (Exception fileEx)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[CHATGPT EXPORT] Failed to save file, but clipboard export may have succeeded: {fileEx.Message}");
                }

                // Show completion notification
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (clipboardSuccess)
                    {
                        _setTransientStatus?.Invoke($"✅ {requirements.Count} requirements exported for ChatGPT! Copied to clipboard and saved to {fileName}", 6);
                    }
                    else
                    {
                        _setTransientStatus?.Invoke($"⚠️ Export completed but clipboard copy failed. File saved to {fileName}", 5);
                    }
                });

                TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Completed export for {requirements.Count} requirements, clipboard={clipboardSuccess}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[CHATGPT EXPORT] Failed to export requirements: {ex.Message}");
                
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _setTransientStatus?.Invoke("❌ Failed to export requirements for ChatGPT.", 4);
                });
            }
        }

        /// <summary>
        /// Batch analyze all requirements (placeholder).
        /// TODO: This should probably be moved to a separate RequirementAnalysisViewModel
        /// </summary>
        private void BatchAnalyzeAllRequirements()
        {
            try
            {
                var requirements = _getRequirements().ToList();
                var totalCount = requirements.Count;
                _setTransientStatus?.Invoke($"⚡ Starting batch analysis of {totalCount} requirements...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[ANALYSIS] Batch analyze all requested for {totalCount} requirements");
                
                // TODO: Implement actual batch analysis logic
                // This could trigger analysis for all requirements in sequence or parallel
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ANALYSIS] Failed to batch analyze all requirements: {ex.Message}");
                _setTransientStatus?.Invoke("❌ Failed to batch analyze all requirements", 3);
            }
        }

        /// <summary>
        /// Paste ChatGPT analysis functionality - allows importing analysis results from ChatGPT
        /// </summary>
        public void PasteChatGptAnalysis()
        {
            try
            {
                _setTransientStatus?.Invoke("📋 Paste ChatGPT analysis coming soon...", 3);
                _logger?.LogInformation("ChatGPT analysis paste requested");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to paste ChatGPT analysis");
                _setTransientStatus?.Invoke("❌ Failed to paste analysis", 3);
            }
        }

        /// <summary>
        /// Get the latest LLM draft text for a requirement
        /// </summary>
        public string GetLatestLlmDraftText(Requirement req)
        {
            try
            {
                // TODO: Implement actual LLM draft text retrieval
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Failed to get latest LLM draft text for requirement {req?.Item}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Build strict output from saved requirement data
        /// </summary>
        public string BuildStrictOutputFromSaved(Requirement req)
        {
            try
            {
                // TODO: Implement actual strict output building from saved data
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Failed to build strict output from saved data for requirement {req?.Item}");
                return string.Empty;
            }
        }

        #endregion
    }
}