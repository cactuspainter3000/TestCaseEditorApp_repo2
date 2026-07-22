using System;
using System.Text.RegularExpressions;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Tracks requirement extraction progress from real stage completions and save-loop counts.
    /// No timer-based interpolation is used.
    /// </summary>
    public sealed class RequirementExtractionProgressTracker
    {
        private static readonly Regex SaveProgressRegex = new(
            @"(?:Jama save progress|Retry save progress):\s*(?<processed>\d+)\s*/\s*(?<total>\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private int _currentPercent;

        public int CurrentPercent => _currentPercent;

        public void Reset()
        {
            _currentPercent = 0;
        }

        public int AdvanceFromMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return _currentPercent;
            }

            var mapped = MapMessageToPercent(message);
            if (!mapped.HasValue)
            {
                return _currentPercent;
            }

            _currentPercent = Math.Max(_currentPercent, Math.Clamp(mapped.Value, 0, 99));
            return _currentPercent;
        }

        public int AdvanceFromDiscoveryCount(int discoveredCount)
        {
            if (discoveredCount <= 0)
            {
                return _currentPercent;
            }

            // Discovery is a real unit of work. Increase smoothly across the extraction band.
            var cappedCount = Math.Min(discoveredCount, 20);
            var mapped = 58 + (int)Math.Round((cappedCount / 20d) * 18d);
            _currentPercent = Math.Max(_currentPercent, mapped);
            return _currentPercent;
        }

        public int Complete()
        {
            _currentPercent = 100;
            return _currentPercent;
        }

        private static int? MapMessageToPercent(string message)
        {
            if (TryMapSaveProgress(message, out var savePercent))
            {
                return savePercent;
            }

            if (message.Contains("Preparing to extract requirements", StringComparison.OrdinalIgnoreCase)) return 3;
            if (message.Contains("Checking AI service availability", StringComparison.OrdinalIgnoreCase)) return 6;
            if (message.Contains("Verifying Ollama service", StringComparison.OrdinalIgnoreCase)) return 8;
            if (message.Contains("AI services available", StringComparison.OrdinalIgnoreCase)) return 10;
            if (message.Contains("Downloading attachment", StringComparison.OrdinalIgnoreCase)) return 14;
            if (message.Contains("Processing with reliable RAG-enhanced analysis", StringComparison.OrdinalIgnoreCase)) return 18;
            if (message.Contains("Using fallback requirement extraction", StringComparison.OrdinalIgnoreCase)) return 20;
            if (message.Contains("Processing '", StringComparison.OrdinalIgnoreCase)) return 22;
            if (message.Contains("Preparing extraction-aware document index", StringComparison.OrdinalIgnoreCase)) return 30;
            if (message.Contains("Reusing existing index", StringComparison.OrdinalIgnoreCase)) return 36;
            if (message.Contains("Standardizing ATP content", StringComparison.OrdinalIgnoreCase)) return 42;
            if (message.Contains("Extracting requirement clauses", StringComparison.OrdinalIgnoreCase)) return 46;
            if (message.Contains("Using structured extraction", StringComparison.OrdinalIgnoreCase)) return 52;
            if (message.Contains("AI analyzing document with structured output", StringComparison.OrdinalIgnoreCase)) return 60;
            if (message.Contains("Validating", StringComparison.OrdinalIgnoreCase) && message.Contains("requirements", StringComparison.OrdinalIgnoreCase)) return 72;
            if (message.Contains("Enriching", StringComparison.OrdinalIgnoreCase) && message.Contains("requirements", StringComparison.OrdinalIgnoreCase)) return 80;
            if (message.Contains("Found", StringComparison.OrdinalIgnoreCase) && message.Contains("requirements", StringComparison.OrdinalIgnoreCase)) return 84;
            if (message.Contains("Saving", StringComparison.OrdinalIgnoreCase) && message.Contains("extracted requirements to Jama", StringComparison.OrdinalIgnoreCase)) return 88;
            if (message.Contains("Retrying Jama save", StringComparison.OrdinalIgnoreCase)) return 92;
            if (message.Contains("Skipped", StringComparison.OrdinalIgnoreCase) && message.Contains("already-saved requirements", StringComparison.OrdinalIgnoreCase)) return 96;
            if (message.Contains("Saved", StringComparison.OrdinalIgnoreCase) && message.Contains("extracted requirements to Jama", StringComparison.OrdinalIgnoreCase)) return 98;

            return null;
        }

        private static bool TryMapSaveProgress(string message, out int percent)
        {
            percent = 0;
            var match = SaveProgressRegex.Match(message);
            if (!match.Success)
            {
                return false;
            }

            var processedParsed = int.TryParse(match.Groups["processed"].Value, out var processed);
            var totalParsed = int.TryParse(match.Groups["total"].Value, out var total);
            if (!processedParsed || !totalParsed || total <= 0)
            {
                percent = 88;
                return true;
            }

            var ratio = Math.Clamp((double)processed / total, 0d, 1d);
            percent = 88 + (int)Math.Round(ratio * 10d);
            return true;
        }
    }
}