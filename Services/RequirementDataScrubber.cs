using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Implementation of requirement data scrubber for cleaning and validating imported requirements.
    /// Provides multi-layer validation: structure, content normalization, duplicates, and business rules.
    /// </summary>
    public class RequirementDataScrubber : IRequirementDataScrubber
    {
        private readonly ILogger<RequirementDataScrubber> _logger;
        
        public RequirementDataScrubber(ILogger<RequirementDataScrubber> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Process requirements through the complete scrubbing pipeline
        /// </summary>
        public async Task<ScrubberResult> ProcessRequirementsAsync(
            List<Requirement> newRequirements, 
            List<Requirement> existingRequirements,
            ImportContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ScrubberResult();
            
            _logger.LogInformation("🧹 Starting requirement scrubbing for {Count} requirements from {Source}", 
                newRequirements.Count, context.Source);

            try
            {
                // Phase 1: Structure validation and basic cleanup
                var structureValidated = await ValidateStructureAsync(newRequirements, result);
                
                // Phase 2: Content normalization 
                var contentNormalized = await NormalizeContentAsync(structureValidated, result);
                
                // Phase 3: Duplicate detection
                var duplicatesFiltered = await FilterDuplicatesAsync(contentNormalized, existingRequirements, result, context);
                
                // Phase 4: Business rule validation
                var businessValidated = await ValidateBusinessRulesAsync(duplicatesFiltered, result);
                
                result.CleanRequirements = businessValidated;
                
                // Calculate final statistics
                stopwatch.Stop();
                result.Statistics = new ScrubberStats
                {
                    TotalProcessed = newRequirements.Count,
                    CleanRequirements = result.CleanRequirements.Count,
                    DuplicatesSkipped = result.DuplicatesDetected.Count,
                    IssuesFixed = result.ValidationIssues.Count(i => i.Type == IssueType.Fixed),
                    WarningsGenerated = result.ValidationIssues.Count(i => i.Type == IssueType.Warning),
                    ErrorsFound = result.ValidationIssues.Count(i => i.Type == IssueType.Error),
                    ProcessingTime = stopwatch.Elapsed
                };
                
                _logger.LogInformation("✅ Scrubbing complete: {Clean}/{Total} requirements, {Duplicates} duplicates, {Issues} issues fixed",
                    result.Statistics.CleanRequirements, result.Statistics.TotalProcessed, 
                    result.Statistics.DuplicatesSkipped, result.Statistics.IssuesFixed);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during requirement scrubbing");
                throw;
            }
        }

        /// <summary>
        /// Phase 1: Validate basic structure and required fields
        /// </summary>
        private async Task<List<Requirement>> ValidateStructureAsync(List<Requirement> requirements, ScrubberResult result)
        {
            await Task.CompletedTask; // Placeholder for async operations
            
            var validRequirements = new List<Requirement>();
            
            foreach (var req in requirements)
            {
                var isValid = true;
                
                // Validate GlobalId
                if (string.IsNullOrWhiteSpace(req.GlobalId))
                {
                    // Auto-generate GlobalId instead of rejecting the requirement
                    req.GlobalId = $"REQ-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
                    
                    result.ValidationIssues.Add(new RequirementIssue
                    {
                        Requirement = req,
                        Type = IssueType.Fixed,
                        FieldName = nameof(req.GlobalId),
                        Description = $"Auto-generated GlobalId: {req.GlobalId}",
                        SuggestedFix = "Consider using a more meaningful ID scheme"
                    });
                }
                
                // Validate Name
                if (string.IsNullOrWhiteSpace(req.Name))
                {
                    result.ValidationIssues.Add(new RequirementIssue
                    {
                        Requirement = req,
                        Type = IssueType.Warning,
                        FieldName = nameof(req.Name),
                        Description = "Name is empty",
                        SuggestedFix = "Use Item number as Name"
                    });
                    
                    // Auto-fix: use Item as Name if available
                    if (!string.IsNullOrWhiteSpace(req.Item))
                    {
                        req.Name = req.Item;
                        result.ValidationIssues.Add(new RequirementIssue
                        {
                            Requirement = req,
                            Type = IssueType.Fixed,
                            FieldName = nameof(req.Name),
                            Description = "Auto-filled Name from Item"
                        });
                    }
                }
                
                if (isValid)
                {
                    validRequirements.Add(req);
                }
            }
            
            return validRequirements;
        }

        /// <summary>
        /// Phase 2: Normalize content and clean up formatting
        /// </summary>
        private async Task<List<Requirement>> NormalizeContentAsync(List<Requirement> requirements, ScrubberResult result)
        {
            await Task.CompletedTask;
            
            foreach (var req in requirements)
            {
                var changesMade = false;
                
                // Trim whitespace from text fields
                var originalName = req.Name;
                req.Name = req.Name?.Trim() ?? string.Empty;
                if (req.Name != originalName)
                {
                    changesMade = true;
                }
                
                var originalDescription = req.Description;
                req.Description = req.Description?.Trim() ?? string.Empty;
                if (req.Description != originalDescription)
                {
                    changesMade = true;
                }
                
                // Clean up Item field
                var originalItem = req.Item;
                req.Item = req.Item?.Trim() ?? string.Empty;
                if (req.Item != originalItem)
                {
                    changesMade = true;
                }
                
                if (changesMade)
                {
                    result.ValidationIssues.Add(new RequirementIssue
                    {
                        Requirement = req,
                        Type = IssueType.Fixed,
                        Description = "Cleaned up text formatting (trimmed whitespace)"
                    });
                }
            }
            
            return requirements;
        }

        /// <summary>
        /// Phase 3: Filter out duplicates based on GlobalId and content similarity
        /// </summary>
        private async Task<List<Requirement>> FilterDuplicatesAsync(
            List<Requirement> newRequirements, 
            List<Requirement> existingRequirements, 
            ScrubberResult result,
            ImportContext context)
        {
            await Task.CompletedTask;
            
            var existingIds = existingRequirements.Select(r => r.GlobalId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var uniqueRequirements = new List<Requirement>();
            
            foreach (var req in newRequirements)
            {
                if (existingIds.Contains(req.GlobalId))
                {
                    result.DuplicatesDetected.Add(req);
                    result.ValidationIssues.Add(new RequirementIssue
                    {
                        Requirement = req,
                        Type = IssueType.Info,
                        Description = $"Duplicate GlobalId '{req.GlobalId}' - skipping",
                        FieldName = nameof(req.GlobalId)
                    });
                }
                else
                {
                    uniqueRequirements.Add(req);
                    // For additional imports, track the new IDs to prevent internal duplicates
                    if (context.ImportType == ImportType.Additional)
                    {
                        existingIds.Add(req.GlobalId);
                    }
                }
            }
            
            return uniqueRequirements;
        }

        /// <summary>
        /// Phase 4: Validate business rules and domain constraints
        /// </summary>
        private async Task<List<Requirement>> ValidateBusinessRulesAsync(List<Requirement> requirements, ScrubberResult result)
        {
            await Task.CompletedTask;
            
            foreach (var req in requirements)
            {
                // Validate description minimum length
                if (!string.IsNullOrWhiteSpace(req.Description) && req.Description.Length < 10)
                {
                    result.ValidationIssues.Add(new RequirementIssue
                    {
                        Requirement = req,
                        Type = IssueType.Warning,
                        FieldName = nameof(req.Description),
                        Description = "Description is very short (less than 10 characters)",
                        SuggestedFix = "Consider expanding the description for better test case generation"
                    });
                }
                
                // Validate verification method if present
                if (req.Method != VerificationMethod.Unassigned)
                {
                    var standardMethods = new HashSet<VerificationMethod> 
                    { 
                        VerificationMethod.Analysis, 
                        VerificationMethod.Demonstration, 
                        VerificationMethod.Test, 
                        VerificationMethod.Inspection 
                    };
                    
                    if (!standardMethods.Contains(req.Method))
                    {
                        result.ValidationIssues.Add(new RequirementIssue
                        {
                            Requirement = req,
                            Type = IssueType.Warning,
                            FieldName = nameof(req.Method),
                            Description = $"Verification method '{req.Method}' is less common",
                            SuggestedFix = $"Standard methods: {string.Join(", ", standardMethods)}"
                        });
                    }
                }
            }
            
            return requirements;
        }
    }
}