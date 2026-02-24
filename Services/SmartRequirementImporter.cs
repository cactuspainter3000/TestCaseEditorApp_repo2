using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using Microsoft.Extensions.Logging;
using DocumentFormat.OpenXml.Packaging;
using System.IO;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Import mode for SmartRequirementImporter
    /// </summary>
    public enum ImportMode
    {
        /// <summary>
        /// Standard requirement import from document
        /// </summary>
        StandardImport,
        /// <summary>
        /// ATP derivation mode - derive system capabilities from test procedures
        /// </summary>
        ATPDerivation
    }

    /// <summary>
    /// Smart requirement importer that automatically detects document format and chooses the best import strategy
    /// Supports both standard import and ATP (Acceptance Test Procedure) derivation modes
    /// </summary>
    public class SmartRequirementImporter
    {
        private readonly IRequirementService _requirementService;
        private readonly ISystemCapabilityDerivationService _derivationService;
        private readonly ATPStepParser _atpParser;
        private readonly ILogger<SmartRequirementImporter> _logger;

        public SmartRequirementImporter(
            IRequirementService requirementService,
            ISystemCapabilityDerivationService derivationService,
            ATPStepParser atpParser,
            ILogger<SmartRequirementImporter> logger)
        {
            _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _derivationService = derivationService ?? throw new ArgumentNullException(nameof(derivationService));
            _atpParser = atpParser ?? throw new ArgumentNullException(nameof(atpParser));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public class ImportResult
        {
            public bool Success { get; set; }
            public List<Requirement> Requirements { get; set; } = new();
            public string ImportMethod { get; set; } = string.Empty;
            public ImportMode Mode { get; set; } = ImportMode.StandardImport;
            public DocumentFormatDetector.DetectionResult? FormatAnalysis { get; set; }
            public DerivationResult? ATPDerivationResult { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
            public string UserMessage { get; set; } = string.Empty;
            public TimeSpan ImportDuration { get; set; }
        }

        /// <summary>
        /// Smart import that analyzes the document and chooses the best import strategy
        /// </summary>
        /// <param name="filePath">Path to the document to import</param>
        /// <param name="mode">Import mode (StandardImport or ATPDerivation)</param>
        public async Task<ImportResult> ImportRequirementsAsync(string filePath, ImportMode mode = ImportMode.StandardImport)
        {
            var startTime = DateTime.Now;
            var result = new ImportResult { Mode = mode };

            try
            {
                _logger.LogInformation("Starting {Mode} import analysis for: {FilePath}", mode, filePath);

                if (mode == ImportMode.ATPDerivation)
                {
                    return await ImportWithATPDerivationAsync(filePath, result, startTime);
                }
                else
                {
                    return await ImportWithStandardModeAsync(filePath, result, startTime);
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

        /// <summary>
        /// Legacy method for backward compatibility - uses standard import mode
        /// </summary>
        public async Task<ImportResult> ImportRequirementsAsync(string filePath)
        {
            return await ImportRequirementsAsync(filePath, ImportMode.StandardImport);
        }

        private async Task<ImportResult> ImportWithStandardModeAsync(string filePath, ImportResult result, DateTime startTime)
        {
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

            return result;
        }

        private async Task<ImportResult> ImportWithATPDerivationAsync(string filePath, ImportResult result, DateTime startTime)
        {
            try
            {
                // Step 1: Read ATP document content
                string atpContent;
                if (filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract text from DOCX for ATP parsing
                    atpContent = ExtractTextFromDocx(filePath);
                }
                else
                {
                    atpContent = await File.ReadAllTextAsync(filePath);
                }

                if (string.IsNullOrWhiteSpace(atpContent))
                {
                    result.Success = false;
                    result.ErrorMessage = "Document appears to be empty or unreadable";
                    result.UserMessage = "❌ Could not extract content from ATP document";
                    return result;
                }

                _logger.LogInformation("Extracted ATP content: {Length} characters", atpContent.Length);

                // Step 2: Derive capabilities from ATP content
                result.ATPDerivationResult = await _derivationService.DeriveCapabilitiesAsync(atpContent);
                result.ImportMethod = "ATP Capability Derivation";

                // Step 3: Convert derived capabilities to requirements
                result.Requirements = ConvertCapabilitiesToRequirements(result.ATPDerivationResult.DerivedCapabilities, filePath);

                // Step 4: Validate results
                if (result.Requirements.Count > 0)
                {
                    result.Success = true;
                    var derivedCount = result.Requirements.Count;
                    var rejectedCount = result.ATPDerivationResult.RejectedItems?.Count ?? 0;
                    result.UserMessage = $"✅ Successfully derived {derivedCount} system requirements from ATP content.\n" +
                                       $"📋 Analyzed {result.ATPDerivationResult.DerivedCapabilities.Count} capabilities, {rejectedCount} items filtered out.";
                    
                    _logger.LogInformation("ATP derivation successful: {DerivedCount} requirements from {CapabilityCount} capabilities", 
                        derivedCount, result.ATPDerivationResult.DerivedCapabilities.Count);
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "No system capabilities could be derived from ATP content";
                    result.UserMessage = "❌ ATP analysis completed but no derivable system requirements were found.\n" +
                                       "💡 The document may contain only procedural steps without system-level requirements.";
                    
                    _logger.LogWarning("ATP derivation completed but no requirements derived: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"ATP derivation failed: {ex.Message}";
                result.UserMessage = $"❌ ATP derivation failed: {ex.Message}";
                _logger.LogError(ex, "ATP derivation failed for file: {FilePath}", filePath);
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

        /// <summary>
        /// Extract plain text content from DOCX file for ATP parsing
        /// </summary>
        private string ExtractTextFromDocx(string filePath)
        {
            try
            {
                using (var doc = WordprocessingDocument.Open(filePath, false))
                {
                    var body = doc.MainDocumentPart?.Document.Body;
                    return body?.InnerText ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text from DOCX: {FilePath}", filePath);
                return string.Empty;
            }
        }

        /// <summary>
        /// Convert derived capabilities to Requirement objects for the application
        /// </summary>
        private List<Requirement> ConvertCapabilitiesToRequirements(List<DerivedCapability> capabilities, string sourceFile)
        {
            var requirements = new List<Requirement>();
            var sourceFileName = System.IO.Path.GetFileName(sourceFile);

            for (int i = 0; i < capabilities.Count; i++)
            {
                var capability = capabilities[i];
                var requirement = new Requirement
                {
                    GlobalId = $"ATP-{i + 1:D3}", // ATP-001, ATP-002, etc.
                    Name = GenerateRequirementName(capability.RequirementText, capability.TaxonomyCategory),
                    Description = capability.RequirementText,
                    RequirementType = $"{capability.TaxonomyCategory} - {capability.TaxonomySubcategory}",
                    
                    // ATP-specific fields 
                    Rationale = BuildRequirementNotes(capability),
                    
                    // Standard fields
                    Heading = "Draft",
                    ItemType = "System Requirement",
                    CreatedDate = DateTime.Now,
                    ModifiedDate = DateTime.Now
                };

                requirements.Add(requirement);
            }

            _logger.LogInformation("Converted {CapabilityCount} capabilities to {RequirementCount} requirements", 
                capabilities.Count, requirements.Count);

            return requirements;
        }

        /// <summary>
        /// Generate a concise requirement name from capability text
        /// </summary>
        private string GenerateRequirementName(string requirementText, string taxonomyCategory)
        {
            // Take first 50 characters and clean up
            var name = requirementText.Length > 50 ? requirementText.Substring(0, 50) + "..." : requirementText;
            
            // Remove line breaks and extra spaces
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();
            
            // Prefix with taxonomy category
            return $"[{taxonomyCategory}] {name}";
        }

        /// <summary>
        /// Map capability priority to requirement priority
        /// </summary>
        private string MapPriorityFromCapability(DerivedCapability capability)
        {
            return capability.Priority switch
            {
                "High" => "High",
                "Medium" => "Medium", 
                "Low" => "Low",
                _ => "Medium" // Default
            };
        }

        /// <summary>
        /// Build comprehensive notes from capability metadata
        /// </summary>
        private string BuildRequirementNotes(DerivedCapability capability)
        {
            var notes = new List<string>();
            
            // Add derivation info
            notes.Add($"**Derived from ATP Step:** {capability.SourceATPStep}");
            notes.Add($"**Taxonomy:** {capability.TaxonomyCategory} - {capability.TaxonomySubcategory}");
            
            if (!string.IsNullOrEmpty(capability.DerivationRationale))
            {
                notes.Add($"**Derivation Rationale:** {capability.DerivationRationale}");
            }

            if (capability.MissingSpecifications?.Count > 0)
            {
                notes.Add($"**Missing Specifications:** {string.Join(", ", capability.MissingSpecifications)}");
            }

            if (capability.AllocationTargets?.Count > 0)
            {
                notes.Add($"**Allocation Targets:** {string.Join(", ", capability.AllocationTargets)}");
            }

            return string.Join("\n\n", notes);
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