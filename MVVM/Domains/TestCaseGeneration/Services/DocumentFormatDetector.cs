using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services
{
    /// <summary>
    /// Detects document format and provides guidance for proper import methods
    /// </summary>
    public static class DocumentFormatDetector
    {
        public enum DocumentFormat
        {
            JamaAllDataExport,
            GeneralWordDocument,
            RequirementsTable,
            UnknownFormat
        }

        public class DetectionResult
        {
            public DocumentFormat Format { get; set; }
            public string Description { get; set; } = string.Empty;
            public string RecommendedImportMethod { get; set; } = string.Empty;
            public string UserGuidance { get; set; } = string.Empty;
            public bool HasRequirements { get; set; }
            public int EstimatedRequirementCount { get; set; }
            public List<string> FoundRequirementIds { get; set; } = new();
            public List<string> DetectionReasons { get; set; } = new();
        }

        /// <summary>
        /// Analyzes a Word document and provides guidance on the best import approach
        /// </summary>
        public static DetectionResult AnalyzeDocument(string filePath)
        {
            var result = new DetectionResult();
            
            try
            {
                if (!File.Exists(filePath))
                {
                    result.Description = "File not found";
                    result.UserGuidance = "Please check that the file path is correct and the file exists.";
                    return result;
                }

                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                if (extension != ".docx")
                {
                    result.Description = $"Unsupported file format: {extension}";
                    result.UserGuidance = "Please save or export your document as a Word .docx file.";
                    return result;
                }

                return AnalyzeWordDocument(filePath);
            }
            catch (Exception ex)
            {
                result.Description = $"Error analyzing document: {ex.Message}";
                result.UserGuidance = "There was an error reading the document. Please ensure it's not corrupted and not open in another application.";
                return result;
            }
        }

        private static DetectionResult AnalyzeWordDocument(string filePath)
        {
            var result = new DetectionResult();
            
            using var doc = WordprocessingDocument.Open(filePath, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null)
            {
                result.Description = "Empty or corrupted document";
                result.UserGuidance = "The document appears to be empty or corrupted.";
                return result;
            }

            // Check for Jama All Data export format
            var jamaIndicators = CheckForJamaFormat(body);
            
            // Look for requirement IDs
            var requirementIds = FindRequirementIds(body);
            result.FoundRequirementIds = requirementIds;
            result.EstimatedRequirementCount = requirementIds.Count;
            result.HasRequirements = requirementIds.Count > 0;

            // Determine format based on analysis
            if (jamaIndicators.IsJamaFormat)
            {
                result.Format = DocumentFormat.JamaAllDataExport;
                result.Description = "Jama 'All Data' export document";
                result.RecommendedImportMethod = "ImportRequirementsFromJamaAllDataDocx";
                result.UserGuidance = "✅ This appears to be a proper Jama 'All Data' export. Use the 'Import from Jama' option.";
                result.DetectionReasons.AddRange(jamaIndicators.Reasons);
            }
            else if (result.HasRequirements)
            {
                result.Format = DocumentFormat.GeneralWordDocument;
                result.Description = "Word document with requirements";
                result.RecommendedImportMethod = "ImportRequirementsFromWord";
                result.UserGuidance = $"📄 Found {requirementIds.Count} requirement ID(s) in a general Word document. Use the 'Import from Word' option.";
                result.DetectionReasons.Add($"Found requirement IDs: {string.Join(", ", requirementIds.Take(3))}{(requirementIds.Count > 3 ? "..." : "")}");
            }
            else
            {
                result.Format = DocumentFormat.UnknownFormat;
                result.Description = "Document format not recognized";
                result.UserGuidance = GetFormatGuidance();
                result.DetectionReasons.Add("No recognizable requirement patterns found");
            }

            return result;
        }

        private static (bool IsJamaFormat, List<string> Reasons) CheckForJamaFormat(Body body)
        {
            var reasons = new List<string>();
            var jamaKeywords = new[] 
            {
                "Item ID", "Global ID", "Requirement Description", "Validation Method/s",
                "Verification Method/s", "# of Downstream Relationships", "Upstream Cross Instance Relationships"
            };

            var foundKeywords = new List<string>();
            var tables = body.Descendants<Table>().ToList();
            
            foreach (var table in tables)
            {
                foreach (var cell in table.Descendants<TableCell>())
                {
                    var cellText = cell.InnerText?.Trim() ?? "";
                    foreach (var keyword in jamaKeywords)
                    {
                        if (cellText.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            foundKeywords.Add(keyword);
                        }
                    }
                }
            }

            var uniqueKeywords = foundKeywords.Distinct().ToList();
            var isJamaFormat = uniqueKeywords.Count >= 3; // Need at least 3 Jama-specific fields
            
            if (isJamaFormat)
            {
                reasons.Add($"Found {uniqueKeywords.Count} Jama-specific field names");
                reasons.Add($"Jama fields detected: {string.Join(", ", uniqueKeywords.Take(5))}");
                
                // Check for 2-column table structure
                var twoColumnTables = tables.Count(t => 
                    t.Descendants<TableRow>().Any(row => 
                        row.Descendants<TableCell>().Count() == 2));
                
                if (twoColumnTables > 0)
                {
                    reasons.Add($"Found {twoColumnTables} two-column tables (typical of Jama exports)");
                }
            }
            else
            {
                reasons.Add($"Only found {uniqueKeywords.Count} Jama-specific fields (need at least 3)");
            }

            return (isJamaFormat, reasons);
        }

        private static List<string> FindRequirementIds(Body body)
        {
            var requirementIds = new HashSet<string>();
            var patterns = new[]
            {
                @"\b([A-Z0-9_-]+-REQ_RC-\d+)\b",  // DECAGON-REQ_RC-11
                @"\b([A-Z]+\d*-REQ-\d+)\b",        // ABC-REQ-123
                @"\b(REQ[_-]\d+)\b",               // REQ_001, REQ-001
                @"\b([A-Z]+[_-]\d+)\b"             // FUNC_001
            };

            var allText = body.InnerText ?? "";
            
            foreach (var pattern in patterns)
            {
                var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var matches = regex.Matches(allText);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    requirementIds.Add(match.Groups[1].Value);
                }
            }

            return requirementIds.OrderBy(x => x).ToList();
        }

        private static string GetFormatGuidance()
        {
            return @"❓ Unable to detect document format. Here's how to get your requirements imported:

📋 **For Jama users:**
1. In Jama, go to your project/set
2. Select 'Export' → 'All Data'
3. Choose 'Word (.docx)' format
4. Use 'Import from Jama' in this app

📄 **For Word documents:**
Ensure your document contains requirement IDs like:
• PROJ-REQ_RC-001
• ABC-REQ-123
• REQ_001
Then use 'Import from Word'

🔧 **Need help?**
Contact support with a sample of your document format.";
        }
    }
}