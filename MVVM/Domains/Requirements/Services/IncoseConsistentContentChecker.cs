using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Services
{
    /// <summary>
    /// Deterministic INCOSE-style "Consistent Content" structural checker.
    /// Validates that requirements follow canonical actor-shall-action patterns.
    /// 
    /// Canonical forms:
    ///   General:            The {ACTOR} shall {ACTION}.
    ///   Event-driven:       The {ACTOR} shall {ACTION} when {CONDITIONS}.
    ///   State-driven:       The {ACTOR} shall {ACTION} while {CONDITIONS}.
    ///   Optional feature:   The {ACTOR} shall {ACTION} where {CONDITIONS}.
    ///   Timing constrained: Any of the above with explicit timing appended.
    ///   Multi-conditional:  Conditions combined with "and".
    /// 
    /// This is a rule-based check that runs before the LLM analysis so the LLM can
    /// reference the structural findings in its SuggestedEdit rewrites.
    /// </summary>
    public class IncoseConsistentContentChecker
    {
        // Obligation keyword: shall/must/will/should (in order of formality)
        private static readonly Regex ObligationPattern = new(
            @"\b(shall|must|will|should)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "Shall" is required for INCOSE-compliant formal requirements
        private static readonly Regex ShallPattern = new(
            @"\bshall\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Actor: "The <ACTOR> shall" — actor is a noun phrase before "shall"
        private static readonly Regex ActorShallPattern = new(
            @"\bthe\s+(?<actor>[A-Za-z][A-Za-z0-9_\-/ ]{1,60}?)\s+shall\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Conditional keywords indicating requirement type
        private static readonly Regex WhenPattern = new(@"\bwhen\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WhilePattern = new(@"\bwhile\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WherePattern = new(@"\bwhere\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Timing constraints: "within X ms/s/minutes", "no later than", "at least X times per"
        private static readonly Regex TimingPattern = new(
            @"\b(within\s+\d+\s*(ms|milliseconds?|seconds?|minutes?|hours?)|no later than|at least\s+\d+\s*(times?|cycles?)\s+per|every\s+\d+\s*(ms|seconds?|minutes?))\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Action: word immediately following "shall" (should be a verb)
        private static readonly Regex ShallActionPattern = new(
            @"\bshall\s+(?<action>\w+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Common non-action words that follow "shall" incorrectly (nouns, articles)
        private static readonly HashSet<string> NonActionWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "be", "is", "are", "was", "were", "have", "has", "had",
            "it", "this", "that", "which", "not"
        };

        // Mixed conditional: uses both "when" and "while" — indicates inconsistent phrasing
        private static readonly Regex MixedConditionalPattern = new(
            @"\bwhen\b.*\bwhile\b|\bwhile\b.*\bwhen\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public IncoseConsistentContentCheckResult Check(string requirementText)
        {
            var result = new IncoseConsistentContentCheckResult();

            if (string.IsNullOrWhiteSpace(requirementText))
            {
                result.Issues.Add(new IncoseIssue
                {
                    Code = "ICC-001",
                    Severity = "High",
                    Description = "Requirement text is empty.",
                    Suggestion = "Provide a complete requirement statement."
                });
                result.Passed = false;
                return result;
            }

            var text = requirementText.Trim();

            // --- Check 1: Obligation keyword presence ---
            if (!ObligationPattern.IsMatch(text))
            {
                result.Issues.Add(new IncoseIssue
                {
                    Code = "ICC-002",
                    Severity = "High",
                    Description = "Missing obligation keyword. INCOSE requirements must contain 'shall' (or 'must'/'will'/'should').",
                    Suggestion = "Rewrite using 'shall': \"The <ACTOR> shall <ACTION>.\""
                });
            }
            else if (!ShallPattern.IsMatch(text))
            {
                // Has must/will/should but not shall
                result.Issues.Add(new IncoseIssue
                {
                    Code = "ICC-003",
                    Severity = "Medium",
                    Description = "Requirement uses 'must', 'will', or 'should' instead of 'shall'. INCOSE recommends 'shall' as the formal obligation keyword.",
                    Suggestion = "Replace the obligation keyword with 'shall' for consistency."
                });
            }

            // --- Check 2: Actor presence ---
            var actorMatch = ActorShallPattern.Match(text);
            if (!actorMatch.Success)
            {
                result.Issues.Add(new IncoseIssue
                {
                    Code = "ICC-004",
                    Severity = "High",
                    Description = "Missing actor. No 'The <ACTOR> shall' pattern found. An explicit actor (system, subsystem, or component) must precede 'shall'.",
                    Suggestion = "Add an explicit actor: \"The <System/Component> shall <ACTION>.\""
                });
            }
            else
            {
                result.DetectedActor = actorMatch.Groups["actor"].Value.Trim();
            }

            // --- Check 3: Action follows "shall" ---
            var actionMatch = ShallActionPattern.Match(text);
            if (actionMatch.Success)
            {
                var action = actionMatch.Groups["action"].Value;
                if (NonActionWords.Contains(action))
                {
                    result.Issues.Add(new IncoseIssue
                    {
                        Code = "ICC-005",
                        Severity = "High",
                        Description = $"No action verb follows 'shall'. Found '{action}' instead of an action verb.",
                        Suggestion = "Ensure a clear action verb follows 'shall': \"The <ACTOR> shall <VERB> <OBJECT>.\""
                    });
                }
                else
                {
                    result.DetectedAction = action;
                }
            }

            // --- Check 4: Classify conditional pattern ---
            var hasWhen = WhenPattern.IsMatch(text);
            var hasWhile = WhilePattern.IsMatch(text);
            var hasWhere = WherePattern.IsMatch(text);
            var hasTiming = TimingPattern.IsMatch(text);

            if (hasWhen && hasWhile)
            {
                result.Issues.Add(new IncoseIssue
                {
                    Code = "ICC-006",
                    Severity = "Medium",
                    Description = "Mixed conditional phrasing: both 'when' (event-driven) and 'while' (state-driven) appear in the same requirement.",
                    Suggestion = "Use 'when' for event-triggered behavior or 'while' for state-driven behavior — not both. Consider splitting into two requirements."
                });
            }

            if (hasWhen) result.RequirementType = hasTiming ? "Event-Driven + Timing" : "Event-Driven";
            else if (hasWhile) result.RequirementType = hasTiming ? "State-Driven + Timing" : "State-Driven";
            else if (hasWhere) result.RequirementType = hasTiming ? "Optional-Feature + Timing" : "Optional-Feature";
            else if (hasTiming) result.RequirementType = "General + Timing";
            else result.RequirementType = "General";

            // --- Check 5: Condition placement ---
            // If "when/while/where" appears BEFORE "shall", the condition is likely embedded in the action
            var shallIndex = text.IndexOf("shall", StringComparison.OrdinalIgnoreCase);
            if (shallIndex > 0)
            {
                var beforeShall = text.Substring(0, shallIndex);
                var afterShall = text.Substring(shallIndex);

                // Conditional should appear after "shall", not before
                if (WhenPattern.IsMatch(beforeShall) || WhilePattern.IsMatch(beforeShall))
                {
                    result.Issues.Add(new IncoseIssue
                    {
                        Code = "ICC-007",
                        Severity = "Low",
                        Description = "Conditional clause ('when'/'while') appears before 'shall'. INCOSE convention places conditions after the action.",
                        Suggestion = "Restructure: \"The <ACTOR> shall <ACTION> when/while <CONDITION>.\""
                    });
                }

                // Check condition after shall exists when conditional keywords present
                if ((hasWhen || hasWhile || hasWhere) && afterShall.Split(' ').Length < 4)
                {
                    result.Issues.Add(new IncoseIssue
                    {
                        Code = "ICC-008",
                        Severity = "Medium",
                        Description = "Conditional keyword present but condition clause appears incomplete or missing.",
                        Suggestion = "Complete the condition: \"The <ACTOR> shall <ACTION> when/while <full condition description>.\""
                    });
                }
            }

            // Determine overall pass/fail
            result.Passed = result.Issues.TrueForAll(i => i.Severity == "Low");

            // Build canonical form suggestion
            result.CanonicalFormSuggestion = BuildCanonicalSuggestion(result);

            return result;
        }

        private string BuildCanonicalSuggestion(IncoseConsistentContentCheckResult result)
        {
            var actor = string.IsNullOrEmpty(result.DetectedActor) ? "<ACTOR>" : result.DetectedActor;
            var action = string.IsNullOrEmpty(result.DetectedAction) ? "<ACTION>" : result.DetectedAction;

            return result.RequirementType switch
            {
                "Event-Driven" => $"The {actor} shall {action} when <CONDITION/EVENT>.",
                "Event-Driven + Timing" => $"The {actor} shall {action} when <CONDITION/EVENT> within <TIME>.",
                "State-Driven" => $"The {actor} shall {action} while <STATE/CONDITION>.",
                "State-Driven + Timing" => $"The {actor} shall {action} while <STATE/CONDITION> within <TIME>.",
                "Optional-Feature" => $"The {actor} shall {action} where <FEATURE CONDITION>.",
                "General + Timing" => $"The {actor} shall {action} within <TIME>.",
                _ => $"The {actor} shall {action}."
            };
        }

        /// <summary>
        /// Convert checker results to AnalysisIssue entries for inclusion in LLM RequirementAnalysis.
        /// </summary>
        public List<AnalysisIssue> ToAnalysisIssues(IncoseConsistentContentCheckResult checkResult)
        {
            var issues = new List<AnalysisIssue>();
            foreach (var issue in checkResult.Issues)
            {
                issues.Add(new AnalysisIssue
                {
                    Category = "Consistency",
                    Severity = issue.Severity,
                    Description = $"[INCOSE {issue.Code}] {issue.Description}",
                    Fix = issue.Suggestion
                });
            }
            return issues;
        }
    }

    public class IncoseConsistentContentCheckResult
    {
        public bool Passed { get; set; } = true;
        public string RequirementType { get; set; } = "General";
        public string? DetectedActor { get; set; }
        public string? DetectedAction { get; set; }
        public string CanonicalFormSuggestion { get; set; } = string.Empty;
        public List<IncoseIssue> Issues { get; set; } = new();
    }

    public class IncoseIssue
    {
        public string Code { get; set; } = string.Empty;
        public string Severity { get; set; } = "Medium";
        public string Description { get; set; } = string.Empty;
        public string Suggestion { get; set; } = string.Empty;
    }
}
