using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
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
            ATPDocument,           // NEW: Document containing ATP sections
            JamaWithATP,          // NEW: Jama export that also contains ATP content
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
            
            // NEW: ATP Detection Properties
            public bool HasATPContent { get; set; }
            public int EstimatedATPSteps { get; set; }
            public List<string> ATPIndicators { get; set; } = new();
            public string ATPGuidance { get; set; } = string.Empty;
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
            
            // NEW: Check for ATP content
            var atpIndicators = CheckForATPContent(body);
            result.HasATPContent = atpIndicators.HasATP;
            result.EstimatedATPSteps = atpIndicators.EstimatedSteps;
            result.ATPIndicators = atpIndicators.Indicators;
            result.ATPGuidance = atpIndicators.Guidance;
            
            // Look for requirement IDs
            var requirementIds = FindRequirementIds(body);
            result.FoundRequirementIds = requirementIds;
            result.EstimatedRequirementCount = requirementIds.Count;
            result.HasRequirements = requirementIds.Count > 0;

            // Determine format based on analysis
            if (jamaIndicators.IsJamaFormat && result.HasATPContent)
            {
                result.Format = DocumentFormat.JamaWithATP;
                result.Description = "Jama 'All Data' export with ATP content";
                result.RecommendedImportMethod = "ImportRequirementsFromJamaAllDataDocx or ATP Derivation Mode";
                result.UserGuidance = $"✅ Jama export detected with {result.EstimatedATPSteps} ATP steps. " +
                                    "Use 'Import from Jama' for requirements OR 'ATP Derivation Mode' to derive system capabilities from test procedures.";
                result.DetectionReasons.AddRange(jamaIndicators.Reasons);
                result.DetectionReasons.AddRange(atpIndicators.Indicators);
            }
            else if (jamaIndicators.IsJamaFormat)
            {
                result.Format = DocumentFormat.JamaAllDataExport;
                result.Description = "Jama 'All Data' export document";
                result.RecommendedImportMethod = "ImportRequirementsFromJamaAllDataDocx";
                result.UserGuidance = "✅ This appears to be a proper Jama 'All Data' export. Use the 'Import from Jama' option.";
                result.DetectionReasons.AddRange(jamaIndicators.Reasons);
            }
            else if (result.HasATPContent)
            {
                result.Format = DocumentFormat.ATPDocument;
                result.Description = "Acceptance Test Procedure document";
                result.RecommendedImportMethod = "ATP Derivation Mode";
                result.UserGuidance = $"🧪 ATP document detected with {result.EstimatedATPSteps} test steps. " +
                                    "Use 'ATP Derivation Mode' to automatically derive system requirements from test procedures.";
                result.DetectionReasons.AddRange(atpIndicators.Indicators);
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

        /// <summary>
        /// Detects ATP (Acceptance Test Procedure) content in the document
        /// </summary>
        private static (bool HasATP, int EstimatedSteps, List<string> Indicators, string Guidance) CheckForATPContent(Body body)
        {
            var indicators = new List<string>();
            var allText = body.InnerText ?? "";
            var words = allText.ToLowerInvariant().Split();
            
            // ATP Keywords and Patterns
            var atpKeywords = new Dictionary<string, int>
            {
                // Direct ATP terminology
                { "acceptance test procedure", 15 },
                { "test procedure", 10 },
                { "atp", 8 },
                { "test plan", 8 },
                { "test method", 8 },
                
                // Test step indicators  
                { "step", 5 },
                { "procedure", 5 },
                { "verify", 4 },
                { "measure", 4 },
                { "apply", 3 },
                { "configure", 3 },
                { "calibrate", 4 },
                { "monitor", 3 },
                { "control", 3 },
                
                // Test execution terms
                { "pass", 2 },
                { "fail", 2 },
                { "criteria", 3 },
                { "expected", 2 },
                { "actual", 2 },
                { "tolerance", 4 },
                { "within", 2 },
                
                // Equipment/setup terms
                { "equipment", 3 },
                { "setup", 3 },
                { "initialization", 4 },
                { "calibration", 4 },
                { "power", 2 },
                { "signal", 2 },
                { "voltage", 3 },
                { "current", 3 }
            };
            
            // ATP Step Patterns
            var stepPatterns = new[]
            {
                @"\bstep\s+\d+",                    // "step 1", "step 2"
                @"\d+\.\s*[a-z]",                  // "1. verify", "2. measure"  
                @"\d+\.\d+\s*[a-z]",               // "3.1 apply", "3.2 check"
                @"^(verify|measure|apply|configure|calibrate|monitor|control|check|test)\s",  // Action verbs at start
                @"\b(shall|must|will)\s+(verify|measure|apply|configure|calibrate|monitor|control|check|test)", // Requirements with test verbs
                @"(pass|fail)\s+(criteria|condition|threshold)", // Pass/fail criteria
                @"(expected|actual)\s+(value|result|output)",     // Expected vs actual comparisons
            };
            
            int totalScore = 0;
            var foundKeywords = new HashSet<string>();
            
            // Score based on keyword frequency
            foreach (var (keyword, weight) in atpKeywords)
            {
                var count = CountOccurrences(allText.ToLowerInvariant(), keyword);
                if (count > 0)
                {
                    totalScore += count * weight;
                    foundKeywords.Add(keyword);
                }
            }
            
            // Additional scoring for step patterns
            int stepCount = 0;
            foreach (var pattern in stepPatterns)
            {
                var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
                var matches = regex.Matches(allText);
                stepCount += matches.Count;
                if (matches.Count > 0)
                {
                    totalScore += matches.Count * 3; // Bonus for structured step patterns
                }
            }
            
            // ATP detection logic
            bool hasATP = totalScore > 50 || (foundKeywords.Contains("acceptance test procedure")) || 
                         (foundKeywords.Contains("atp") && totalScore > 30) ||
                         (stepCount > 10 && foundKeywords.Count > 5);
            
            if (hasATP)
            {
                indicators.Add($"ATP score: {totalScore} (threshold: 50)");
                indicators.Add($"Found {foundKeywords.Count} ATP-related terms");
                indicators.Add($"Detected {stepCount} structured test steps");
                
                if (foundKeywords.Contains("acceptance test procedure"))
                    indicators.Add("Explicit 'Acceptance Test Procedure' reference");
                    
                if (foundKeywords.Contains("atp"))
                    indicators.Add("ATP acronym detected");
                    
                var topKeywords = foundKeywords.Take(5).ToList();
                if (topKeywords.Count > 0)
                    indicators.Add($"Key terms: {string.Join(", ", topKeywords)}");
            }
            
            var guidance = hasATP 
                ? $"🧪 Document contains structured test procedures that can be converted to system requirements. " +
                  $"Estimated {stepCount} test steps detected. Use ATP Derivation Mode to automatically extract system capabilities."
                : "";
            
            return (hasATP, stepCount, indicators, guidance);
        }
        
        /// <summary>
        /// Count occurrences of a keyword in text
        /// </summary>
        private static int CountOccurrences(string text, string keyword)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(keyword, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                count++;
                index += keyword.Length;
            }
            return count;
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