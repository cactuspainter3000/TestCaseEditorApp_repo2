using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using System.Text.Json;

namespace TestCaseEditorApp.Services.Prompts
{
    public interface ITextGenerationService
    {
        Task<string> GenerateAsync(string prompt, CancellationToken ct = default);
        Task<string> GenerateWithSystemAsync(string systemMessage, string contextMessage, CancellationToken ct = default);
    }

    public sealed class VerificationPromptInput
    {
        public VerificationPromptInput(
            IReadOnlyList<VerificationMethod> methods,
            string reqId,
            string reqName,
            string reqDescription,
            string? rationale = null,
            string? validationEvidence = null,
            string? supportingNotes = null,
            string? supportingTables = null)
        {
            Methods = methods ?? Array.Empty<VerificationMethod>();
            ReqId = reqId ?? string.Empty;
            ReqName = reqName ?? string.Empty;
            ReqDescription = reqDescription ?? string.Empty;
            Rationale = rationale;
            ValidationEvidence = validationEvidence;
            SupportingNotes = supportingNotes;
            SupportingTables = supportingTables;
        }

        public IReadOnlyList<VerificationMethod> Methods { get; }
        public string ReqId { get; }
        public string ReqName { get; }
        public string ReqDescription { get; }
        public string? Rationale { get; }
        public string? ValidationEvidence { get; }
        public string? SupportingNotes { get; }
        public string? SupportingTables { get; }
    }

    public sealed class VerificationPromptBuilder
    {
        public string Build(VerificationMethod method, string reqId, string reqName, string reqDescription, string? customInstructions = null)
            => Build(new VerificationPromptInput(new[] { method }, reqId, reqName, reqDescription), customInstructions);

        public string Build(IReadOnlyList<VerificationMethod> methods, string reqId, string reqName, string reqDescription, string? customInstructions = null)
            => Build(new VerificationPromptInput(methods, reqId, reqName, reqDescription), customInstructions);

        public string BuildClarifyingWithSuggestedDefaults(
            VerificationPromptInput input,
            IEnumerable<DefaultItem> userSelectedDefaults,
            IEnumerable<DefaultItem> allDefaultsCatalog,
            int budget = 5)
                {
                    var selected = userSelectedDefaults.Select(d => d.Key).ToArray();
                    var catalogSlim = allDefaultsCatalog.Select(d => new { id = d.Key, label = d.Name });

                    var sb = new StringBuilder();
                    sb.AppendLine("You are preparing to draft verification test cases.");
                    sb.AppendLine($"Ask up to {Math.Max(0, Math.Min(7, budget))} concise clarifying questions.");
                    sb.AppendLine("Additionally, from DEFAULTS_CATALOG, return a list of IDs that should be assumed true for this requirement.");
                    sb.AppendLine("Do NOT include any already in USER_SELECTED_DEFAULT_IDS.");
                    sb.AppendLine();
                    sb.AppendLine("REQUIREMENT:");
                    sb.AppendLine(input.ReqDescription ?? "(no description)");
                    sb.AppendLine();
                    sb.AppendLine("USER_SELECTED_DEFAULT_IDS:");
                    sb.AppendLine(JsonSerializer.Serialize(selected));
                    sb.AppendLine();
                    sb.AppendLine("DEFAULTS_CATALOG:");
                    sb.AppendLine(JsonSerializer.Serialize(catalogSlim));
                    sb.AppendLine();
                    sb.AppendLine("Return ONLY JSON with this exact shape:");
                    sb.AppendLine(@"{
          ""must_ask"": [ { ""text"": ""..."", ""why"": ""..."" } ],
          ""nice_to_ask"": [ { ""text"": ""..."", ""why"": ""..."" } ],
          ""suggested_defaults"": [ ""DEF-001"", ""DEF-010"" ]
        }");
            return sb.ToString();
        }

        public string Build(VerificationPromptInput input, string? customInstructions = null)
        {
            var seq = (input.Methods ?? Array.Empty<VerificationMethod>())
                .Where(m => m != VerificationMethod.Unassigned)
                .Distinct()
                .OrderBy(m => (int)m)
                .ToList();

            if (seq.Count == 0)
                seq.Add(VerificationMethod.Unassigned);

            var sb = new StringBuilder();

            // Add custom instructions at the top if provided
            if (!string.IsNullOrWhiteSpace(customInstructions))
            {
                sb.AppendLine("[SPECIAL INSTRUCTIONS FROM USER]");
                sb.AppendLine(customInstructions.Trim());
                sb.AppendLine("[/SPECIAL INSTRUCTIONS]");
                sb.AppendLine();
            }

            var preamble = BuildContextPreamble(
                input.Rationale,
                input.ValidationEvidence,
                input.SupportingNotes,
                input.SupportingTables);

            if (!string.IsNullOrWhiteSpace(preamble))
            {
                sb.AppendLine(preamble.Trim());
                sb.AppendLine();
            }

            for (int i = 0; i < seq.Count; i++)
            {
                var method = seq[i];
                var template = GetTemplate(method)
                    .Replace("<<ReqID>>", input.ReqId)
                    .Replace("<<ReqName>>", input.ReqName)
                    .Replace("<<ReqDescription>>", input.ReqDescription);

                if (i > 0) sb.AppendLine().AppendLine();
                sb.Append(template.Trim());
            }

            sb.AppendLine();
            sb.AppendLine(BuildStrictOutputContract(input.ReqId, input.ReqName));

            return sb.ToString();
        }

