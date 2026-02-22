using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Smart requirement importer that automatically detects document format and chooses the best import strategy
    /// </summary>
    public class SmartRequirementImporter
    {
        private readonly IRequirementService _requirementService;
        private readonly ILogger<SmartRequirementImporter> _logger;

        public SmartRequirementImporter(IRequirementService requirementService, ILogger<SmartRequirementImporter> logger)
        {
            _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public class ImportResult
        {
            public bool Success { get; set; }
            public List<Requirement> Requirements { get; set; } = new();
            public string ImportMethod { get; set; } = string.Empty;
            public DocumentFormatDetector.DetectionResult? FormatAnalysis { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
            public string UserMessage { get; set; } = string.Empty;
            public TimeSpan ImportDuration { get; set; }
        }

        /// <summary>
        /// Smart import that analyzes the document and chooses the best import strategy
        /// </summary>
        public async Task<ImportResult> ImportRequirementsAsync(string filePath)
        {
            var startTime = DateTime.Now;
            var result = new ImportResult();

            try
            {
                _logger.LogInformation("Starting smart import analysis for: {FilePath}", filePath);
                
                // Step 1: Analyze document format
                result.FormatAnalysis = DocumentFormatDetector.AnalyzeDocument(filePath);
                _logger.LogInformation("Document analysis complete: Format={Format}, HasRequirements={HasRequirements}", 
                    result.FormatAnalysis.Format, result.FormatAnalysis.HasRequirements);

                // Step 2: Choose import strategy based on analysis
                result.Requirements = await ImportWithOptimalStrategy(filePath, result.FormatAnalysis);
                result.ImportMethod = GetImportMethodName(result.FormatAnalysis.Format);
                
                // Step 3: Validate results
                if (result.Requirements.Count > 0)
                {
                    result.Success = true;
                    result.UserMessage = $"✅ Successfully imported {result.Requirements.Count} requirements using {result.ImportMethod}";
                    
                    _logger.LogInformation("Import successful: {Count} requirements imported using {Method}", 
                        result.Requirements.Count, result.ImportMethod);
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "No requirements found in the document";
                    result.UserMessage = GetNoRequirementsMessage(result.FormatAnalysis);
                    
                    _logger.LogWarning("Import completed but no requirements found: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.UserMessage = $"❌ Import failed: {ex.Message}";
                
                _logger.LogError(ex, "Import failed for file: {FilePath}", filePath);
            }
            finally
            {
                result.ImportDuration = DateTime.Now - startTime;
                _logger.LogInformation("Import completed in {Duration:F2}s", result.ImportDuration.TotalSeconds);
            }

            return result;
        }

        private async Task<List<Requirement>> ImportWithOptimalStrategy(string filePath, DocumentFormatDetector.DetectionResult analysis)
        {
            return await Task.Run(() =>
            {
                List<Requirement> requirements;

                switch (analysis.Format)
                {
                    case DocumentFormatDetector.DocumentFormat.JamaAllDataExport:
                        _logger.LogInformation("Using Jama All Data parser (detected format)");
                        requirements = _requirementService.ImportRequirementsFromJamaAllDataDocx(filePath);
                        
                        // If Jama parser fails, fall back to Word parser
                        if (requirements.Count == 0)
                        {
                            _logger.LogInformation("Jama parser returned 0 results, trying Word parser as fallback");
                            requirements = _requirementService.ImportRequirementsFromWord(filePath);
                        }
                        break;

                    case DocumentFormatDetector.DocumentFormat.GeneralWordDocument:
                    case DocumentFormatDetector.DocumentFormat.RequirementsTable:
                        _logger.LogInformation("Using Word parser (detected format)");
                        requirements = _requirementService.ImportRequirementsFromWord(filePath);
                        break;

                    case DocumentFormatDetector.DocumentFormat.UnknownFormat:
                    default:
                        // Try both parsers when format is unknown
                        _logger.LogInformation("Unknown format detected, trying both parsers");
                        
                        // Try Jama first (more specific)
                        requirements = _requirementService.ImportRequirementsFromJamaAllDataDocx(filePath);
                        if (requirements.Count == 0)
                        {
                            _logger.LogInformation("Jama parser returned 0 results, trying Word parser");
                            requirements = _requirementService.ImportRequirementsFromWord(filePath);
                        }
                        break;
                }

                return requirements ?? new List<Requirement>();
            });
        }

        private static string GetImportMethodName(DocumentFormatDetector.DocumentFormat format)
        {
            return format switch
            {
                DocumentFormatDetector.DocumentFormat.JamaAllDataExport => "Jama All Data Parser",
                DocumentFormatDetector.DocumentFormat.GeneralWordDocument => "Word Document Parser",
                DocumentFormatDetector.DocumentFormat.RequirementsTable => "Word Document Parser",
                _ => "Auto-Detection (Both Parsers)"
            };
        }

        private static string GetNoRequirementsMessage(DocumentFormatDetector.DetectionResult analysis)
        {
            if (analysis.Format == DocumentFormatDetector.DocumentFormat.UnknownFormat)
            {
                return analysis.UserGuidance;
            }

            return $"❓ The document format was recognized ({analysis.Description}) but no requirements were found.\n\n" +
                   $"{analysis.UserGuidance}\n\n" +
                   "💡 **Troubleshooting tips:**\n" +
                   "• Check that requirements have proper IDs (e.g., PROJ-REQ_RC-001)\n" +
                   "• Verify the document isn't corrupted\n" +
                   "• For Jama exports, ensure you used 'All Data' export format";
        }
    }
}