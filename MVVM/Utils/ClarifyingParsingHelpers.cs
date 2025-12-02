using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Utils
{
    /// <summary>
    /// Parsing helpers (extracted/ported from the original TestCaseGenerator_CoreVM).
    /// - Preclean: remove fences/labels and trim.
    /// - ParseQuestions: JSON-first tolerant parser -> List<ClarifyingQuestionVM>.
    /// - TryParseJsonQuestions: strict JSON parser for the questions contract.
    /// - ExtractFirstJsonArray: extract balanced first JSON array substring.
    /// - TryExtractSuggestedChipKeys: get suggested-default keys from JSON object slice or SUGGEST: block
    ///   and map friendly names to keys using SuggestedDefaults catalog (if provided).
    /// - IndexOfIgnoreCase: small utility.
    /// - BuildBudgetedQuestionsPrompt: builds the clarifying-question prompt given basic context.
    /// 
    /// These helpers return ClarifyingQuestionVM instances so the VM can simply add them to its collection.
    /// </summary>
    public static class ClarifyingParsingHelpers
    {
        public static string Preclean(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            // strip common code fences: ```json ... ``` or ``` ... ```
            s = Regex.Replace(s, @"```(?:json)?\s*([\s\S]*?)```", "$1", RegexOptions.IgnoreCase);

            // remove "Output:" / "Response:" prefix if present
            s = Regex.Replace(s, @"^\s*(?:output|response)\s*:\s*", "", RegexOptions.IgnoreCase);

            // collapse weird leading BOM/whitespace
            s = s.Trim('\uFEFF', ' ', '\t', '\r', '\n');

            return s;
        }

        /// <summary>
        /// Clean question text that may contain embedded JSON patterns
        /// </summary>
        private static string CleanQuestionText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Pattern: Starts with or contains JSON object like {"text":"actual question", ...}
            // Extract just the "text" field value
            if (text.Contains("{") && text.Contains("\"text\""))
            {
                // Try to extract the text field value
                var textFieldPattern = new Regex(@"""text""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
                var match = textFieldPattern.Match(text);
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
            }

            return text;
        }

        /// <summary>
        /// Parse clarifying questions from arbitrary LLM text using tolerant, JSON-first approach.
        /// Returns an empty list when none found.
        /// </summary>
        public static List<ClarifyingQuestionVM> ParseQuestions(string llmText, MainViewModel? mainVm = null)
        {
            var results = new List<ClarifyingQuestionVM>();
            if (string.IsNullOrWhiteSpace(llmText)) return results;

            // normalize and preclean
            llmText = Preclean(llmText);

            // JSON-first detection: quick heuristics
            bool looksJson =
                llmText.IndexOf('[') >= 0 &&
                llmText.IndexOf('{') >= 0 &&
                llmText.IndexOf("\"text\"", StringComparison.OrdinalIgnoreCase) >= 0;

            if (looksJson)
            {
                if (TryParseJsonQuestions(llmText, out var jsonQs) && jsonQs.Count > 0)
                {
                    // Clean each question text in case they contain embedded JSON
                    foreach (var q in jsonQs)
                    {
                        q.Text = CleanQuestionText(q.Text);
                    }
                    return jsonQs;
                }

                var arrSlice = ExtractFirstJsonArray(llmText);
                if (!string.IsNullOrWhiteSpace(arrSlice) && TryParseJsonQuestions(arrSlice, out var retry) && retry.Count > 0)
                {
                    // Clean each question text in case they contain embedded JSON
                    foreach (var q in retry)
                    {
                        q.Text = CleanQuestionText(q.Text);
                    }
                    return retry;
                }
            }

            // If JSON parsing failed, check for malformed single question patterns
            // Only try this if we didn't find a valid JSON array
            var cleaned = CleanQuestionText(llmText);
            
            // If cleaning extracted a simple question (no JSON markers), return it
            if (cleaned != llmText && !cleaned.Contains("[") && !cleaned.Contains("{") && cleaned.Contains("?"))
            {
                results.Add(new ClarifyingQuestionVM(cleaned.Trim(), new List<string>(), mainVm));
                return results;
            }

            // Pattern: {"text":"..."} or {"text":"...", "category":"...", ...} SUGGEST: ... or similar
            // More flexible pattern that handles any JSON object containing a "text" field
            var jsonObjectPattern = new Regex(@"^\s*\{[^}]*""text""\s*:\s*""([^""]+)""[^}]*\}", RegexOptions.IgnoreCase);
            var match = jsonObjectPattern.Match(llmText);
            if (match.Success)
            {
                // Extract just the text value and use it as the question
                var extractedText = match.Groups[1].Value;
                results.Add(new ClarifyingQuestionVM(extractedText.Trim(), new List<string>(), mainVm));
                return results;
            }

            // Tolerant text parse fallback (line-oriented)
            llmText = llmText.Replace("\r\n", "\n").Replace("\r", "\n");

            // Prefer MUST-ASK section if present
            var mustAskIdx = IndexOfIgnoreCase(llmText, "MUST-ASK");
            if (mustAskIdx < 0) mustAskIdx = IndexOfIgnoreCase(llmText, "MUST ASK");
            if (mustAskIdx >= 0) llmText = llmText.Substring(mustAskIdx);

            var lines = llmText.Split('\n');

            StringBuilder? currentQuestion = null;
            List<string>? currentOptions = null;

            void EmitCurrent()
            {
                if (currentQuestion == null) return;
                var text = currentQuestion.ToString().Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    currentQuestion = null;
                    currentOptions = null;
                    return;
                }

                var opts = currentOptions?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList()
                           ?? new List<string>();

                if (opts.Count == 1 && opts[0].Contains("|"))
                    opts = opts[0].Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

                // Create an item-level VM (ClarifyingQuestionVM)
                results.Add(new ClarifyingQuestionVM(text, opts, mainVm));
                currentQuestion = null;
                currentOptions = null;
            }

            List<string> ParseOptionsInline(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return new List<string>();
                s = s.Trim();

                // Only honor square brackets for options; ignore parentheses
                if (s.StartsWith("[") && s.EndsWith("]"))
                    s = s.Substring(1, s.Length - 2);

                // Split on '|' or ' or '
                var parts = Regex.Split(s, @"\s*\|\s*|\s+or\s+|\s+OR\s+");
                return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            }

            var questionStartRegex = new Regex(@"^\s*(?:Q\s*\d+|Q\d+|\d+)\s*[:\)\.]|\s*^-{1,2}\s*Q?\d+", RegexOptions.IgnoreCase);

            for (int i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                var line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (Regex.IsMatch(line, @"^\[.*\]$"))
                {
                    if (currentQuestion != null)
                    {
                        var opts = ParseOptionsInline(line);
                        currentOptions ??= new List<string>();
                        currentOptions.AddRange(opts);
                    }
                    continue;
                }

                var inlineOptMatch = Regex.Match(line, @"^(?<q>.*?)(?:\s*\[(?<opts>.+)\])\s*$");
                if (inlineOptMatch.Success && !string.IsNullOrWhiteSpace(inlineOptMatch.Groups["opts"].Value))
                {
                    if (currentQuestion != null) EmitCurrent();

                    var qPart = inlineOptMatch.Groups["q"].Value.Trim();
                    var optsText = inlineOptMatch.Groups["opts"].Value.Trim();

                    qPart = Regex.Replace(qPart, @"^\s*(?:Q\s*\d+|Q\d+|\d+)\s*[:\)\.]\s*", "", RegexOptions.IgnoreCase);

                    currentQuestion = new StringBuilder(qPart);
                    currentOptions = ParseOptionsInline(optsText);
                    EmitCurrent();
                    continue;
                }

                if (questionStartRegex.IsMatch(line) || line.StartsWith("Q", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentQuestion != null) EmitCurrent();
                    var qText = Regex.Replace(line, @"^\s*(?:Q\s*\d+|Q\d+|\d+)\s*[:\)\.]\s*", "", RegexOptions.IgnoreCase);
                    currentQuestion = new StringBuilder(qText.Trim());
                    currentOptions = new List<string>();
                    continue;
                }

                if (line.Contains("?") && currentQuestion == null)
                {
                    currentQuestion = new StringBuilder(line);
                    currentOptions = new List<string>();
                    continue;
                }

                if (currentQuestion != null)
                {
                    currentQuestion.Append(" ");
                    currentQuestion.Append(line);
                    continue;
                }

                var fallbackInline = Regex.Match(line, @"(?<q>.+?)\s*\[(?<opts>.*?)]\s*$");
                if (fallbackInline.Success)
                {
                    var qpart = fallbackInline.Groups["q"].Value.Trim();
                    var optPart = fallbackInline.Groups["opts"].Value.Trim();
                    currentQuestion = new StringBuilder(qpart);
                    currentOptions = ParseOptionsInline(optPart);
                    EmitCurrent();
                    continue;
                }
            }

            EmitCurrent();
            return results;
        }

        /// <summary>
        /// Try parse JSON array of objects with "text" and optional "options" and produce ClarifyingQuestionVM instances.
        /// </summary>
        public static bool TryParseJsonQuestions(string text, out List<ClarifyingQuestionVM> result, MainViewModel? mainVm = null)
        {
            result = new List<ClarifyingQuestionVM>();
            if (string.IsNullOrWhiteSpace(text)) return false;

            int start = text.IndexOf('[');
            int end = text.LastIndexOf(']');
            if (start < 0 || end <= start) return false;

            var json = text.Substring(start, end - start + 1).Trim();
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    if (!el.TryGetProperty("text", out var textProp) || textProp.ValueKind != JsonValueKind.String) continue;
                    var qText = textProp.GetString() ?? string.Empty;

                    List<string> opts = new();
                    if (el.TryGetProperty("options", out var optsProp) && optsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var o in optsProp.EnumerateArray())
                            if (o.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(o.GetString()))
                                opts.Add(o.GetString()!.Trim());
                    }
                    result.Add(new ClarifyingQuestionVM(qText.Trim(), opts, mainVm));
                }
                return result.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extracts the first balanced JSON array substring (if any), else null.
        /// </summary>
        public static string? ExtractFirstJsonArray(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            int first = s.IndexOf('[');
            if (first < 0) return null;
            int depth = 0;
            for (int i = first; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        var cand = s.Substring(first, i - first + 1).Trim();
                        try
                        {
                            using var doc = JsonDocument.Parse(cand);
                            if (doc.RootElement.ValueKind == JsonValueKind.Array) return cand;
                        }
                        catch { }
                        break;
                    }
                }
            }

            try
            {
                var m = Regex.Match(s, @"\[[\s\S]*\]", RegexOptions.Singleline);
                if (m.Success)
                {
                    var cand = m.Value.Trim();
                    try
                    {
                        using var doc2 = JsonDocument.Parse(cand);
                        if (doc2.RootElement.ValueKind == JsonValueKind.Array) return cand;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Extract suggested-default keys from LLM output.
        /// If suggestedDefaultsCatalog is provided, mapping friendly names -> keys will be attempted.
        /// </summary>
        public static List<string> TryExtractSuggestedChipKeys(string llmText, IEnumerable<DefaultItem>? suggestedDefaultsCatalog = null)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(llmText)) return results;

            // 1) Try JSON object slice for suggestedDefaults or suggestedIgnoreKeys
            try
            {
                int objStart = llmText.IndexOf('{');
                int objEnd = llmText.LastIndexOf('}');
                if (objStart >= 0 && objEnd > objStart)
                {
                    var jsonSlice = llmText.Substring(objStart, objEnd - objStart + 1);
                    using var doc = JsonDocument.Parse(jsonSlice);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("suggestedDefaults", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        results.AddRange(arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));

                    if (results.Count == 0 && root.TryGetProperty("suggested", out var arr2) && arr2.ValueKind == JsonValueKind.Array)
                        results.AddRange(arr2.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));

                    if (results.Count == 0 && root.TryGetProperty("suggestedIgnoreKeys", out var arr3) && arr3.ValueKind == JsonValueKind.Array)
                        results.AddRange(arr3.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));
                }
            }
            catch { /* ignore JSON slice errors */ }

            // 2) Plain-text SUGGEST: block parsing if no JSON suggestions found
            if (results.Count == 0)
            {
                var rx = new Regex(@"(?ms)^\s*SUGGEST\s*:?\s*\r?\n(?<block>.+?)(?:\r?\n\s*(?:CLARIFY|QUESTIONS|MUST-ASK|NICE-TO-HAVE|END|JSON)\b|\r?\n\s*\r?\n|$)");
                var m = rx.Match(llmText);
                if (m.Success)
                {
                    var block = m.Groups["block"].Value;
                    foreach (var raw in block.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var line = raw.Trim();
                        // strip bullet/numbering
                        line = Regex.Replace(line, @"^\s*(?:[-*•]+|\d+[\.\)]|[\u2022])\s*", "");
                        if (!string.IsNullOrWhiteSpace(line))
                            results.Add(line);
                    }
                }
            }

            // 3) Normalize and map Names -> Keys using SuggestedDefaults catalog
            if (results.Count == 0) return results;

            var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var byKey = suggestedDefaultsCatalog?.ToDictionary(d => d.Key ?? "", d => d, StringComparer.OrdinalIgnoreCase)
                       ?? new Dictionary<string, DefaultItem>(StringComparer.OrdinalIgnoreCase);
            var byName = suggestedDefaultsCatalog?
                .GroupBy(d => d.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, DefaultItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in results)
            {
                var token = (raw ?? "").Trim();

                // If it matches a Key, keep it
                if (byKey.ContainsKey(token))
                {
                    distinct.Add(byKey[token].Key);
                    continue;
                }

                // If it matches a Name, map to its Key
                if (byName.ContainsKey(token))
                {
                    distinct.Add(byName[token].Key);
                    continue;
                }

                // Try loose normalization once (spaces/underscores -> hyphen)
                var norm = Regex.Replace(token, @"[\s_]+", "-");
                if (byKey.ContainsKey(norm))
                {
                    distinct.Add(byKey[norm].Key);
                    continue;
                }
                if (byName.ContainsKey(norm))
                {
                    distinct.Add(byName[norm].Key);
                    continue;
                }
            }

            return distinct.ToList();
        }

        public static int IndexOfIgnoreCase(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return -1;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Build a clarifying-questions prompt following the budget/format contract used previously.
        /// </summary>
        public static string BuildBudgetedQuestionsPrompt(
            Requirement? req,
            int budget,
            bool thorough,
            IEnumerable<string> paragraphs,
            IEnumerable<TableDto> tables,
            string? customInstructions = null)
        {
            var id = req?.Item ?? req?.Name ?? "(unnamed requirement)";
            var desc = req?.Description ?? "(no description)";
            var methods = (req?.VerificationMethods != null && req.VerificationMethods.Count > 0)
                ? string.Join(",", req.VerificationMethods)
                : "(none)";

            var paraCount = paragraphs?.Count() ?? 0;
            var tableCount = tables?.Count() ?? 0;
            var maxQs = Math.Max(0, Math.Min(7, budget));

            var sb = new StringBuilder();
            sb.AppendLine("You are a test engineer extracting ONLY the MUST-ASK clarifying questions needed before authoring a verification test case,");
            sb.AppendLine("AND suggesting defaults (\"chips\") to auto-enable for this requirement.");
            sb.AppendLine($"Ask at most {maxQs} questions (0..{maxQs}). Mode: {(thorough ? "THOROUGH" : "FAST")}.");
            sb.AppendLine();

            // Include custom user instructions if provided
            if (!string.IsNullOrWhiteSpace(customInstructions))
            {
                sb.AppendLine("Special Instructions from User:");
                sb.AppendLine(customInstructions.Trim());
                sb.AppendLine();
            }

            sb.AppendLine("Context:");
            sb.AppendLine($"RequirementId: {id}");
            sb.AppendLine($"RequirementDescription: {desc}");
            sb.AppendLine($"VerificationMethods: {methods}");
            sb.AppendLine($"LooseParagraphsCount: {paraCount}");
            sb.AppendLine($"LooseTablesCount: {tableCount}");
            sb.AppendLine();

            // Include assumptions/previous answers if provided
            if (paraCount > 0)
            {
                sb.AppendLine("Already Known / Assumed:");
                sb.AppendLine("CRITICAL: The following topics have ALREADY been answered or assumed. DO NOT ask questions about these topics or any related/similar variations:");
                if (paragraphs != null)
                {
                    foreach (var p in paragraphs)
                    {
                        if (!string.IsNullOrWhiteSpace(p))
                        {
                            sb.AppendLine($"  - {p.Trim()}");
                        }
                    }
                }
                sb.AppendLine("If a question would be similar to or overlap with any of the above, skip it entirely and ask about a different aspect of the requirement.");
                sb.AppendLine();
            }

            sb.AppendLine("Questions Output Rules:");
            sb.AppendLine("- Return ONLY a JSON array of question objects. Nothing else.");
            sb.AppendLine("- Each question MUST be a separate object in the array.");
            sb.AppendLine("- JSON schema: [{\"text\": \"question here?\", \"options\": [\"option1\", \"option2\"]}]");
            sb.AppendLine("- The \"options\" field is optional - only include it if the question has multiple choice answers.");
            sb.AppendLine("- Do NOT return the entire array as a single string. Each question must be its own object.");
            sb.AppendLine("- Do NOT include question numbers, prefixes, or any other keys besides \"text\" and \"options\".");
            sb.AppendLine("- Do NOT wrap the JSON in markdown code fences or add any commentary.");
            sb.AppendLine();

            sb.AppendLine("Suggested Defaults (Chips) Output Rules:");
            sb.AppendLine("- AFTER the JSON array above, output a plain-text section titled exactly: SUGGEST:");
            sb.AppendLine("- Under SUGGEST:, list 0 or more items, one per line, prefixed with '-' (hyphen).");
            sb.AppendLine("- Use the provided catalog KEYS when known; if a key is unknown, use the exact chip NAME.");
            sb.AppendLine();

            sb.AppendLine("Return format (strict):");
            sb.AppendLine("1) First, the QUESTIONS JSON array exactly as specified (top-level array).");
            sb.AppendLine("2) Then, a newline and the SUGGEST: block with '-' bullet lines (may be empty).");
            sb.AppendLine("3) Absolutely no markdown/code fences/commentary before, between, or after these two parts.");
            sb.AppendLine();

            return sb.ToString();
        }
    }
}