using System;
using System.Collections.Generic;
using System.Linq;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services.Parsing
{
    /// <summary>
    /// Manages multiple response parsers and automatically selects the best one for a given response.
    /// Implements chain-of-responsibility pattern for parsing different response formats.
    /// </summary>
    public class ResponseParserManager
    {
        private readonly List<IResponseParser> _parsers;

        public ResponseParserManager()
        {
            _parsers = new List<IResponseParser>
            {
                // Current RAG responses are JSON (sometimes wrapped in markdown fences).
                new JsonResponseParser(),
                // Legacy scrape/export requirement blocks use REQ-ID/Text/Category/Priority/Verification fields.
                new LegacyRequirementExtractionParser(),
                // Keep legacy delimited support for older prompt flows.
                new DelimitedResponseParser()
            };
        }

        /// <summary>
        /// Parse an LLM response using the most appropriate parser.
        /// Tries parsers in order until one succeeds.
        /// </summary>
        /// <param name="response">Raw LLM response text</param>
        /// <param name="requirementId">ID of the requirement being analyzed (for logging)</param>
        /// <returns>Parsed RequirementAnalysis or null if no parser could handle the response</returns>
        public RequirementAnalysis? ParseResponse(string response, string requirementId)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[ParserManager] Empty response for {requirementId}");
                return null;
            }

            TestCaseEditorApp.Services.Logging.Log.Debug($"[ParserManager] Attempting to parse response for {requirementId}, length: {response.Length}");

            var route = new List<string>();

            foreach (var parser in _parsers)
            {
                bool canParse;
                try
                {
                    canParse = parser.CanParse(response);
                }
                catch (Exception ex)
                {
                    route.Add($"{parser.ParserName}:canparse-error");
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[ParserManager] {parser.ParserName} CanParse failed for {requirementId}: {ex.Message}");
                    continue;
                }

                if (!canParse)
                {
                    route.Add($"{parser.ParserName}:skip");
                    continue;
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[ParserManager] Using {parser.ParserName} parser for {requirementId}");

                try
                {
                    var result = parser.ParseResponse(response, requirementId);

                    if (result != null)
                    {
                        route.Add($"{parser.ParserName}:success");
                        TestCaseEditorApp.Services.Logging.Log.Info($"[ParserManager] Successfully parsed {requirementId} using {parser.ParserName} parser. Route={string.Join(" -> ", route)}");
                        return result;
                    }

                    route.Add($"{parser.ParserName}:null");
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[ParserManager] {parser.ParserName} parser returned null for {requirementId}; trying next parser.");
                }
                catch (Exception ex)
                {
                    route.Add($"{parser.ParserName}:error");
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ParserManager] {parser.ParserName} parser failed for {requirementId}; trying next parser.");
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Warn($"[ParserManager] No parser produced a valid analysis for {requirementId}. Route={string.Join(" -> ", route)}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ParserManager] Response preview (first 200 chars): {response.Substring(0, Math.Min(200, response.Length))}");
            return null;
        }

        /// <summary>
        /// Get information about available parsers.
        /// </summary>
        /// <returns>List of parser names and their capabilities</returns>
        public string GetParserInfo()
        {
            var info = $"Available parsers ({_parsers.Count}): ";
            info += string.Join(", ", _parsers.Select(p => p.ParserName));
            return info;
        }

        /// <summary>
        /// Test which parser would be selected for a given response.
        /// Useful for debugging parsing issues.
        /// </summary>
        /// <param name="response">Response to test</param>
        /// <returns>Name of the parser that would be selected, or "None" if no parser matches</returns>
        public string GetSelectedParserName(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return "None (empty response)";

            var compatibleParser = _parsers.FirstOrDefault(p => p.CanParse(response));
            return compatibleParser?.ParserName ?? "None";
        }
    }
}