        private static string BuildContextPreamble(
            string? rationale,
            string? validationEvidence,
            string? supportingNotes,
            string? supportingTables)
        {
            bool hasR = !string.IsNullOrWhiteSpace(rationale);
            bool hasE = !string.IsNullOrWhiteSpace(validationEvidence);
            bool hasN = !string.IsNullOrWhiteSpace(supportingNotes);
            bool hasT = !string.IsNullOrWhiteSpace(supportingTables);

            string r = hasR ? rationale!.Trim() : "(none)";
            string e = hasE ? validationEvidence!.Trim() : "(none)";
            string n = hasN ? supportingNotes!.Trim() : "(none)";
            string t = hasT ? supportingTables!.Trim() : "(none)";

            var sb = new StringBuilder();

            if (hasR || hasE || hasN || hasT)
            {
                sb.AppendLine("[CONTEXT — DO NOT OUTPUT THIS SECTION]");
                sb.AppendLine("The following text was imported from the requirement record. It may be useful background but is not authoritative for test steps or pass/fail criteria. Use it only if it directly supports the requirement; otherwise ignore it.");
                sb.AppendLine();
                sb.AppendLine("Rationale (may or may not be viable):");
                sb.AppendLine(r);
                sb.AppendLine();
                sb.AppendLine("Validation Evidence (may or may not be viable):");
                sb.AppendLine(e);
                sb.AppendLine();
                sb.AppendLine("Supporting Notes (may or may not be viable):");
                sb.AppendLine(n);
                sb.AppendLine();
                sb.AppendLine("Supporting Tables (may or may not be viable):");
                sb.AppendLine(t);
                sb.AppendLine();
                sb.AppendLine("Instructions: Do not repeat or summarize this context in your output. Base all testable content on the requirement text and the method template(s). If the context conflicts with the requirement, prefer the requirement and the template.");
                sb.AppendLine("[/CONTEXT]");
                sb.AppendLine();
            }

            sb.AppendLine("Before drafting the test case, check whether any information is missing or ambiguous.");
            sb.AppendLine("If clarification is needed, respond ONLY with:");
            sb.AppendLine();
            sb.AppendLine("CLARIFY:");
            sb.AppendLine("1) <concise question>");
            sb.AppendLine("2) <concise question>");
            sb.AppendLine("(…up to 5 total)");
            sb.AppendLine();
            sb.AppendLine("STOP.");
            sb.AppendLine();
            sb.AppendLine("Do not generate the test case yet.");
            sb.AppendLine();
            sb.AppendLine("If no clarification is needed, respond with:");
            sb.AppendLine("CLARIFY: None");
            sb.AppendLine();
            sb.AppendLine("Then immediately produce the test case per the required output format (Strict Output Contract).");
            sb.AppendLine("If you wrote 'CLARIFY: None', you MUST immediately output the strict table per the contract below.");
            return sb.ToString();
        }

