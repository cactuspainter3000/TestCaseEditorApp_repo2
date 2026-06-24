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

        /// <summary>
        /// Hard cap to prevent runaway segmentation from creating excessive per-step LLM calls.
        /// </summary>
        public int MaxStepsToAnalyze { get; set; } = 120;
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
            "set", "adjust", "maintain", "limit", "prevent", "ensure", "provide",
            // Video/media processing verbs
            "play", "stop", "pause", "resume", "stream", "broadcast", "capture", "record",
            "encode", "decode", "compress", "decompress", "render", "output", "input",
            // General test procedure verbs
            "execute", "run", "start", "finish", "complete", "perform", "operate", "function",
            "connect", "disconnect", "load", "save", "store", "retrieve", "update", "refresh"
        };
        
        // System component keywords
        private static readonly string[] SystemKeywords = {
            "power", "voltage", "current", "signal", "interface", "bus", "controller", 
            "processor", "memory", "storage", "network", "communication", "sensor", 
            "actuator", "display", "indicator", "switch", "relay", "connector", "cable",
            // Video/media processing keywords (common in test procedures)
            "video", "audio", "stream", "frame", "fps", "resolution", "codec", "format",
            "encode", "decode", "render", "capture", "output", "input", "channel",
            // General system keywords 
            "system", "device", "component", "module", "unit", "equipment", "hardware", "software",
            // Test/measurement related
            "data", "information", "status", "state", "mode", "function", "operation"
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
            "seconds", "minutes", "milliseconds", "microseconds", "Hz", "kHz", "MHz", "GHz",
            // Video performance keywords
            "fps", "frame", "frames", "framerate", "bitrate", "quality", "resolution",
            "1080p", "720p", "4K", "HD", "UHD", "progressive", "interlaced",
            // General performance terms
            "performance", "efficiency", "load", "capacity", "utilization", "metric"
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
                        var (fullStepText, lastConsumedIndex) = await ParseMultiLineStepAsync(lines, i, parseOptions);
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

                        // Skip continuation lines that were consumed into this step to avoid overlap amplification.
                        if (lastConsumedIndex > i)
                        {
                            i = lastConsumedIndex;
                        }
                    }
                }

                // Filter out boilerplate if requested
                if (parseOptions.SkipBoilerplate)
                {
                    steps = FilterBoilerplateSteps(steps);
                }

                if (parseOptions.MaxStepsToAnalyze > 0 && steps.Count > parseOptions.MaxStepsToAnalyze)
                {
                    steps = ReduceExcessSteps(steps, parseOptions.MaxStepsToAnalyze);
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

        private async Task<(string StepText, int LastConsumedIndex)> ParseMultiLineStepAsync(string[] lines, int startIndex, ATPParsingOptions options)
        {
            var combinedText = lines[startIndex];
            var lastConsumedIndex = startIndex;
            
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
                    lastConsumedIndex = i;
                }
                else
                {
                    break;
                }
            }
            
            return (combinedText, lastConsumedIndex);
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
            
            // Filter out obvious non-steps first
            if (IsDocumentHeader(line)) return false;
            if (line.Length < options.MinimumStepLength) return false;
            if (GetWordCount(line) < 6) return false;

            // Recognize structured requirement records like:
            // ID: C4B_ATR-143 The MFD shall ... Test_type: BIT Test_Venue: HASS Functional
            var hasStructuredId = Regex.IsMatch(line, @"\bID\s*:\s*[A-Za-z0-9][A-Za-z0-9_.\-]*\b", RegexOptions.IgnoreCase);
            var hasFieldMarkers = Regex.IsMatch(line, @"\b(Test[_ ]?type|Test[_ ]?venue)\s*:", RegexOptions.IgnoreCase);
            var hasShallStatement = Regex.IsMatch(line, @"\b[A-Za-z][A-Za-z0-9_\-/ ]{1,60}\s+shall\b", RegexOptions.IgnoreCase);
            if (hasStructuredId && (hasShallStatement || hasFieldMarkers)) return true;
            
            // Strong indicator: explicit requirement language
            if (hasShallStatement)
                return true;

            // For unnumbered lines, require at least two semantic indicators to avoid line explosion.
            var hasActionVerb = ActionVerbs.Any(verb => lowerLine.Contains(verb));
            var hasMeasurementKeyword = MeasurementKeywords.Any(keyword => lowerLine.Contains(keyword));
            var hasSystemKeyword = SystemKeywords.Any(keyword => lowerLine.Contains(keyword));
            var hasSafetyKeyword = SafetyKeywords.Any(keyword => lowerLine.Contains(keyword));
            var indicatorCount = 0;
            if (hasActionVerb) indicatorCount++;
            if (hasMeasurementKeyword) indicatorCount++;
            if (hasSystemKeyword) indicatorCount++;
            if (hasSafetyKeyword) indicatorCount++;

            if (indicatorCount >= 2)
                return true;
            
            return false;
        }

        private static int GetWordCount(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            return Regex.Matches(text, @"\b\w+\b").Count;
        }
        
        private bool IsDocumentHeader(string line)
        {
            var lowerLine = line.ToLowerInvariant();
            
            // Document revision/proprietary headers
            if (lowerLine.Contains("proprietary") || lowerLine.Contains("confidential")) return true;
            if (lowerLine.Contains("rev ") && lowerLine.Contains("-")) return true;
            if (lowerLine.Contains("initial rel")) return true;
            
            // Date patterns (common in headers)
            if (Regex.IsMatch(line, @"\d{1,2}-\w+-\d{4}")) return true; // "18-April-2019"
            if (Regex.IsMatch(line, @"\d{4}-\d{2}-\d{2}")) return true; // "2019-04-18"
            
            // Part numbers without actual test content
            if (Regex.IsMatch(line, @"^[A-Z0-9]+-[A-Z0-9]+.*Rev\s") && line.Length < 100) return true;
            
            // Author/signature lines
            if (lowerLine.Contains("signed by") || lowerLine.Contains("author:")) return true;
            
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
            double confidence = 0.6; // Increase base confidence from 0.5 to 0.6 to be less strict
            
            // Increase confidence for structural markers
            if (StepNumberPattern.IsMatch(text)) confidence += 0.3;
            if (ProcedurePattern.IsMatch(text)) confidence += 0.3;
            
            // Increase confidence for action verbs
            var lowerText = text.ToLowerInvariant();
            if (ActionVerbs.Any(verb => lowerText.Contains(verb))) confidence += 0.2;
            
            // Increase confidence for system keywords  
            if (SystemKeywords.Any(keyword => lowerText.Contains(keyword))) confidence += 0.1;
            
            // Give partial credit for measurement units and numbers (common in test procedures)
            if (Regex.IsMatch(text, @"\d+\s*[a-zA-Z%]+")) confidence += 0.1; // "60 fps", "100%", etc.
            
            // Give credit for "shall" statements (requirements language)
            if (lowerText.Contains("shall")) confidence += 0.1;
            
            return Math.Min(1.0, confidence);
        }

        private List<ParsedATPStep> FilterBoilerplateSteps(List<ParsedATPStep> steps)
        {
            var originalCount = steps.Count;
            var filtered = steps.Where(step => 
            {
                var lowerText = step.StepText.ToLowerInvariant();
                
                // Filter out common boilerplate
                if (lowerText.Contains("end of procedure")) return false;
                if (lowerText.Contains("this completes")) return false;
                if (lowerText.Length < 15) return false; // Very short steps are likely boilerplate
                
                // Filter out document metadata that sneaked through
                if (IsDocumentHeader(step.StepText)) return false;
                
                // Require minimum confidence for ATP steps (lowered from 0.6 to 0.3 to be less aggressive)
                if (step.ParsingConfidence < 0.3) return false;
                
                return true;
            }).ToList();
            
            _logger.LogInformation("Boilerplate filtering: {OriginalCount} → {FilteredCount} steps (removed {RemovedCount})", 
                originalCount, filtered.Count, originalCount - filtered.Count);
            
            return filtered;
        }

        private List<ParsedATPStep> ReduceExcessSteps(List<ParsedATPStep> steps, int maxSteps)
        {
            var numbered = steps
                .Where(step => !string.IsNullOrWhiteSpace(step.StepNumber))
                .OrderBy(step => step.LineNumber)
                .ToList();

            var selected = new List<ParsedATPStep>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var step in numbered)
            {
                if (selected.Count >= maxSteps) break;
                var key = NormalizeStepKey(step.StepText);
                if (seen.Add(key))
                {
                    selected.Add(step);
                }
            }

            if (selected.Count < maxSteps)
            {
                var unnumbered = steps
                    .Where(step => string.IsNullOrWhiteSpace(step.StepNumber))
                    .OrderByDescending(step => step.ParsingConfidence)
                    .ThenByDescending(step => step.StepText?.Length ?? 0)
                    .ToList();

                foreach (var step in unnumbered)
                {
                    if (selected.Count >= maxSteps) break;
                    var key = NormalizeStepKey(step.StepText);
                    if (seen.Add(key))
                    {
                        selected.Add(step);
                    }
                }
            }

            var reduced = selected
                .OrderBy(step => step.LineNumber)
                .ToList();

            _logger.LogWarning("Step reduction applied: {OriginalCount} -> {ReducedCount} (max {MaxSteps})", steps.Count, reduced.Count, maxSteps);
            return reduced;
        }

        private static string NormalizeStepKey(string? stepText)
        {
            if (string.IsNullOrWhiteSpace(stepText))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(stepText, @"\s+", " ").Trim();
            return normalized.Length <= 220 ? normalized : normalized.Substring(0, 220);
        }
    }
}