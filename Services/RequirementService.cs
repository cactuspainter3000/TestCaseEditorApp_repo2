// RequirementService.cs
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using TestCaseEditorApp.Import;
using TestCaseEditorApp.MVVM.Models;
using VMVerMethod = TestCaseEditorApp.MVVM.Models.VerificationMethod;
// Alias Word table to avoid confusion with Spreadsheet types
using WTable = DocumentFormat.OpenXml.Wordprocessing.Table;


namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Pure data service: parses Word/Excel into models.
    /// No ViewModel types, no editor callbacks.
    /// </summary>
    public class RequirementService : IRequirementService
    {
        // ─────────────────────────────────────────────
        // ───────────── DOCX IMPORTS ─────────────────
        // ─────────────────────────────────────────────

        public List<Requirement> ImportRequirementsFromJamaAllDataDocx(string path)
        {
            try
            {
                EnsureDocx(path);
                return JamaAllDataDocxParser.Parse(path);
            }
            catch (NotSupportedException nse)
            {
                MessageBox.Show(nse.Message, "Unsupported file", MessageBoxButton.OK, MessageBoxImage.Warning);
                return new List<Requirement>();
            }
            catch (IOException ioex) when (IsSharingViolation(ioex))
            {
                var file = System.IO.Path.GetFileName(path);
                MessageBox.Show(
                    $"I can’t read '{file}' because it’s open in another app (Word, Preview, or a sync/indexer).\n\n" +
                    $"Please close the file and try again.",
                    "Close the file and retry",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return new List<Requirement>();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return new List<Requirement>();
            }
        }

        public List<Requirement> ImportRequirementsFromWord(string path) => ParseDocxRequirements(path);

        private static void EnsureDocx(string path)
        {
            var ext = Path.GetExtension(path);
            if (!ext.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Please export/save the Jama report as a Word .docx file (not .doc).");
        }

        private static bool IsSharingViolation(IOException ex)
            => ex.HResult == unchecked((int)0x80070020);

        private static readonly Regex ReqRcTokenRegex =
            new(@"\b([A-Za-z0-9]+-REQ_RC-\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static bool IsTocParagraph(Paragraph p) =>
            p.Descendants<FieldCode>().Any(fc => fc.InnerText.Contains("TOC", StringComparison.OrdinalIgnoreCase));

        private static string StripLeadingSectionNumbers(string s) =>
            Regex.Replace(s ?? string.Empty, @"^\s*\d+(\.\d+)*\s+", "");

        private static bool TryParseHeaderText(string text, out string item, out string name)
        {
            item = string.Empty;
            name = string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return false;

            // Legacy
            if (text.Contains("StdTestReq-", StringComparison.OrdinalIgnoreCase))
            {
                var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var it = parts.FirstOrDefault(part => part.StartsWith("StdTestReq-", StringComparison.OrdinalIgnoreCase));
                if (it != null)
                {
                    item = it.Trim();
                    int idx = Array.IndexOf(parts, it);
                    name = string.Join(" ", parts.Skip(idx + 1)).Trim();
                    name = StripLeadingSectionNumbers(name);
                    return true;
                }
            }

            // Jama: PREFIX-REQ_RC-<digits>
            var m = ReqRcTokenRegex.Match(text);
            if (m.Success)
            {
                item = m.Groups[1].Value.Trim();

                var after = text.Substring(m.Index + m.Length).Trim(' ', '-', '–', ':');

                // OPTIONAL: grab heading number if you want to set r.Heading later
                // var headingMatch = System.Text.RegularExpressions.Regex.Match(after, @"^\d+(\.\d+)*");
                // var headingNumber = headingMatch.Success ? headingMatch.Value : null;

                if (string.IsNullOrWhiteSpace(after))
                {
                    // Fallback: remove the item token only; DO NOT strip section numbering
                    var noItem = text.Replace(item, "", StringComparison.OrdinalIgnoreCase);
                    after = noItem.Trim(' ', '-', '–', ':');
                }

                name = after; // ← keep "3.1.1 Hardware Interfaces"

                return true;
            }

            return false;
        }

        private List<Requirement> ParseDocxRequirements(string path)
        {
            var requirements = new List<Requirement>();

            using (var wordDoc = WordprocessingDocument.Open(path, false))
            {
                Body? body = wordDoc.MainDocumentPart?.Document?.Body;
                if (body == null) return requirements;

                var elements = body.Elements().ToList();

                for (int i = 0; i < elements.Count; i++)
                {
                    var element = elements[i];

                    // Skip TOC pieces
                    if (element is Paragraph maybeToc && IsTocParagraph(maybeToc))
                        continue;

                    // Requirement header?
                    if (element is Paragraph pHeader)
                    {
                        var headerText = pHeader.InnerText?.Trim() ?? string.Empty;

                        if (TryParseHeaderText(headerText, out var item, out var name))
                        {
                            var candidate = new Requirement
                            {
                                Item = item,
                                Name = name
                            };

                            ExtractRequirementInfo(body, candidate, ref i);

                            if (!string.IsNullOrEmpty(candidate.Item) || !string.IsNullOrEmpty(candidate.Name))
                                requirements.Add(candidate);

                            continue;
                        }
                    }

                    // Standalone meta tables near the last requirement
                    if (element is WTable table && requirements.Count > 0)
                    {
                        var current = requirements.Last();
                        TryParseMetaTables(body, current, ref i);
                    }
                }

                // Assign loose content between structured sections
                AssignLooseContentBetweenRequirements(body, requirements);
            }

            return requirements;
        }

        // Detects and consumes the requirement metadata table (2 columns with known keys).
        private bool TryParseMetaTables(Body body, Requirement requirement, ref int index)
        {
            var elements = body.Elements().ToList();
            if (index >= elements.Count || elements[index] is not WTable table) return false;

            var rows = table.Elements<TableRow>()
                .Select(r => r.Elements<TableCell>().Select(c => c.InnerText.Trim()).ToList())
                .ToList();

            // Must be 2-col and have recognized keys
            if (rows.Count == 0 || rows.Any(r => r.Count != 2)) return false;

            var knownKeys = new[]
            {
                "Item Type", "Status", "Locked", "ID", "Requirement Type", "Name",
                "DOORS ID", "Created Date", "Change Driver", "Allocation/s",
                "Derived Requirement", "Rationale",
                "Safety Requirement", "Safety Rationale",
                "Security Requirement", "Security Rationale",
                "Statement of Compliance",
                "Verification Method", "Verification Method/s",
                "Validation Method", "Validation Methods",
                "Validation Evidence", "Validation Conclusion",
                "Robust Requirement", "Robust Rationale",
                "Upstream Cross Instance Relationships", "Upstream Relationship",
                "Export Controlled", "Customer ID", "Project Defined",
                "Folder Path", "Set Name", "Set ID"
            };

            int matchCount = rows.Count(r =>
                knownKeys.Any(k => r[0].StartsWith(k, StringComparison.OrdinalIgnoreCase)));

            if (matchCount < 5) return false;

            foreach (var row in rows)
            {
                string key = row[0].Trim();
                string value = row[1];

                if (key.StartsWith("Status", StringComparison.OrdinalIgnoreCase)) requirement.Status = value;
                else if (key.StartsWith("Requirement Type", StringComparison.OrdinalIgnoreCase)) requirement.RequirementType = value;
                else if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) requirement.Name = value;
                else if (key.Equals("ID", StringComparison.OrdinalIgnoreCase)) requirement.Item ??= value;
                else if (key.StartsWith("Validation Method", StringComparison.OrdinalIgnoreCase)) requirement.ValidationMethodRaw = value ?? string.Empty;
                else if (key.StartsWith("Validation Evidence", StringComparison.OrdinalIgnoreCase)) requirement.ValidationEvidence = value;
                else if (key.StartsWith("Validation Conclusion", StringComparison.OrdinalIgnoreCase)) requirement.ValidationConclusion = value;
                else if (key.StartsWith("Verification Method", StringComparison.OrdinalIgnoreCase)) requirement.VerificationMethodRaw = value;
                else if (key.StartsWith("Safety Requirement", StringComparison.OrdinalIgnoreCase)) requirement.SafetyRequirement = value;
                else if (key.StartsWith("Safety Rationale", StringComparison.OrdinalIgnoreCase)) requirement.SafetyRationale = value;
                else if (key.StartsWith("Security Requirement", StringComparison.OrdinalIgnoreCase)) requirement.SecurityRequirement = value;
                else if (key.StartsWith("Security Rationale", StringComparison.OrdinalIgnoreCase)) requirement.SecurityRationale = value;
                else if (key.StartsWith("Statement of Compliance", StringComparison.OrdinalIgnoreCase)) requirement.StatementOfCompliance = value;
                else if (key.StartsWith("Robust Requirement", StringComparison.OrdinalIgnoreCase)) requirement.RobustRequirement = value;
                else if (key.StartsWith("Robust Rationale", StringComparison.OrdinalIgnoreCase)) requirement.RobustRationale = value;
                else if (key.StartsWith("Folder Path", StringComparison.OrdinalIgnoreCase)) requirement.FolderPath = value;
                else if (key.StartsWith("Set Name", StringComparison.OrdinalIgnoreCase)) requirement.SetName = value;
                else if (key.Equals("Set ID", StringComparison.OrdinalIgnoreCase)) requirement.SetId = value;
                else if (key.StartsWith("Upstream Cross Instance Relationships", StringComparison.OrdinalIgnoreCase) ||
                         key.StartsWith("Upstream Relationship", StringComparison.OrdinalIgnoreCase)) requirement.UpstreamCrossInstanceRelationships = value;
            }

            // Claim/consume this table so it won't be treated as a loose table later
            requirement.ConsumedElements.Add(table);
            return true;
        }

        // Extracts header, description, and metadata only. Defers ALL loose content to AssignLooseContentBetweenRequirements.
        private void ExtractRequirementInfo(Body body, Requirement requirement, ref int index)
        {
            var elements = body.Elements().ToList();
            int startIndex = index;

            requirement.Description = string.Empty;
            requirement.ConsumedElements.Clear();

            for (; index < elements.Count; index++)
            {
                var el = elements[index];

                // Stop when we hit the next requirement header
                if (el is Paragraph maybeNext && index != startIndex &&
                    IsRequirementHeaderText(maybeNext.InnerText?.Trim() ?? string.Empty))
                {
                    break;
                }

                // Header line (Item + Name)
                if (index == startIndex && el is Paragraph pHead)
                {
                    var headerText = pHead.InnerText?.Trim() ?? string.Empty;
                    if (TryParseHeaderText(headerText, out var item, out var name))
                    {
                        requirement.Item = item;
                        requirement.Name = name;

                        if (!requirement.ConsumedElements.Contains(pHead))
                            requirement.ConsumedElements.Add(pHead);
                    }
                }

                // Description: prefer shall/must/will/should; else first meaningful paragraph
                if (el is Paragraph descPara)
                {
                    var txt = descPara.InnerText?.Trim();
                    if (!string.IsNullOrWhiteSpace(txt))
                    {
                        if (Regex.IsMatch(txt, @"\b(shall|must|will|should)\b", RegexOptions.IgnoreCase))
                        {
                            requirement.Description = txt;
                        }
                        else if (string.IsNullOrWhiteSpace(requirement.Description))
                        {
                            requirement.Description = txt; // tentative fallback
                        }
                    }
                }

                // Metadata table (claim before loose content sees it)
                if (el is WTable _ && TryParseMetaTables(body, requirement, ref index))
                {
                    // consumed in TryParseMetaTables
                }
            }

            // Step back one so the outer loop reprocesses current element
            index--;

            requirement.Description ??= string.Empty;
        }

        /// <summary>
        /// Assigns loose paragraphs and tables that appear between each requirement's
        /// consumed content and the next requirement. Excludes elements already marked as consumed,
        /// de-dups paragraphs and tables, and stores them in req.LooseContent.
        /// </summary>
        private void AssignLooseContentBetweenRequirements(Body body, List<Requirement> requirements)
        {
            var elements = body.Elements().ToList();

            for (int r = 0; r < requirements.Count; r++)
            {
                var req = requirements[r];
                if (req.ConsumedElements == null || req.ConsumedElements.Count == 0) continue;

                // Prefer the actual header paragraph as the start anchor
                var startAnchor = req.ConsumedElements
                    .OfType<Paragraph>()
                    .FirstOrDefault(p => IsRequirementHeaderText(p.InnerText?.Trim() ?? string.Empty))
                    ?? req.ConsumedElements.FirstOrDefault();

                // If we still can't find a start anchor in the body, skip gracefully
                int start = (startAnchor != null) ? elements.IndexOf(startAnchor) : -1;
                if (start < 0) continue;

                int end = (r < requirements.Count - 1 && requirements[r + 1].ConsumedElements.Count > 0)
                    ? elements.IndexOf(requirements[r + 1].ConsumedElements.First())
                    : elements.Count;

                var looseParagraphs = new List<string>();
                var looseTables = new List<LooseTable>();

                // Fast lookup of consumed elements to avoid re-capturing description/meta blocks
                var consumedSet = new HashSet<OpenXmlElement>(req.ConsumedElements);

                for (int i = start + 1; i < end; i++)
                {
                    var el = elements[i];

                    // Skip anything we already marked as consumed for this requirement
                    if (consumedSet.Contains(el)) continue;

                    if (el is Paragraph para)
                    {
                        var t = para.InnerText?.Trim();
                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            // Include ALL non-empty paragraphs (not just list items).
                            // Also avoid re-adding the requirement header text.
                            if (!IsRequirementHeaderText(t))
                                looseParagraphs.Add(t);
                        }
                    }
                    else if (el is WTable tbl)
                    {
                        var rows = tbl.Elements<TableRow>()
                                      .Select(rw => rw.Elements<TableCell>()
                                                      .Select(c => (c.InnerText ?? string.Empty).Trim())
                                                      .ToList())
                                      .Where(rw => rw.Count > 0)
                                      .ToList();

                        if (rows.Count > 0)
                        {
                            string? title = TryGetTableTitle(elements, i);
                            looseTables.Add(new LooseTable
                            {
                                EditableTitle = title ?? $"Untitled Table {looseTables.Count + 1}",
                                Rows = rows
                            });
                        }
                    }
                }

                // De-dup paragraphs (text-based) and tables (shape/data-based)
                var desc = (req.Description ?? string.Empty).Trim();
                var uniqueParas = looseParagraphs
                    .Select(s => s?.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.Ordinal)
                    .Where(p => !string.Equals(p, desc, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var distinctTables = new List<LooseTable>();
                foreach (var table in looseTables)
                {
                    bool exists = distinctTables.Any(existing =>
                        existing.Rows.Count == table.Rows.Count &&
                        existing.Rows.Zip(table.Rows, (a, b) => a.SequenceEqual(b, StringComparer.Ordinal)).All(eq => eq));
                    if (!exists) distinctTables.Add(table);
                }

                req.LooseContent ??= new RequirementLooseContent();
                req.LooseContent.Paragraphs = uniqueParas;
                req.LooseContent.Tables = distinctTables;
            }
        }

        private static string? TryGetTableTitle(List<OpenXmlElement> elements, int tableIndex)
        {
            if (tableIndex <= 0) return null;

            if (elements[tableIndex - 1] is Paragraph p)
            {
                var candidate = p.InnerText?.Trim();
                if (string.IsNullOrWhiteSpace(candidate)) return null;

                // Never take a list item as a title
                if (IsListParagraph(p)) return null;

                // Heuristics
                if (candidate.Contains("Note", StringComparison.OrdinalIgnoreCase)) return null;
                if (candidate.Length > 90) return null;
                int periods = candidate.Count(c => c == '.');
                if (periods > 1) return null;

                return candidate.TrimEnd(':').Trim();
            }

            return null;
        }

        private static bool IsListParagraph(Paragraph p)
        {
            var pp = p.ParagraphProperties;
            return pp?.NumberingProperties != null;
        }

        private static bool IsRequirementHeaderText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Contains("StdTestReq-", StringComparison.OrdinalIgnoreCase)) return true;
            return ReqRcTokenRegex.IsMatch(text);
        }

        // ─────────────────────────────────────────────
        // ───────────── EXPORT / STEPS ───────────────
        // ─────────────────────────────────────────────

        // Unified step row used for all methods
        public sealed class StepRow
        {
            public string Number { get; set; } = "";
            public string Action { get; set; } = "";
            public string Expected { get; set; } = "";
            public string Name { get; set; } = "";
        }

        // Detect which method blocks are present in a draft output
        private static IReadOnlyList<string> DetectMethodsInOutput(string output)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var t = (output ?? string.Empty);

            // Table => Test (or TUF) — default to Test; TUF detected by heading
            if (Regex.IsMatch(t, @"\|\s*Step\s*#\s*\|\s*Step\s*Action\s*\|\s*Step\s*Expected\s*Result\s*\|\s*Name\s*\|", RegexOptions.IgnoreCase))
                set.Add("Test");

            // Headings from templates
            if (t.IndexOf("Verification Case for **Test for Unintended Function", StringComparison.OrdinalIgnoreCase) >= 0)
                set.Add("TestUnintendedFunction");

            if (t.IndexOf("Verification Case for **Demonstration**", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("**Demonstration Procedure:**", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("**Pass Criteria:**", StringComparison.OrdinalIgnoreCase) >= 0)
                set.Add("Demonstration");

            if (t.IndexOf("Verification Case for **Inspection**", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("**Inspection Criteria:**", StringComparison.OrdinalIgnoreCase) >= 0)
                set.Add("Inspection");

            if (t.IndexOf("Verification Case for **Analysis**", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("**Analysis to be Performed:**", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("**Expected Outputs:**", StringComparison.OrdinalIgnoreCase) >= 0)
                set.Add("Analysis");

            if (t.IndexOf("Verification Case for **Simulation**", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("**Simulation to be Performed:**", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("**Scenarios:**", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("**Expected Outputs:**", StringComparison.OrdinalIgnoreCase) >= 0)
                set.Add("Simulation");

            if (t.IndexOf("Verification Case for **Service History**", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("**Service History Criteria:**", StringComparison.OrdinalIgnoreCase) >= 0)
                set.Add("ServiceHistory");

            if (t.IndexOf("Verification Case for **Verified at Another Level**", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("**Linked Test/Report IDs:**", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("**Credit Statement:**", StringComparison.OrdinalIgnoreCase) >= 0)
                set.Add("VerifiedAtAnotherLevel");

            if (set.Count == 0) set.Add("Test"); // safe default
            return set.ToList();
        }

        // Slice helpers for method sections
        private static string ExtractSectionForMethodOrWhole(string output, string method)
        {
            if (method.Equals("Demonstration", StringComparison.OrdinalIgnoreCase))
            {
                var sec = SliceBetween(output, "**Demonstration Procedure:**", "**Pass Criteria:**");
                if (!string.IsNullOrWhiteSpace(sec)) return sec;
            }
            else if (method.Equals("Inspection", StringComparison.OrdinalIgnoreCase))
            {
                var sec = SliceFrom(output, "**Inspection Criteria:**");
                if (!string.IsNullOrWhiteSpace(sec)) return sec;
            }
            // Test & others: table/bullets scan over whole output is fine
            return output;
        }

        private static string SliceBetween(string text, string startMarker, string endMarker)
        {
            var i = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return string.Empty;
            i += startMarker.Length;
            var j = text.IndexOf(endMarker, i, StringComparison.OrdinalIgnoreCase);
            if (j < 0) j = text.Length;
            return text.Substring(i, j - i).Trim();
        }

        private static string SliceFrom(string text, string startMarker)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var i = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return string.Empty;
            i += startMarker.Length;
            return text.Substring(i).Trim();
        }

        // Objective extractor
        private static string ExtractObjective(string sectionOrWhole)
        {
            var m = Regex.Match(sectionOrWhole, @"(?is)\*\*Objective:\*\*\s*(.+?)(?:\r?\n\s*\r?\n|$)");
            return m.Success ? CleanLine(m.Groups[1].Value) : string.Empty;
        }

        private static string CleanLine(string s)
        {
            s = Regex.Replace(s, @"\s+", " ");
            return s.Trim();
        }

        // Markdown step table → StepRow list
        private static List<StepRow> ParseMarkdownSteps(string text)
        {
            var steps = new List<StepRow>();
            if (string.IsNullOrWhiteSpace(text)) return steps;

            var lines = text.Replace("\r", "").Split('\n');

            // Find the header (with or without leading pipe)
            int headerIdx = Array.FindIndex(lines, l =>
                Regex.IsMatch(l, @"(?i)^\s*\|?\s*Step\s*#\s*\|\s*Step\s*Action\s*\|\s*Step\s*Expected\s*Result\s*\|\s*Name\s*\|?\s*$"));

            if (headerIdx < 0) return steps;

            for (int i = headerIdx + 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.Contains("|")) break; // first non-table line
                if (line.StartsWith("**")) break; // next section heading

                // Eat separator row if present
                if (Regex.IsMatch(line, @"^\s*\|?\s*-{2,}\s*\|\s*-{2,}\s*\|\s*-{2,}\s*\|\s*-{2,}\s*\|?\s*$"))
                    continue;

                var parts = line.Split('|').Select(c => c.Trim()).ToList();
                if (parts.Count == 0) continue;

                if (parts.First() == "") parts.RemoveAt(0);
                if (parts.Count > 0 && parts.Last() == "") parts.RemoveAt(parts.Count - 1);
                if (parts.Count < 4) continue;

                string number = parts[0];
                string action = parts[1];
                string expected = parts[2];
                string name = parts[3];

                // Normalize number: keep digits if present
                var numDigits = Regex.Replace(number ?? "", @"[^\d]", "");
                if (!string.IsNullOrEmpty(numDigits)) number = numDigits;

                steps.Add(new StepRow
                {
                    Number = number,
                    Action = action,
                    Expected = expected,
                    Name = name
                });
            }

            return steps;
        }

        // Bullets extractor for various sections
        private static List<string> ExtractBullets(string text)
        {
            var list = new List<string>();
            foreach (Match m in Regex.Matches(text ?? "", @"(?m)^\s*[-*]\s+(.*)$"))
            {
                var s = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
            }
            return list;
        }

        // Per-method step builders (for non-table methods)
        private static List<StepRow> ParseDemonstrationSteps(string sectionOrWhole)
        {
            var proc = SliceFrom(sectionOrWhole, "**Demonstration Procedure:**");
            var pass = SliceFrom(sectionOrWhole, "**Pass Criteria:**");
            var procBullets = ExtractBullets(proc);
            var passBullets = ExtractBullets(pass);

            var rows = new List<StepRow>();
            int n = Math.Max(procBullets.Count, passBullets.Count);
            for (int i = 0; i < n; i++)
            {
                rows.Add(new StepRow
                {
                    Number = (i + 1).ToString(),
                    Action = i < procBullets.Count ? procBullets[i] : "",
                    Expected = i < passBullets.Count ? passBullets[i] : "",
                    Name = ""
                });
            }
            return rows;
        }

        private static List<StepRow> ParseInspectionSteps(string sectionOrWhole)
        {
            var crit = SliceFrom(sectionOrWhole, "**Inspection Criteria:**");
            var bullets = ExtractBullets(crit);
            var rows = new List<StepRow>();
            for (int i = 0; i < bullets.Count; i++)
            {
                rows.Add(new StepRow
                {
                    Number = (i + 1).ToString(),
                    Action = bullets[i],
                    Expected = "Criterion satisfied",
                    Name = ""
                });
            }
            return rows;
        }

        private static List<StepRow> ParseAnalysisSteps(string sectionOrWhole)
        {
            var exp = SliceFrom(sectionOrWhole, "**Expected Outputs:**");
            var bullets = ExtractBullets(exp);
            if (bullets.Count == 0)
            {
                return new List<StepRow> {
                    new StepRow{ Number="1", Action="Perform analysis per plan", Expected="Expected outputs documented", Name="" }
                };
            }
            var rows = new List<StepRow>();
            for (int i = 0; i < bullets.Count; i++)
            {
                rows.Add(new StepRow
                {
                    Number = (i + 1).ToString(),
                    Action = "Produce & review: " + bullets[i],
                    Expected = "Meets acceptance limits / rationale documented",
                    Name = ""
                });
            }
            return rows;
        }

        private static List<StepRow> ParseSimulationSteps(string sectionOrWhole)
        {
            var exp = ExtractBullets(SliceFrom(sectionOrWhole, "**Expected Outputs:**"));
            if (exp.Count > 0)
            {
                var rows = new List<StepRow>();
                for (int i = 0; i < exp.Count; i++)
                {
                    rows.Add(new StepRow
                    {
                        Number = (i + 1).ToString(),
                        Action = "Run simulation and capture: " + exp[i],
                        Expected = "Results within specified limits",
                        Name = ""
                    });
                }
                return rows;
            }
            var scen = ExtractBullets(SliceFrom(sectionOrWhole, "**Scenarios:**"));
            if (scen.Count > 0)
            {
                var rows = new List<StepRow>();
                for (int i = 0; i < scen.Count; i++)
                {
                    rows.Add(new StepRow
                    {
                        Number = (i + 1).ToString(),
                        Action = "Execute scenario: " + scen[i],
                        Expected = "Sim results meet requirement acceptance criteria",
                        Name = ""
                    });
                }
                return rows;
            }
            return new List<StepRow> {
                new StepRow{ Number="1", Action="Execute defined simulation", Expected="Outputs meet requirement limits", Name="" }
            };
        }

        private static List<StepRow> ParseServiceHistorySteps(string sectionOrWhole)
        {
            var crit = ExtractBullets(SliceFrom(sectionOrWhole, "**Service History Criteria:**"));
            if (crit.Count == 0)
            {
                return new List<StepRow> {
                    new StepRow{ Number="1", Action="Assemble and review service history package", Expected="Evidence supports compliance with requirement", Name="" }
                };
            }
            var rows = new List<StepRow>();
            for (int i = 0; i < crit.Count; i++)
            {
                rows.Add(new StepRow
                {
                    Number = (i + 1).ToString(),
                    Action = "Verify: " + crit[i],
                    Expected = "Criterion satisfied by evidence",
                    Name = ""
                });
            }
            return rows;
        }

        private static List<StepRow> ParseVerifiedAtAnotherLevelSteps(string sectionOrWhole)
        {
            // Try to parse the small table first
            var tableRows = new List<StepRow>();
            var lines = sectionOrWhole.Replace("\r", "").Split('\n');
            int headerIdx = Array.FindIndex(lines, l =>
                Regex.IsMatch(l, @"(?i)^\s*\|\s*Level\s*\|\s*Item/Test\s*ID\s*\|\s*Report\s*ID/Rev\s*\|\s*Coverage\s*Notes\s*\|\s*$"));

            if (headerIdx >= 0)
            {
                int n = 0;
                for (int i = headerIdx + 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) break;
                    if (!line.Contains("|")) break;
                    if (line.StartsWith("**")) break;

                    var parts = line.Split('|').Select(s => s.Trim()).ToList();
                    if (parts.First() == "") parts.RemoveAt(0);
                    if (parts.Count > 0 && parts.Last() == "") parts.RemoveAt(parts.Count - 1);
                    if (parts.Count < 4) continue;

                    n++;
                    var level = parts[0];
                    var itemId = parts[1];
                    var report = parts[2];
                    var notes = parts.Count > 3 ? parts[3] : "";

                    tableRows.Add(new StepRow
                    {
                        Number = n.ToString(),
                        Action = $"Credit to {level}: {itemId} via {report}",
                        Expected = string.IsNullOrWhiteSpace(notes) ? "Coverage per credited report" : notes,
                        Name = ""
                    });
                }
                if (tableRows.Count > 0) return tableRows;
            }

            // Fallback
            return new List<StepRow> {
                new StepRow{ Number="1", Action="Apply credit from upstream/downstream verification", Expected="Credit statement present and trace complete", Name="" }
            };
        }

        // Public counter (used by VM to show “exportable” totals)
        public static int CountExportableSteps(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return 0;

            var methods = DetectMethodsInOutput(output);
            int total = 0;

            foreach (var method in methods)
            {
                var section = ExtractSectionForMethodOrWhole(output, method);

                if (method.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
                    method.Equals("TestUnintendedFunction", StringComparison.OrdinalIgnoreCase))
                {
                    total += ParseMarkdownSteps(section).Count;
                }
                else if (method.Equals("Demonstration", StringComparison.OrdinalIgnoreCase))
                {
                    var c = ParseMarkdownSteps(section).Count;
                    if (c == 0) c = ParseDemonstrationSteps(output).Count;
                    total += c;
                }
                else if (method.Equals("Inspection", StringComparison.OrdinalIgnoreCase))
                {
                    total += ParseInspectionSteps(output).Count;
                }
                else if (method.Equals("Analysis", StringComparison.OrdinalIgnoreCase))
                {
                    total += ParseAnalysisSteps(output).Count;
                }
                else if (method.Equals("Simulation", StringComparison.OrdinalIgnoreCase))
                {
                    total += ParseSimulationSteps(output).Count;
                }
                else if (method.Equals("ServiceHistory", StringComparison.OrdinalIgnoreCase))
                {
                    total += ParseServiceHistorySteps(output).Count;
                }
                else if (method.Equals("VerifiedAtAnotherLevel", StringComparison.OrdinalIgnoreCase))
                {
                    total += ParseVerifiedAtAnotherLevelSteps(output).Count;
                }
            }

            return total;
        }

        // Exporter — writes one CSV row per step (Jama-friendly UTF-8 without BOM)
        public string ExportAllGeneratedTestCasesToCsv(
            IEnumerable<Requirement> requirements,
            string outPath,
            string project,
            string testPlan)
        {

            string[] headers =
                {"Project",
                "Test Plan",
                "Name",
                "Description",
                "Associated Requirements",
                "Test Step Number",
                "Test Step Action",
                "Test Step Expected Result",
                "Test Step Notes",
                "Tags"
            };

            int reqCount = 0, reqWithAnyCase = 0, caseCount = 0, rowCount = 0;
            var summary = new StringBuilder();

            Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? ".");
            using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.WriteLine(string.Join(",", headers.Select(CsvEscape)));

            foreach (var req in requirements ?? Enumerable.Empty<Requirement>())
            {
                reqCount++;
                var output = req.CurrentResponse?.Output;
                if (string.IsNullOrWhiteSpace(output)) continue;

                var methods = DetectMethodsInOutput(output);
                bool emittedAnyForReq = false;

                // Fallback: use requirement’s methods (skip Unassigned). If still none, mark unknown (empty string).
                if (methods == null || !methods.Any())
                {
                    var vmList = (req.VerificationMethods ?? new List<VMVerMethod>())
                        .Where(m => m != VMVerMethod.Unassigned)
                        .Select(m => m.ToString())
                        .ToList();

                    methods = vmList.Count > 0 ? vmList : new List<string> { string.Empty }; // unknown
                }

                foreach (var method in methods)
                {
                    if (method.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // If method is unknown/empty, parse the whole output; else parse the method section
                    var section = string.IsNullOrWhiteSpace(method) ? output : ExtractSectionForMethodOrWhole(output, method);

                    // Prefer objective from the section; fall back to requirement description
                    string description = ExtractObjective(section);
                    if (string.IsNullOrWhiteSpace(description))
                        description = req.Description ?? string.Empty;

                    // 1) Try strict 4-column markdown/pipe table first
                    List<StepRow> steps = ParseMarkdownSteps(section);

                    // 2) If none, try a lenient 3-or-4 column pipe-table parser
                    if (steps.Count == 0)
                        steps = ParseMarkdownStepsLenient(section);

                    // 3) If still none, try method-specific parsers only when method is known
                    if (steps.Count == 0 && !string.IsNullOrWhiteSpace(method))
                    {
                        steps =
                            method.Equals("Test", StringComparison.OrdinalIgnoreCase) ? ParseMarkdownSteps(section) :
                            method.Equals("TestUnintendedFunction", StringComparison.OrdinalIgnoreCase) ? ParseMarkdownSteps(section) :
                            method.Equals("Demonstration", StringComparison.OrdinalIgnoreCase)
                                ? (ParseMarkdownSteps(section).Count > 0 ? ParseMarkdownSteps(section) : ParseDemonstrationSteps(section))
                                : method.Equals("Inspection", StringComparison.OrdinalIgnoreCase) ? ParseInspectionSteps(section)
                                : method.Equals("Analysis", StringComparison.OrdinalIgnoreCase) ? ParseAnalysisSteps(section)
                                : method.Equals("Simulation", StringComparison.OrdinalIgnoreCase) ? ParseSimulationSteps(section)
                                : method.Equals("ServiceHistory", StringComparison.OrdinalIgnoreCase) ? ParseServiceHistorySteps(section)
                                : method.Equals("VerifiedAtAnotherLevel", StringComparison.OrdinalIgnoreCase) ? ParseVerifiedAtAnotherLevelSteps(section)
                                : new List<StepRow>();
                    }


                    if (steps.Count == 0) continue;

                    var baseName = string.IsNullOrWhiteSpace(req.Name)
                        ? (string.IsNullOrWhiteSpace(req.Item) ? "Autogenerated Test Case" : $"{req.Item} – Autogenerated Test Case")
                        : $"{req.Item}: {req.Name}";

                    // Do not append (method) when unknown/empty
                    var caseName = string.IsNullOrWhiteSpace(method) ? baseName : $"{baseName} ({method})";

                    var associated = string.IsNullOrWhiteSpace(req.GlobalId) ? (req.Item ?? "") : req.GlobalId;
                    var tags = string.Join(";", req.TagList ?? Enumerable.Empty<string>());

                    foreach (var st in steps)
                    {
                        var row = new[]
                        {
                    project, testPlan, caseName, description, associated,
                    st.Number, st.Action, st.Expected, st.Name, tags
                };
                        writer.WriteLine(string.Join(",", row.Select(CsvEscape)));
                        rowCount++;
                    }

                    caseCount++;
                    emittedAnyForReq = true;
                }

                if (emittedAnyForReq) reqWithAnyCase++;
            }

            summary.AppendLine($"Requirements scanned: {reqCount}");
            summary.AppendLine($"Requirements with ≥1 exported test case: {reqWithAnyCase}");
            summary.AppendLine($"Distinct test cases exported: {caseCount}");
            summary.AppendLine($"CSV rows written (one per step): {rowCount}");
            if (rowCount == 0)
                summary.AppendLine("No rows exported. Likely causes: drafts lack step tables/bullets or CurrentResponse.Output is empty.");

            return summary.ToString();
        }

        // Accepts pipe tables with either 3 or 4 columns:
        // [Number] | [Action] | [Expected] | [Name?]
        private static List<StepRow> ParseMarkdownStepsLenient(string text)
        {
            var result = new List<StepRow>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            // Normalize line endings
            var lines = text.Replace("\r", "").Split('\n');

            // Find a header-ish line with at least two pipes
            int i = 0;
            while (i < lines.Length && (lines[i].IndexOf('|') < 0)) i++;
            if (i >= lines.Length) return result;

            // If next line looks like a markdown separator (---|---), skip it
            int start = i + 1;
            if (start < lines.Length && lines[start].Contains("|") && lines[start].Contains("-"))
                start++;

            for (int r = start; r < lines.Length; r++)
            {
                var raw = lines[r].Trim();
                if (string.IsNullOrEmpty(raw)) continue;
                if (raw.StartsWith("**") || raw.StartsWith("#")) break; // likely next section

                // Must be a pipe row
                if (raw.IndexOf('|') < 0) continue;

                // Split, keep 3-4 cells
                var cells = raw.Split('|')
                               .Select(c => c.Trim())
                               .Where(c => c.Length > 0)
                               .ToList();

                if (cells.Count < 3) continue; // need at least Number, Action, Expected

                // Map cells
                var number = cells[0];
                var action = cells.Count >= 2 ? cells[1] : "";
                var expected = cells.Count >= 3 ? cells[2] : "";
                var name = cells.Count >= 4 ? cells[3] : "";

                // Light cleanup: strip "Step" prefix from number
                if (number.StartsWith("Step", StringComparison.OrdinalIgnoreCase))
                    number = number.Substring(4).Trim().Trim(':');

                result.Add(new StepRow
                {
                    Number = number,
                    Action = action,
                    Expected = expected,
                    Name = name
                });
            }

            return result;
        }


        /// <summary>CSV-safe cell escaping per RFC4180.</summary>
        private static string CsvEscape(string? s)
        {
            s ??= string.Empty;
            bool mustQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
            return mustQuote ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        }

        // ─────────────────────────────────────────────
        // ───────────── Excel helpers ────────────────
        // ─────────────────────────────────────────────

        private static List<string> ReadRowValuesAligned(WorkbookPart wbPart, Row row)
        {
            int maxIdx = 0;
            foreach (var c in row.Elements<Cell>())
            {
                int idx = GetColumnIndex(c.CellReference?.Value ?? "");
                if (idx > maxIdx) maxIdx = idx;
            }

            var values = Enumerable.Repeat(string.Empty, maxIdx + 1).ToList();
            foreach (var c in row.Elements<Cell>())
            {
                int idx = GetColumnIndex(c.CellReference?.Value ?? "");
                values[idx] = GetCellString(wbPart, c);
            }
            return values;
        }

        // A=0, B=1, ... Z=25, AA=26, etc. (returns 0-based)
        private static int GetColumnIndex(string cellRef)
        {
            if (string.IsNullOrWhiteSpace(cellRef)) return 0;

            int col = 0;
            foreach (char ch in cellRef)
            {
                if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1);
                else if (ch >= 'a' && ch <= 'z') col = col * 26 + (ch - 'a' + 1);
                else break; // stop at first non-letter (e.g., the row number)
            }

            return Math.Max(0, col - 1); // convert to 0-based index
        }

        private static string GetCellString(WorkbookPart wbPart, Cell cell)
        {
            if (cell == null) return string.Empty;

            // Inline string
            if (cell.DataType?.Value == CellValues.InlineString && cell.InlineString != null)
                return cell.InlineString.Text?.Text ?? string.Empty;

            // Shared string
            if (cell.DataType?.Value == CellValues.SharedString)
            {
                var sst = wbPart.SharedStringTablePart?.SharedStringTable;
                if (sst == null) return string.Empty;
                if (!int.TryParse(cell.CellValue?.InnerText, out int ssi)) return string.Empty;
                var item = sst.Elements<SharedStringItem>().ElementAtOrDefault(ssi);
                return item?.InnerText ?? string.Empty;
            }

            // Boolean
            if (cell.DataType?.Value == CellValues.Boolean)
                return (cell.CellValue?.InnerText == "1") ? "TRUE" : "FALSE";

            // Number / General
            return cell.CellValue?.InnerText ?? string.Empty;
        }

        public void ExportAllGeneratedTestCasesToExcel(IEnumerable<Requirement> requirements, string outputPath)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            var reqList = requirements.ToList();

            // Partition by “set” (we infer via reflection so we don't add model fields)
            // Try: SetName → Set → FolderPath → Folder → else "All Requirements"
            var bySet = reqList
                .GroupBy(r => GetGroupName(r))
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            using var wb = new XLWorkbook();

            // Create each worksheet
            var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var setGroup in bySet)
            {
                var sheetName = MakeWorksheetNameUnique(SanitizeSheetName(setGroup.Key), usedSheetNames);
                usedSheetNames.Add(sheetName);

                var ws = wb.Worksheets.Add(sheetName);

                // Row 1: banner (merged across all columns), like Jama’s round-trip
                ws.Cell(1, 1).Value = $"Excel Export from Jama\t\t\t\t{DateTime.Now:MM/dd/yyyy}";
                ws.Range(1, 1, 1, _roundtripColumns.Length).Merge();
                ws.Row(1).Height = 22;

                // Row 2: round-trip headers (exact names / order)
                for (int i = 0; i < _roundtripColumns.Length; i++)
                    ws.Cell(2, i + 1).Value = _roundtripColumns[i];

                // Hide columns A–E (1..5), as the round-trip template does
                for (int col = 1; col <= 5; col++)
                    ws.Column(col).Hide();

                int row = 3;

                // Emit rows in requirement order. Each requirement can host multiple named cases.
                foreach (var req in setGroup)
                {
                    var steps = GetSteps(req).ToList();           // IEnumerable<object> → List<object>
                    var hasSteps = steps.Count > 0;

                    if (hasSteps)
                    {
                        // Group by TestCase.Name (via reflection)
                        foreach (var caseGroup in steps.GroupBy(s => StepName(s), StringComparer.Ordinal))
                        {
                            var caseName = BestCaseName(caseGroup.Key, req);
                            var reqDesc = TryGetStringProp(req, "Description");
                            var reqName = TryGetStringProp(req, "Name");
                            var descriptionHtml = WrapPlainTextAsHtml(
                                string.IsNullOrWhiteSpace(reqDesc) ? (reqName ?? string.Empty) : reqDesc!);

                            // Sort steps numerically by StepNumber when possible
                            var ordered = caseGroup
                                .OrderBy(s => TryParseIntSafe(StepNumber(s)))
                                .ThenBy(s => StepNumber(s));

                            int stepIndex = 1;
                            foreach (var st in ordered)
                            {
                                WriteRoundtripRow(ws, row++,
                                    apiId: "", version: "", hasChildren: "FALSE", indent: 5,
                                    heading: GetHeading(req), jamaId: "",
                                    name: caseName, assigned: "", testCaseHtml: descriptionHtml, testCaseStatus: "",
                                    stepNumber: stepIndex++,
                                    stepAction: SafeCell(StepAction(st)),
                                    stepExpected: SafeCell(StepExpected(st)),
                                    stepNotes: SafeCell(StepNotes(st)),
                                    testRunResults: ""
                                );
                            }
                        }
                    }
                    else
                    {
                        // Shell: emit a single row (so Jama creates the case)
                        var caseName = BestCaseName(null, req);
                        var reqDesc = TryGetStringProp(req, "Description");
                        var reqName = TryGetStringProp(req, "Name");
                        var descriptionHtml = WrapPlainTextAsHtml(
                            string.IsNullOrWhiteSpace(reqDesc) ? (reqName ?? string.Empty) : reqDesc!);

                        WriteRoundtripRow(ws, row++,
                            apiId: "", version: "", hasChildren: "FALSE", indent: 5,
                            heading: GetHeading(req), jamaId: "",
                            name: caseName, assigned: "", testCaseHtml: descriptionHtml, testCaseStatus: "",
                            stepNumber: 1,
                            stepAction: "", stepExpected: "", stepNotes: "",
                            testRunResults: ""
                        );
                    }
                }

                // A little readability (purely cosmetic)
                ws.Columns().AdjustToContents(6, _roundtripColumns.Length); // adjust visible cols (F..O)
                                                                            // Set some practical widths (optional)
                SafeSetWidth(ws, 6, 18);  // Heading
                SafeSetWidth(ws, 7, 18);  // ID
                SafeSetWidth(ws, 8, 45);  // Name
                SafeSetWidth(ws, 9, 22);  // Assigned
                SafeSetWidth(ws, 10, 16); // Test Case Status
                SafeSetWidth(ws, 11, 10); // Step #
                SafeSetWidth(ws, 12, 40); // Step Action
                SafeSetWidth(ws, 13, 40); // Step Expected Result
                SafeSetWidth(ws, 14, 24); // Step Notes
                SafeSetWidth(ws, 15, 18); // Test Run Results
            }

            // If no requirements, still create a single empty sheet to avoid a corrupt file
            if (!bySet.Any())
            {
                var ws = wb.Worksheets.Add("All Requirements");
                ws.Cell(1, 1).Value = $"Excel Export from Jama\t\t\t\t{DateTime.Now:MM/dd/yyyy}";
                ws.Range(1, 1, 1, _roundtripColumns.Length).Merge();
                for (int i = 0; i < _roundtripColumns.Length; i++)
                    ws.Cell(2, i + 1).Value = _roundtripColumns[i];
                for (int col = 1; col <= 5; col++) ws.Column(col).Hide();
            }

            wb.SaveAs(outputPath);
        }

        // ======== helpers (private, no new public API) ========

        // Exact round-trip column order you validated in Jama
        private static readonly string[] _roundtripColumns = new[]
        {
            "API-ID",
            "Version #",
            "Has Children",
            "Indent",
            "Heading",
            "ID",
            "Name",
            "Assigned",
            "Test Case",
            "Test Case Status",
            "Step #",
            "Step Action",
            "Step Expected Result",
            "Step Notes",
            "Test Run Results"
        };

        private static void WriteRoundtripRow(IXLWorksheet ws, int row,
            string apiId, string version, string hasChildren, int indent,
            string heading, string jamaId,
            string name, string assigned, string testCaseHtml, string testCaseStatus,
            int stepNumber, string stepAction, string stepExpected, string stepNotes, string testRunResults)
        {
            // A..O = 15 columns
            ws.Cell(row, 1).Value = apiId;
            ws.Cell(row, 2).Value = version;
            ws.Cell(row, 3).Value = hasChildren;
            ws.Cell(row, 4).Value = indent;
            ws.Cell(row, 5).Value = heading;
            ws.Cell(row, 6).Value = jamaId;
            ws.Cell(row, 7).Value = name;
            ws.Cell(row, 8).Value = assigned;
            ws.Cell(row, 9).Value = testCaseHtml;       // Jama tolerates HTML here
            ws.Cell(row, 10).Value = testCaseStatus;    // cannot update via round-trip
            ws.Cell(row, 11).Value = stepNumber;
            ws.Cell(row, 12).Value = stepAction;
            ws.Cell(row, 13).Value = stepExpected;
            ws.Cell(row, 14).Value = stepNotes;
            ws.Cell(row, 15).Value = testRunResults;
        }

        private static string SafeCell(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var t = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            // Excel formula guard: prefix if starts with =, +, -, @ to avoid formula injection
            if (t.Length > 0 && ("=+-@".IndexOf(t[0]) >= 0)) t = "'" + t;
            return t;
        }

        private static int TryParseIntSafe(string? s) => int.TryParse(s, out var n) ? n : int.MaxValue;

        private static string BestCaseName(string? testCaseNameFromRow, Requirement req)
        {
            string pick(params string?[] opts) => opts.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Untitled Test";
            var composed = !string.IsNullOrWhiteSpace(req?.Item) && !string.IsNullOrWhiteSpace(req?.Name)
                ? $"{req!.Item}: {req!.Name}" : null;
            return SafeCell(pick(testCaseNameFromRow, composed, req?.Item, req?.Name));
        }

        private static string WrapPlainTextAsHtml(string? text)
        {
            // Round-trip allows HTML in "Test Case" field. If plain, wrap in <p>.
            var t = (text ?? string.Empty).Trim();
            if (t.StartsWith("<", StringComparison.Ordinal)) return t; // already HTML
                                                                       // Collapse whitespace and wrap
            t = Regex.Replace(t, @"[\r\n\t]+", " ").Trim();
            if (string.IsNullOrEmpty(t)) return string.Empty;
            var sb = new StringBuilder();
            sb.Append("<p>").Append(System.Net.WebUtility.HtmlEncode(t)).Append("</p>");
            return sb.ToString();
        }

        private static string GetHeading(Requirement r)
        {
            // Best-effort: try common properties for a heading/section (reflection so we don't add to the model)
            return TryGetStringProp(r, "Heading")
                ?? TryGetStringProp(r, "Section")
                ?? TryGetStringProp(r, "Path")
                ?? string.Empty;
        }

        private static string GetGroupName(Requirement r)
        {
            // Infer a “set” name without changing your model:
            // SetName → Set → FolderPath (last segment) → Folder → else fallback
            var setName = TryGetStringProp(r, "SetName")
                       ?? TryGetStringProp(r, "Set")
                       ?? LastSegment(TryGetStringProp(r, "FolderPath"))
                       ?? TryGetStringProp(r, "Folder")
                       ?? "All Requirements";
            return string.IsNullOrWhiteSpace(setName) ? "All Requirements" : setName.Trim();
        }

        private static string? TryGetStringProp(object obj, string prop)
        {
            var pi = obj.GetType().GetProperty(prop);
            if (pi == null) return null;
            var val = pi.GetValue(obj);
            return val?.ToString();
        }

        private static string? LastSegment(string? pathLike)
        {
            if (string.IsNullOrWhiteSpace(pathLike)) return null;
            var parts = pathLike.Replace('\\', '/').Split('/');
            return parts.Length == 0 ? pathLike : parts[^1];
        }

        private static string SanitizeSheetName(string name)
        {
            var t = string.IsNullOrWhiteSpace(name) ? "Sheet" : name.Trim();

            // Replace illegal chars: \ / ? * [ ]
            t = Regex.Replace(t, @"[\\/\?\*\[\]:]", "-");
            // Collapse whitespace
            t = Regex.Replace(t, @"\s+", " ");
            // Excel max 31 chars
            if (t.Length > 31) t = t.Substring(0, 31);
            if (t.Length == 0) t = "Sheet";
            return t;
        }

        private static string MakeWorksheetNameUnique(string baseName, HashSet<string> used)
        {
            if (!used.Contains(baseName)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                var candidate = SanitizeSheetName($"{baseName} ({i})");
                if (!used.Contains(candidate)) return candidate;
            }
            return Guid.NewGuid().ToString("n").Substring(0, 8);
        }

        private static void SafeSetWidth(IXLWorksheet ws, int columnIndex, double width)
        {
            try { ws.Column(columnIndex).Width = width; } catch { /* ignore */ }
        }


        // ------------ local helpers (keep private inside the class) ------------

        // Returns all step objects from a Requirement via reflection.
        // Works whether the backing property exists or not (returns empty when absent).
        private static IEnumerable<object> GetSteps(object? req)
        {
            if (req == null) yield break;
            var pi = req.GetType().GetProperty("GeneratedTestCases");
            var enumerable = pi?.GetValue(req) as System.Collections.IEnumerable;
            if (enumerable == null) yield break;
            foreach (var s in enumerable)
                if (s != null) yield return s;
        }

        // Step field accessors via reflection (return empty string when missing)
        private static string StepName(object? step) => TryGetStringProp(step!, "Name") ?? string.Empty;
        private static string StepAction(object? step) => TryGetStringProp(step!, "StepAction") ?? string.Empty;
        private static string StepExpected(object? step) => TryGetStringProp(step!, "StepExpectedResult") ?? string.Empty;
        private static string StepNotes(object? step) => TryGetStringProp(step!, "StepNotes") ?? string.Empty;
        private static string StepNumber(object? step) => TryGetStringProp(step!, "StepNumber") ?? string.Empty;

        // Keep the same signature — only the body changes
        private static (string Name, string Description) ComputeNameAndDescription(Requirement req, string? preferredCaseName)
        {
            // Build the display Name first
            string item = req?.Item ?? "";
            string reqNm = req?.Name ?? "";
            string name = Clean(
                string.IsNullOrWhiteSpace(preferredCaseName)
                    ? (!string.IsNullOrWhiteSpace(item) && !string.IsNullOrWhiteSpace(reqNm)
                        ? $"{item}: {reqNm}"
                        : (!string.IsNullOrWhiteSpace(item) ? item : (reqNm.Length > 0 ? reqNm : "Untitled Test")))
                    : preferredCaseName);

            // Description (“Test Case” column) — prefer Requirement.Description
            string desc = Clean(req?.Description);

            // If empty and we have a specific test-case name, try first matching step’s TestCaseText via reflection
            if (string.IsNullOrEmpty(desc) && !string.IsNullOrWhiteSpace(preferredCaseName))
            {
                var stepsEnum = req?.GetType().GetProperty("GeneratedTestCases")?.GetValue(req) as System.Collections.IEnumerable;
                if (stepsEnum != null)
                {
                    foreach (var s in stepsEnum)
                    {
                        var sType = s.GetType();
                        var stepCaseName = sType.GetProperty("Name")?.GetValue(s)?.ToString() ?? "";
                        if (string.Equals(stepCaseName, preferredCaseName, StringComparison.Ordinal))
                        {
                            var tcText = sType.GetProperty("TestCaseText")?.GetValue(s)?.ToString();
                            desc = Clean(tcText);
                            break;
                        }
                    }
                }
            }

            return (name, desc);
        }


        private static int TryParseIntSafe(string? s, int fallbackIndex) =>
            int.TryParse((s ?? "").Trim(), out var n) ? n : int.MaxValue - 100000 + fallbackIndex;

        private static string Clean(string? s) =>
            string.IsNullOrWhiteSpace(s) ? "" :
            s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();

        private static void WriteRow(IXLWorksheet ws, int r, string name, string testCase, string action, string expected, string notes)
        {
            ws.Cell(r, 1).Value = name;
            ws.Cell(r, 2).Value = testCase;
            ws.Cell(r, 3).Value = action;
            ws.Cell(r, 4).Value = expected;
            ws.Cell(r, 5).Value = notes;
        }

        private static string MakeSafeSheetName(string raw, XLWorkbook book)
        {
            string s = (raw ?? "Sheet").Trim();
            foreach (var ch in new[] { '\\', '/', '*', '?', ':', '[', ']' }) s = s.Replace(ch, '-');
            if (s.Length == 0) s = "Sheet";
            if (s.Length > 31) s = s.Substring(0, 31);

            string candidate = s; int i = 2;
            while (book.Worksheets.Any(w => string.Equals(w.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                var baseName = s.Length > 28 ? s.Substring(0, 28) : s;
                candidate = $"{baseName} ({i++})";
                if (candidate.Length > 31) candidate = candidate.Substring(0, 31);
            }
            return candidate;
        }

        private static string? ComputeSetName(Requirement req)
        {
            var t = req.GetType();

            var p = t.GetProperty("SetName");
            if (p?.GetValue(req) is string s1 && !string.IsNullOrWhiteSpace(s1)) return s1.Trim();

            p = t.GetProperty("SetKey");
            if (p?.GetValue(req) is string s2 && !string.IsNullOrWhiteSpace(s2)) return s2.Trim();

            foreach (var prop in new[] { "ItemPath", "FolderPath" })
            {
                p = t.GetProperty(prop);
                if (p?.GetValue(req) is string path && !string.IsNullOrWhiteSpace(path))
                {
                    var seg = path.Replace('\\', '/').Trim('/');
                    if (seg.Contains('/')) seg = seg.Split('/').LastOrDefault() ?? seg;
                    if (!string.IsNullOrWhiteSpace(seg)) return seg;
                }
            }
            return null;
        }

        private static string GetPropString(object? obj, string propName)
        {
            if (obj is null) return "";
            var p = obj.GetType().GetProperty(propName);
            var v = p?.GetValue(obj);
            return v?.ToString()?.Trim() ?? "";
        }

        private static (string Name, string Description) ComputeNameAndDescriptionDynamic(object req, string? preferredCaseName)
        {
            // pull Item/Name/Description via reflection so we work with any Requirement flavor
            string item = GetPropString(req, "Item");
            string reqName = GetPropString(req, "Name");
            string desc = Clean(GetPropString(req, "Description"));

            // Name priority: explicit case name → "{Item}: {Name}" → Item → Name → fallback
            string name = Clean(
                string.IsNullOrWhiteSpace(preferredCaseName)
                    ? (!string.IsNullOrWhiteSpace(item) && !string.IsNullOrWhiteSpace(reqName)
                        ? $"{item}: {reqName}"
                        : (string.IsNullOrWhiteSpace(item) ? (string.IsNullOrWhiteSpace(reqName) ? "Untitled Test" : reqName) : item))
                    : preferredCaseName);

            return (name, desc);
        }


        private static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> headers)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
            {
                var key = NormalizeHeader(headers[i]);
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (!map.ContainsKey(key)) map[key] = i;
            }
            return map;
        }

        private static string NormalizeHeader(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var t = s.Trim();
            t = Regex.Replace(t, @"\s+", " ");
            t = t.Replace(":", "").Replace("-", " ").Trim();
            return t.ToLowerInvariant();
        }

        private static int? TryGet(Dictionary<string, int> headerMap, params string[] names)
        {
            foreach (var n in names)
            {
                var key = NormalizeHeader(n);
                if (headerMap.TryGetValue(key, out var idx)) return idx;
            }
            return null;
        }

        private static string Get(IReadOnlyList<string> rowValues, int? idx) =>
            (idx is int i && i >= 0 && i < rowValues.Count) ? (rowValues[i] ?? string.Empty) : string.Empty;
    }
}