        // Replace the existing BuildStrictOutputContract method with this version
        private static string BuildStrictOutputContract(string reqId, string reqName)
        {
            string name = string.IsNullOrWhiteSpace(reqId)
                ? (string.IsNullOrWhiteSpace(reqName) ? "Untitled Test" : reqName)
                : (string.IsNullOrWhiteSpace(reqName) ? reqId : $"{reqId}: {reqName}");

            var sb = new StringBuilder();

            sb.AppendLine("OUTPUT FORMAT (REQUIRED — MACHINE READABLE)");
            sb.AppendLine("- Return JSON ONLY. Do NOT include any extra explanation, markdown, or surrounding text.");
            sb.AppendLine("- Return exactly one JSON array of TestCase objects (may be empty: []).");
            sb.AppendLine("- Each TestCase object MUST include:");
            sb.AppendLine("  - \"Name\": string");
            sb.AppendLine("  - \"Steps\": array of Step objects (must contain at least one object)");
            sb.AppendLine("- Each Step object MUST include:");
            sb.AppendLine("  - \"Action\": string");
            sb.AppendLine("  - \"Expected\": string");
            sb.AppendLine("  - \"Method\": string (optional)");
            sb.AppendLine("  - \"Notes\": string (optional)");
            sb.AppendLine();
            sb.AppendLine("Rules:");
            sb.AppendLine("- Do NOT include markdown, code fences, or commentary. Output must be valid JSON parseable by a standard JSON parser.");
            sb.AppendLine("- If any string contains quotes or special characters, escape them according to JSON rules.");
            sb.AppendLine("- If you cannot produce test cases, return an empty array: []");
            sb.AppendLine();
            sb.AppendLine("Example output (one TestCase, two Steps):");
            sb.AppendLine(@"[");
            sb.AppendLine(@"  {");
            sb.AppendLine($@"    ""Name"": ""{EscapeForInlineJson(name)}"",");
            sb.AppendLine(@"    ""Steps"": [");
            sb.AppendLine(@"      { ""Action"": ""Retrieve and review Test Equipment Safety Checklist (074-8434-282)."", ""Expected"": ""Checklist is obtained and reviewed for compliance."", ""Method"": ""Inspection"", ""Notes"": """" },");
            sb.AppendLine(@"      { ""Action"": ""Identify all listed safety requirements in the checklist."", ""Expected"": ""List of protection measures identified."", ""Method"": ""Inspection"", ""Notes"": """" }");
            sb.AppendLine(@"    ]");
            sb.AppendLine(@"  }");
            sb.AppendLine(@"]");
            sb.AppendLine();
            sb.AppendLine($"NOTE: Test Case Name default: \"{name}\" (you may override Name inside the JSON).");

            return sb.ToString();
        }

        // Small helper to escape the requirement name for the example inline JSON above.
        // Keeps the example safe if the name contains quotes.
        private static string EscapeForInlineJson(string s)
        {
            if (s == null) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string GetTemplate(VerificationMethod method) => method switch
        {
            VerificationMethod.Test => VerificationPromptTemplates.Test,
            VerificationMethod.TestUnintendedFunction => VerificationPromptTemplates.TestUnintendedFunction,
            VerificationMethod.Demonstration => VerificationPromptTemplates.Demonstration,
            VerificationMethod.Inspection => VerificationPromptTemplates.Inspection,
            VerificationMethod.Analysis => VerificationPromptTemplates.Analysis,
            VerificationMethod.Simulation => VerificationPromptTemplates.Simulation,
            VerificationMethod.ServiceHistory => VerificationPromptTemplates.ServiceHistory,
            VerificationMethod.VerifiedAtAnotherLevel => VerificationPromptTemplates.VerifiedAtAnotherLevel,
            _ => VerificationPromptTemplates.Unassigned
        };
    }

    public static class VerificationPromptTemplates
    {
        public static readonly string Test = @"
You are drafting a Verification Case for **Test** using the exact structure below.
Output ONLY the sections below, in this exact order and wording. No extra prose.

**Objective:**
One sentence beginning with ""To verify..."" reflecting requirement <<ReqID>>/<<ReqName>>: <<ReqDescription>>.

**Environment / Mode of Operation / Configuration:**
State ambient conditions, operational mode, and UUT configuration (e.g., ""Standard Ambient Conditions"", ""ATP Mode"", etc.).

**Input:**
List the parameters/fixtures/tools the verifier sets/applies.

**Expected Output(s):**
Describe the measurable values/observations tied to pass/fail with units/tolerances.

After the sections above, output exactly the following lines in plain text (NOT a markdown table):
Test Case Name: <<ReqID>>: <<ReqName>>
Step # | Step Action | Step Expected Result | Name
(Provide 3–10 step rows.)
Use ASCII pipes | and exactly four columns in that order. Do not add any other text before or after.
";

        public static readonly string TestUnintendedFunction = @"
You are drafting a Verification Case for **Test for Unintended Function (TUF)** using the exact structure below.
Output ONLY the sections below, in this exact order and wording. No extra prose.

**Objective:**
One sentence beginning with ""To verify..."" reflecting requirement <<ReqID>>/<<ReqName>>: <<ReqDescription>>.

**Environment / Mode of Operation / Configuration:**
State ambient conditions, operational mode, and UUT configuration.

**Input:**
List the fault-injection/misuse/boundary stimuli and any tools/fixtures required.

**Expected Output(s):**
Each expected result must confirm the absence of unintended behavior and that the system remains safe.

After the sections above, output exactly the following lines in plain text (NOT a markdown table). Provide 4–10 rows covering, at minimum:
- normal vs abnormal scenarios,
- incorrect control use,
- failure of primary functions,
- simultaneous controls,
- power/reversion/bus-failure responses.

Test Case Name: <<ReqID>>: <<ReqName>>
Step # | Step Action | Step Expected Result | Name
";

