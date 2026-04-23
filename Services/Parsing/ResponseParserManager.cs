using System;
using System.Collections.Generic;
using System.Linq;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services.Parsing
{
    /// <summary>
    /// Manages multiple response parsers and automatically selects the best one for a given response.
    /// Tries all compatible parsers in order until one successfully returns a parsed result.
    /// </summary>
    public class ResponseParserManager
    {
        private readonly List<IResponseParser> _parsers;

        public ResponseParserManager()
        {
            _parsers = new List<IResponseParser>
            {
                new DelimitedResponseParser(),
                new StructuredAnalysisResponseParser(),
                new JsonRequirementAnalysisParser()
            };
        }

        /// <summary>
        /// Parse an LLM response using the first compatible parser that succeeds.
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

            TestCaseEditorApp.Services.Logging.Log.Debug(
                $"[ParserManager] Attempting to parse response for {requirementId}, length: {response.Length}");

            var compatibleParsers = _parsers
                .Where(p => SafeCanParse(p, response, requirementId))
                .ToList();

            if (compatibleParsers.Count == 0)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn(
                    $"[ParserManager] No compatible parser found for {requirementId}");
                TestCaseEditorApp.Services.Logging.Log.Debug(
                    $"[ParserManager] Response preview (first 200 chars): {response.Substring(0, Math.Min(200, response.Length))}");
                return null;
            }

            TestCaseEditorApp.Services.Logging.Log.Info(
                $"[ParserManager] Compatible parsers for {requirementId}: {string.Join(", ", compatibleParsers.Select(p => p.ParserName))}");

            foreach (var parser in compatibleParsers)
            {
                try
                {
                    TestCaseEditorApp.Services.Logging.Log.Info(
                        $"[ParserManager] Trying {parser.ParserName} parser for {requirementId}");

                    var result = parser.ParseResponse(response, requirementId);

                    if (result != null)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info(
                            $"[ParserManager] Successfully parsed {requirementId} using {parser.ParserName} parser");
                        return result;
                    }

                    TestCaseEditorApp.Services.Logging.Log.Warn(
                        $"[ParserManager] {parser.ParserName} parser returned null for {requirementId}, trying next compatible parser");
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error(
                        ex,
                        $"[ParserManager] {parser.ParserName} parser failed for {requirementId}, trying next compatible parser");
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Warn(
                $"[ParserManager] All compatible parsers failed for {requirementId}");
            return null;
        }

        /// <summary>
        /// Safely checks whether a parser can handle the response.
        /// </summary>
        private bool SafeCanParse(IResponseParser parser, string response, string requirementId)
        {
            try
            {
                return parser.CanParse(response);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(
                    ex,
                    $"[ParserManager] {parser.ParserName}.CanParse threw an exception for {requirementId}");
                return false;
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
        /// Test which parsers would be selected for a given response.
        /// Useful for debugging parsing issues.
        /// </summary>
        /// <param name="response">Response to test</param>
        /// <returns>Name of compatible parsers, or "None" if no parser matches</returns>
        public string GetSelectedParserName(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return "None (empty response)";

            var compatibleParsers = _parsers
                .Where(p =>
                {
                    try
                    {
                        return p.CanParse(response);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .Select(p => p.ParserName)
                .ToList();

            return compatibleParsers.Count > 0
                ? string.Join(", ", compatibleParsers)
                : "None";
        }
    }
}