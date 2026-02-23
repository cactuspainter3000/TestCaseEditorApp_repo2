using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Parsed ATP step with extracted metadata for capability derivation
    /// </summary>
    public class ParsedATPStep
    {
        /// <summary>
        /// Unique identifier for this step within the document
        /// </summary>
        public string StepId { get; set; } = string.Empty;
        
        /// <summary>
        /// Step number (e.g., "3.2.1", "Step 5", "Procedure A")
        /// </summary>
        public string StepNumber { get; set; } = string.Empty;
        
        /// <summary>
        /// Full step text content
        /// </summary>
        public string StepText { get; set; } = string.Empty;
        
        /// <summary>
        /// Step type classification (Setup, Action, Verification, Cleanup, etc.)
        /// </summary>
        public ATPStepType StepType { get; set; } = ATPStepType.Action;
        
        /// <summary>
        /// Action verbs found in the step (measure, verify, apply, configure, etc.)
        /// </summary>
        public List<string> ActionVerbs { get; set; } = new List<string>();
        
        /// <summary>
        /// System components/interfaces mentioned in the step
        /// </summary>
        public List<string> SystemReferences { get; set; } = new List<string>();
        
        /// <summary>
        /// Measurement/verification keywords (tolerance, limits, criteria)
        /// </summary>
        public List<string> MeasurementKeywords { get; set; } = new List<string>();
        
        /// <summary>
        /// Safety/hazard keywords found
        /// </summary>
        public List<string> SafetyKeywords { get; set; } = new List<string>();
        
        /// <summary>
        /// Performance/timing requirements mentioned
        /// </summary>
        public List<string> PerformanceKeywords { get; set; } = new List<string>();
        
        /// <summary>
        /// Confidence in parsing accuracy (0.0 to 1.0)
        /// </summary>
        public double ParsingConfidence { get; set; } = 1.0;
        
        /// <summary>
        /// Original line number in source document
        /// </summary>
        public int LineNumber { get; set; } = 0;
        
        /// <summary>
        /// Any parsing warnings or issues
        /// </summary>
        public List<string> ParsingWarnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Classification of ATP step types for targeted capability derivation
    /// </summary>
    public enum ATPStepType
    {
        Setup,         // Initial conditions, equipment setup, calibration
        Action,        // Primary test actions, stimuli application
        Verification,  // Measurements, pass/fail criteria evaluation
        Cleanup,       // Return to safe state, equipment shutdown
        Safety,        // Safety checks, hazard mitigation
        Configuration, // System configuration, parameter setting
        Documentation, // Recording results, generating reports
        Branching,     // Conditional logic, decision points
        Unknown        // Could not classify
    }

    /// <summary>
    /// ATP document parsing configuration and options
    /// </summary>
    public class ATPParsingOptions
    {
        /// <summary>
        /// Minimum step text length to be considered valid
        /// </summary>
        public int MinimumStepLength { get; set; } = 10;
        
        /// <summary>
        /// Include substeps in parsing (e.g., 3.2.1.a, 3.2.1.b)
        /// </summary>
        public bool IncludeSubsteps { get; set; } = true;
        
        /// <summary>
        /// Parse step metadata (action verbs, system references, etc.)
        /// </summary>
        public bool ParseMetadata { get; set; } = true;
        
        /// <summary>
        /// System-specific keywords to look for (component names, interfaces)
        /// </summary>
        public List<string> SystemKeywords { get; set; } = new List<string>();
        
        /// <summary>
        /// Document format hint (Word, PDF, PlainText, HTML)
        /// </summary>
        public string DocumentFormat { get; set; } = "PlainText";
        
        /// <summary>
        /// Skip steps that appear to be boilerplate or non-functional
        /// </summary>
        public bool SkipBoilerplate { get; set; } = true;
    }

    /// <summary>
    /// Service for parsing ATP (Acceptance Test Procedure) documents into structured steps
    /// for capability derivation analysis. Handles various ATP formats and extracts metadata.
    /// </summary>
    public class ATPStepParser
    {
        private readonly ILogger<ATPStepParser> _logger;
        
        // Step number patterns (hierarchical numbering)
        private static readonly Regex StepNumberPattern = new Regex(
            @"^(?:Step\s+)?(\d+(?:\.\d+)*(?:\.[a-zA-Z])?)\s*[:.]?\s*", 
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        
        // Procedure/section patterns
        private static readonly Regex ProcedurePattern = new Regex(
            @"^(?:Procedure|Test|Section)\s+([A-Z0-9]+)\s*[:.]?\s*", 
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        
        // Action verbs that indicate system capabilities
        private static readonly string[] ActionVerbs = {
            "measure", "verify", "apply", "configure", "calibrate", "monitor", "control", 
            "generate", "transmit", "receive", "process", "analyze", "record", "display",
            "detect", "isolate", "protect", "enable", "disable", "activate", "deactivate",
            "initialize", "shutdown", "reset", "validate", "test", "check", "confirm",
            "set", "adjust", "maintain", "limit", "prevent", "ensure", "provide"
        };
        
        // System component keywords
        private static readonly string[] SystemKeywords = {
            "power", "voltage", "current", "signal", "interface", "bus", "controller", 
            "processor", "memory", "storage", "network", "communication", "sensor", 
            "actuator", "display", "indicator", "switch", "relay", "connector", "cable"
        };
        
        // Measurement/verification keywords
        private static readonly string[] MeasurementKeywords = {
            "tolerance", "accuracy", "precision", "range", "limit", "threshold", "criteria",
            "specification", "requirement", "within", "between", "exceeds", "below", "above",
            "nominal", "typical", "maximum", "minimum", "±", "percent", "%", "deviation"
        };
        
        // Safety-related keywords
        private static readonly string[] SafetyKeywords = {
            "hazard", "danger", "warning", "caution", "safety", "risk", "protection", 
            "isolation", "lockout", "tagout", "emergency", "alarm", "fault", "failure",
            "safe", "secure", "interlock", "barrier", "containment", "mitigation"
        };
        
        // Performance/timing keywords
        private static readonly string[] PerformanceKeywords = {
            "time", "duration", "delay", "timeout", "response", "latency", "throughput",
            "bandwidth", "frequency", "rate", "speed", "fast", "slow", "real-time",
            "seconds", "minutes", "milliseconds", "microseconds", "Hz", "kHz", "MHz", "GHz"
        };

        public ATPStepParser(ILogger<ATPStepParser> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Parse ATP document content into structured steps with metadata
        /// </summary>
        /// <param name="atpContent">Raw ATP document content</param>
        /// <param name="options">Parsing configuration options</param>
        /// <returns>List of parsed ATP steps with metadata</returns>
        public async Task<List<ParsedATPStep>> ParseATPDocumentAsync(string atpContent, ATPParsingOptions? options = null)
        {
            try
            {
                var parseOptions = options ?? new ATPParsingOptions();
                var steps = new List<ParsedATPStep>();
                
                _logger.LogDebug("Parsing ATP document (length: {ContentLength})", atpContent.Length);

                // Split into lines for processing
                var lines = atpContent.Split('\n', StringSplitOptions.None);
                
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    
                    // Skip empty lines and very short content
                    if (string.IsNullOrWhiteSpace(line) || line.Length < parseOptions.MinimumStepLength)
                        continue;
                    
                    // Try to parse as a step
                    var parsedStep = await ParseSingleLineAsync(line, i + 1, parseOptions);
                    if (parsedStep != null)
                    {
                        // Look ahead for continuation lines
                        var fullStepText = await ParseMultiLineStepAsync(lines, i, parseOptions);
                        if (!string.IsNullOrEmpty(fullStepText) && fullStepText != line)
                        {
                            parsedStep.StepText = fullStepText;
                            // Re-parse metadata with full text
                            if (parseOptions.ParseMetadata)
                            {
                                ParseStepMetadata(parsedStep, fullStepText, parseOptions);
                            }
                        }
                        
                        steps.Add(parsedStep);
                    }
                }

                // Filter out boilerplate if requested
                if (parseOptions.SkipBoilerplate)
                {
                    steps = FilterBoilerplateSteps(steps);
                }

                _logger.LogInformation("Parsed {StepCount} ATP steps from document", steps.Count);
                return steps;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse ATP document");
                return new List<ParsedATPStep>();
            }
        }

        /// <summary>
        /// Parse a single step text with full metadata extraction
        /// </summary>
        /// <param name="stepText">Step text to parse</param>
        /// <param name="options">Parsing options</param>
        /// <returns>Parsed step with metadata</returns>
        public async Task<ParsedATPStep?> ParseSingleStepAsync(string stepText, ATPParsingOptions? options = null)
        {
            try
            {
                var parseOptions = options ?? new ATPParsingOptions();
                var step = await ParseSingleLineAsync(stepText, 0, parseOptions);
                
                if (step != null && parseOptions.ParseMetadata)
                {
                    ParseStepMetadata(step, stepText, parseOptions);
                }
                
                return step;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse single ATP step: {StepText}", stepText.Substring(0, Math.Min(100, stepText.Length)));
                return null;
            }
        }

        /// <summary>
        /// Extract just the step numbers/identifiers from ATP content
        /// </summary>
        /// <param name="atpContent">ATP document content</param>
        /// <returns>List of step identifiers in order</returns>
        public List<string> ExtractStepNumbers(string atpContent)
        {
            var stepNumbers = new List<string>();
            
            // Extract numbered steps
            var numberMatches = StepNumberPattern.Matches(atpContent);
            foreach (Match match in numberMatches)
            {
                if (match.Success)
                {
                    stepNumbers.Add(match.Groups[1].Value);
                }
            }
            
            // Extract procedure/section identifiers
            var procedureMatches = ProcedurePattern.Matches(atpContent);
            foreach (Match match in procedureMatches)
            {
                if (match.Success)
                {
                    stepNumbers.Add($"Procedure_{match.Groups[1].Value}");
                }
            }
            
            return stepNumbers.Distinct().ToList();
        }

        // Private helper methods
        
        private async Task<ParsedATPStep?> ParseSingleLineAsync(string line, int lineNumber, ATPParsingOptions options)
        {
            // Check for step number patterns
            var stepMatch = StepNumberPattern.Match(line);
            var procedureMatch = ProcedurePattern.Match(line);
            
            if (stepMatch.Success || procedureMatch.Success || IsLikelyStep(line, options))
            {
                var step = new ParsedATPStep
                {
                    StepId = Guid.NewGuid().ToString(),
                    StepText = line,
                    LineNumber = lineNumber,
                    ParsingConfidence = CalculateParsingConfidence(line)
                };

                if (stepMatch.Success)
                {
                    step.StepNumber = stepMatch.Groups[1].Value;
                    step.StepText = StepNumberPattern.Replace(line, "").Trim();
                }
                else if (procedureMatch.Success)
                {
                    step.StepNumber = $"Procedure_{procedureMatch.Groups[1].Value}";
                    step.StepText = ProcedurePattern.Replace(line, "").Trim();
                }

                // Classify step type
                step.StepType = ClassifyStepType(step.StepText);
                
                return step;
            }
            
            return null;
        }

        private async Task<string> ParseMultiLineStepAsync(string[] lines, int startIndex, ATPParsingOptions options)
        {
            var combinedText = lines[startIndex];
            
            // Look for continuation lines (indented or unnumbered lines that follow)
            for (int i = startIndex + 1; i < lines.Length && i < startIndex + 5; i++) // Limit lookahead
            {
                var nextLine = lines[i].Trim();
                
                // Stop if we hit another step
                if (StepNumberPattern.IsMatch(nextLine) || ProcedurePattern.IsMatch(nextLine))
                    break;
                
                // Stop if line is too short or looks like a header
                if (string.IsNullOrWhiteSpace(nextLine) || nextLine.Length < 3)
                    break;
                
                // Add continuation if it looks like part of the step
                if (IsLikeContinuation(nextLine))
                {
                    combinedText += " " + nextLine;
                }
                else
                {
                    break;
                }
            }
            
            return combinedText;
        }

        private void ParseStepMetadata(ParsedATPStep step, string fullText, ATPParsingOptions options)
        {
            var lowerText = fullText.ToLowerInvariant();
            
            // Extract action verbs
            step.ActionVerbs = ActionVerbs.Where(verb => lowerText.Contains(verb)).ToList();
            
            // Extract system references
            step.SystemReferences = SystemKeywords.Where(keyword => lowerText.Contains(keyword)).ToList();
            step.SystemReferences.AddRange(options.SystemKeywords.Where(keyword => lowerText.Contains(keyword.ToLowerInvariant())));
            
            // Extract measurement keywords
            step.MeasurementKeywords = MeasurementKeywords.Where(keyword => lowerText.Contains(keyword)).ToList();
            
            // Extract safety keywords
            step.SafetyKeywords = SafetyKeywords.Where(keyword => lowerText.Contains(keyword)).ToList();
            
            // Extract performance keywords  
            step.PerformanceKeywords = PerformanceKeywords.Where(keyword => lowerText.Contains(keyword)).ToList();
            
            // Adjust confidence based on metadata richness
            var metadataCount = step.ActionVerbs.Count + step.SystemReferences.Count + 
                               step.MeasurementKeywords.Count + step.SafetyKeywords.Count + 
                               step.PerformanceKeywords.Count;
            
            if (metadataCount == 0)
            {
                step.ParsingConfidence *= 0.7; // Lower confidence for steps with no recognized keywords
                step.ParsingWarnings.Add("No system capability keywords detected");
            }
        }

        private bool IsLikelyStep(string line, ATPParsingOptions options)
        {
            var lowerLine = line.ToLowerInvariant();
            
            // Look for action verbs that suggest this is a step
            if (ActionVerbs.Any(verb => lowerLine.Contains(verb)))
                return true;
                
            // Look for "shall" statements
            if (lowerLine.Contains("shall") && line.Length > options.MinimumStepLength)
                return true;
                
            // Look for measurement/verification language
            if (MeasurementKeywords.Any(keyword => lowerLine.Contains(keyword)))
                return true;
            
            return false;
        }

        private bool IsLikeContinuation(string line)
        {
            // Simple heuristics for continuation lines
            return !line.EndsWith('.') && 
                   !line.StartsWith("Step", StringComparison.OrdinalIgnoreCase) &&
                   !line.StartsWith("Procedure", StringComparison.OrdinalIgnoreCase) &&
                   line.Length > 10;
        }

        private ATPStepType ClassifyStepType(string stepText)
        {
            var lowerText = stepText.ToLowerInvariant();
            
            // Safety steps
            if (SafetyKeywords.Any(keyword => lowerText.Contains(keyword)))
                return ATPStepType.Safety;
            
            // Setup/configuration steps
            if (lowerText.Contains("setup") || lowerText.Contains("configure") || lowerText.Contains("initialize"))
                return ATPStepType.Setup;
            
            // Verification/measurement steps
            if (lowerText.Contains("verify") || lowerText.Contains("measure") || lowerText.Contains("check"))
                return ATPStepType.Verification;
            
            // Cleanup steps
            if (lowerText.Contains("cleanup") || lowerText.Contains("shutdown") || lowerText.Contains("return"))
                return ATPStepType.Cleanup;
            
            // Configuration steps
            if (lowerText.Contains("set") || lowerText.Contains("adjust") || lowerText.Contains("configure"))
                return ATPStepType.Configuration;
            
            // Documentation steps
            if (lowerText.Contains("record") || lowerText.Contains("document") || lowerText.Contains("report"))
                return ATPStepType.Documentation;
            
            // Default to Action
            return ATPStepType.Action;
        }

        private double CalculateParsingConfidence(string text)
        {
            double confidence = 0.5; // Base confidence
            
            // Increase confidence for structural markers
            if (StepNumberPattern.IsMatch(text)) confidence += 0.3;
            if (ProcedurePattern.IsMatch(text)) confidence += 0.3;
            
            // Increase confidence for action verbs
            var lowerText = text.ToLowerInvariant();
            if (ActionVerbs.Any(verb => lowerText.Contains(verb))) confidence += 0.2;
            
            // Increase confidence for system keywords
            if (SystemKeywords.Any(keyword => lowerText.Contains(keyword))) confidence += 0.1;
            
            return Math.Min(1.0, confidence);
        }

        private List<ParsedATPStep> FilterBoilerplateSteps(List<ParsedATPStep> steps)
        {
            return steps.Where(step => 
            {
                var lowerText = step.StepText.ToLowerInvariant();
                
                // Filter out common boilerplate
                if (lowerText.Contains("end of procedure")) return false;
                if (lowerText.Contains("this completes")) return false;
                if (lowerText.Length < 15) return false; // Very short steps are likely boilerplate
                
                return true;
            }).ToList();
        }
    }
}