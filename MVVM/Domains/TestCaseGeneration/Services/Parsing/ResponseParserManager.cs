using System;
using System.Collections.Generic;
using System.Linq;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.Parsing
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
                // AnythingLLM returns delimited format due to its system prompt configuration
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

            // Find first parser that can handle this response
            var compatibleParser = _parsers.FirstOrDefault(p => p.CanParse(response));
            
            if (compatibleParser == null)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[ParserManager] No compatible parser found for {requirementId}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[ParserManager] Response preview (first 200 chars): {response.Substring(0, Math.Min(200, response.Length))}");
                return null;
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[ParserManager] Using {compatibleParser.ParserName} parser for {requirementId}");

            try
            {
                var result = compatibleParser.ParseResponse(response, requirementId);
                
                if (result != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[ParserManager] Successfully parsed {requirementId} using {compatibleParser.ParserName} parser");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[ParserManager] {compatibleParser.ParserName} parser returned null for {requirementId}");
                }

                return result;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ParserManager] {compatibleParser.ParserName} parser failed for {requirementId}");
                return null;
            }
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