        public static readonly string Demonstration = @"
You are drafting a Verification Case for **Demonstration** using the exact structure below.
Output ONLY the sections below, in this exact order and wording. No extra prose.

**Objective:**
One sentence beginning with ""To verify..."" reflecting requirement <<ReqID>>/<<ReqName>>: <<ReqDescription>>.

**Environment / Mode of Operation / Configuration:**
State the ambient conditions, operational mode, and/or UUT configuration.

**Demonstration Procedure:**
Provide 3–6 bulleted imperative steps (install, mount, observe, etc.) that exercise the requirement qualitatively.

**Pass Criteria:**
Provide 3–6 bulleted observable outcomes phrased as ""It is observed that..."" or equivalent, showing clear pass/fail cues.
";

        public static readonly string Inspection = @"
You are drafting a Verification Case for **Inspection** using the exact structure below.
Output ONLY the sections below, in this exact order and wording. No extra prose.

**Objective:**
To verify ... (one sentence) reflecting requirement <<ReqID>>/<<ReqName>>: <<ReqDescription>>.

**Item Inspected:**
Name the artifact or item (e.g., ""Test Datasheet"", ""ICD <ID Rev>"", ""Drawing <ID Rev>"").

**Inspection Criteria:**
Provide 3–8 atomic bullet criteria phrased as includes/matches/confirms statements that directly satisfy the requirement.
";

        public static readonly string Analysis = @"
You are drafting a Verification Case for **Analysis** using the exact structure below.
Output ONLY the sections below, in this exact order and wording. No extra prose.

**Objective:**
One sentence beginning with ""To verify..."" reflecting requirement <<ReqID>>/<<ReqName>>: <<ReqDescription>>.

**Analysis to be Performed:**
1–3 sentences describing the analytical examination (what will be compared/derived/calculated).

**Expected Outputs:**
2–4 bullets describing tangible artifacts (e.g., summary of analysis, derived values/plots, coverage table).

(Optional if similarity is used)
**Similarity Basis:**
State similarity in design/manufacture/use, spec equivalence/stricter, and environment identical/less rigorous.
";

        public static readonly string Simulation = @"
You are drafting a Verification Case for **Simulation** using the exact structure below.
Output ONLY the sections below, in this exact order and wording. No extra prose.

**Objective:**
One sentence beginning with ""To verify..."" reflecting requirement <<ReqID>>/<<ReqName>>: <<ReqDescription>>.

**Simulation to be Performed:**
1–3 sentences describing the simulation setup (model/tool/topology), stimuli, parameters, and conditions to be exercised.

**Expected Outputs:**
2–4 bullets describing tangible artifacts (e.g., run logs vs limits, plots, parameter-sweep tables).

(Optional)
**Scenarios:**
Provide 3–6 brief bullet scenarios or a short list of corner/sensitivity cases.
";

        public static readonly string ServiceHistory = @"
You are drafting a Verification Case for **Service History** using the exact structure below.
Output ONLY the sections below, in this exact order and wording. No extra prose.

**Objective:**
One sentence beginning with ""To verify..."" reflecting requirement <<ReqID>>/<<ReqName>>: <<ReqDescription>>.

**Comparable System / Item Inspected:**
Identify the previously fielded system being used for comparison (e.g., ""Legacy TPS <ID>"", ""System <ID Rev> with >N operating hours"").

**Service History Criteria:**
Provide 3–6 bullets that establish comparability, such as:
• Similar design/manufacture/use and environment
• Operating hours and failure rates meet/exceed thresholds
• No significant unresolved failures recorded
• Differences identified and mitigations defined

**Expected Outputs:**
1–3 concise statements (e.g., documented evidence supporting compliance; engineering judgment summary confirming no unresolved failures).
";

        public static readonly string VerifiedAtAnotherLevel = @"
You are drafting a Verification Case for **Verified at Another Level** using the exact structure below.
Output ONLY the sections below, in this exact order and wording. No extra prose.

**Objective:**
One sentence beginning with ""To verify..."" reflecting requirement <<ReqID>>/<<ReqName>>: <<ReqDescription>>.

**Source Verification Level:**
Identify the level where verification is credited (e.g., ""Customer-level requirement <ID>"", ""Low-level SW requirement <ID>"").

**Linked Test/Report IDs:**
Provide a short table with the following columns and 1–3 rows:
Level | Item/Test ID | Report ID/Rev | Coverage Notes

**Coverage Statement:**
1–2 sentences stating whether the upstream/downstream verification fully or partially covers this requirement (describe scope if partial).

**Credit Statement:**
This requirement is verified by credit to <source level> verification, as documented in <report IDs>.
";

        public static readonly string Unassigned = @"
You are assisting with method selection. Output ONLY the following:

**Recommended Verification Method:**
Name a single method and give a one-sentence rationale.

Then immediately produce the corresponding artifact using that method’s exact template and headings for requirement <<ReqID>>/<<ReqName>>: <<ReqDescription>>.
";
    }
}
