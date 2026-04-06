using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for exporting requirements in formats optimized for ChatGPT analysis.
    /// </summary>
    public sealed class ChatGptExportService
    {
        /// <summary>
        /// Export a single requirement formatted for ChatGPT analysis.
        /// </summary>
        /// <param name="requirement">The requirement to export</param>
        /// <param name="includeAnalysisRequest">Whether to include the analysis request text</param>
        /// <returns>Formatted text ready for ChatGPT</returns>
        public string ExportSingleRequirement(Requirement requirement, bool includeAnalysisRequest = true)
        {
            if (requirement == null)
                throw new ArgumentNullException(nameof(requirement));

            var sb = new StringBuilder();

            if (includeAnalysisRequest)
            {
                sb.AppendLine("## Requirement Analysis Request");
                sb.AppendLine();
            }

            // Basic requirement info
            sb.AppendLine($"**ID:** {requirement.Item ?? "Unknown"}");
            sb.AppendLine($"**Name:** {requirement.Name ?? "No name provided"}");
            sb.AppendLine();
            sb.AppendLine("**Requirement Text:**");
            sb.AppendLine(requirement.Description ?? "No description provided");
            sb.AppendLine();

            // Add associated tables if present
            if (requirement.Tables != null && requirement.Tables.Any())
            {
                sb.AppendLine("**Associated Tables:**");
                sb.AppendLine();
                
                for (int i = 0; i < requirement.Tables.Count; i++)
                {
                    var table = requirement.Tables[i];
                    sb.AppendLine($"*Table {i + 1}: {table.EditableTitle ?? "Untitled Table"}*");
                    
                    if (table.Table != null && table.Table.Any())
                    {
                        // Format table with markdown
                        bool hasHeader = table.FirstRowLooksLikeHeader && table.Table.Count > 1;
                        
                        for (int rowIdx = 0; rowIdx < table.Table.Count; rowIdx++)
                        {
                            var row = table.Table[rowIdx] ?? new List<string>();
                            sb.AppendLine("| " + string.Join(" | ", row) + " |");
                            
                            // Add separator after header row
                            if (hasHeader && rowIdx == 0)
                            {
                                var separatorCells = row.Select(_ => "---");
                                sb.AppendLine("| " + string.Join(" | ", separatorCells) + " |");
                            }
                        }
                    }
                    else
                    {
                        sb.AppendLine("*(empty table)*");
                    }
                    sb.AppendLine();
                }
            }

            // Add supplemental content if present
            if (requirement.LooseContent != null)
            {
                bool hasContent = false;
                var looseContent = requirement.LooseContent;

                // Check for paragraphs
                if (looseContent.Paragraphs != null && looseContent.Paragraphs.Any())
                {
                    if (!hasContent)
                    {
                        sb.AppendLine("**Supplemental Content:**");
                        sb.AppendLine();
                        hasContent = true;
                    }
                    
                    sb.AppendLine("*Additional Paragraphs:*");
                    foreach (var para in looseContent.Paragraphs)
                    {
                        if (!string.IsNullOrWhiteSpace(para))
                        {
                            sb.AppendLine($"- {para.Trim()}");
                        }
                    }
                    sb.AppendLine();
                }

                // Check for loose tables
                if (looseContent.Tables != null && looseContent.Tables.Any())
                {
                    if (!hasContent)
                    {
                        sb.AppendLine("**Supplemental Content:**");
                        sb.AppendLine();
                        hasContent = true;
                    }
                    
                    sb.AppendLine("*Additional Tables:*");
                    sb.AppendLine();
                    
                    for (int i = 0; i < looseContent.Tables.Count; i++)
                    {
                        var looseTable = looseContent.Tables[i];
                        sb.AppendLine($"*Supplemental Table {i + 1}:*");
                        
                        if (looseTable.Rows != null && looseTable.Rows.Any())
                        {
                            bool hasHeaders = looseTable.ColumnHeaders != null && looseTable.ColumnHeaders.Any();
                            
                            // Display headers if present
                            if (hasHeaders)
                            {
                                var headers = looseTable.ColumnHeaders ?? new List<string>();
                                sb.AppendLine("| " + string.Join(" | ", headers) + " |");
                                var separatorCells = headers.Select(_ => "---");
                                sb.AppendLine("| " + string.Join(" | ", separatorCells) + " |");
                            }
                            
                            // Display data rows
                            foreach (var row in looseTable.Rows)
                            {
                                sb.AppendLine("| " + string.Join(" | ", row ?? new List<string>()) + " |");
                            }
                        }
                        else
                        {
                            sb.AppendLine("*(empty table)*");
                        }
                        sb.AppendLine();
                    }
                }
            }

            if (includeAnalysisRequest)
            {
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine("Please analyze this requirement using our established template with:");
                sb.AppendLine("- Problem identification with categories and tags");
                sb.AppendLine("- Improved requirement variants (minimal, strong, atomic split if needed)");
                sb.AppendLine("- Clear explanations of changes made");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Export multiple requirements formatted for batch ChatGPT analysis.
        /// </summary>
        /// <param name="requirements">The requirements to export</param>
        /// <param name="includeAnalysisRequest">Whether to include analysis request text</param>
        /// <returns>Formatted text ready for ChatGPT</returns>
        public string ExportMultipleRequirements(IEnumerable<Requirement> requirements, bool includeAnalysisRequest = true)
        {
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));

            var requirementList = requirements.ToList();
            if (!requirementList.Any())
                return "No requirements selected for export.";

            var sb = new StringBuilder();

            if (includeAnalysisRequest)
            {
                sb.AppendLine("## Batch Requirement Analysis Request");
                sb.AppendLine();
                sb.AppendLine($"Please analyze the following {requirementList.Count} requirements using our established template.");
                sb.AppendLine("For each requirement, provide:");
                sb.AppendLine("- Problem identification with categories and tags");
                sb.AppendLine("- Improved requirement variants (minimal, strong, atomic split if needed)");
                sb.AppendLine("- Clear explanations of changes made");
                sb.AppendLine();
                sb.AppendLine("=".PadRight(60, '='));
                sb.AppendLine();
            }

            for (int i = 0; i < requirementList.Count; i++)
            {
                if (i > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("=".PadRight(60, '='));
                    sb.AppendLine();
                }
                
                sb.AppendLine($"### Requirement {i + 1} of {requirementList.Count}");
                sb.AppendLine();
                
                // Export individual requirement without the analysis request (we added it at the top)
                sb.AppendLine(ExportSingleRequirement(requirementList[i], false));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Copy formatted requirement text to the system clipboard.
        /// </summary>
        /// <param name="formattedText">The formatted text to copy</param>
        /// <returns>True if successful, false if clipboard operation failed</returns>
        public bool CopyToClipboard(string formattedText)
        {
            try
            {
                // Ensure clipboard operation runs on UI thread
                bool success = false;
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    try
                    {
                        Clipboard.SetText(formattedText);
                        success = true;
                    }
                    catch (Exception innerEx)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[ChatGptExportService] Clipboard.SetText failed: {innerEx.Message}");
                    }
                });
                return success;
            }
            catch (Exception ex)
            {
                // Log the error but don't throw - clipboard operations can fail for various reasons
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ChatGptExportService] Failed to copy to clipboard: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Export a single requirement and copy to clipboard.
        /// </summary>
        /// <param name="requirement">The requirement to export</param>
        /// <param name="includeAnalysisRequest">Whether to include analysis request text</param>
        /// <returns>True if successful</returns>
        public bool ExportAndCopy(Requirement requirement, bool includeAnalysisRequest = true)
        {
            try
            {
                var formatted = ExportSingleRequirement(requirement, includeAnalysisRequest);
                return CopyToClipboard(formatted);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ChatGptExportService] Failed to export requirement {requirement?.Item}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Export multiple requirements and copy to clipboard.
        /// </summary>
        /// <param name="requirements">The requirements to export</param>
        /// <param name="includeAnalysisRequest">Whether to include analysis request text</param>
        /// <returns>True if successful</returns>
        public bool ExportAndCopyMultiple(IEnumerable<Requirement> requirements, bool includeAnalysisRequest = true)
        {
            try
            {
                var formatted = ExportMultipleRequirements(requirements, includeAnalysisRequest);
                return CopyToClipboard(formatted);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ChatGptExportService] Failed to export multiple requirements: {ex.Message}");
                return false;
            }
        }
    }
}