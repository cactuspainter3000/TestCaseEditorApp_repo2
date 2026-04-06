using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;
using System.Text.RegularExpressions;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services
{
    /// <summary>
    /// Structure-first DOCX parser for Jama “All Data” export.
    /// - Walks blocks in order (Paragraphs, Tables, SdtBlocks) and buffers prelude content until a Jama KV table is found.
    /// - On KV table: extracts KV pairs, finds the nearest header above, extracts Description + Supporting Info (paragraphs + non-KV tables).
    /// - Normalizes Unicode whitespace/dashes; avoids false splits on "Global ID ..." by ignoring paragraph KV lines before a requirement is open.
    /// </summary>
    public static class JamaAllDataDocxParser
    {
        private static readonly Regex HeaderRx = new(
    @"^(?:(?<lead>\d+(?:\.\d+)*?)\s+)?(?<item>[A-Z0-9_-]+-REQ_RC-\d+)\s+(?:(?<heading>\d+(?:\.\d+)*?)\s+)?(?<name>.+?)$",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // --- Config ---------------------------------------------------------

        /// <summary>Known Jama KV keys (left column of 2-col table). Keep these exact; no heuristics.</summary>
        private static readonly HashSet<string> JamaKvKeys = new(StringComparer.Ordinal)
        {
            "Item ID",
            "Name",
            "Requirement Description",
            "Item Type",
            "Global ID",
            "Requirement Type",
            "Validation Method/s",
            "Validation Evidence",
            "Validation Conclusion",
            "Verification Method/s",
            "Verification Methods",
            "Verification Evidence",
            "Verification Conclusion",
            "Compliance Rationale",
            "Project Defined",
            "FDAL",
            "Key Characteristics",
            "Robust Requirement",
            "Robust Rationale",
            "Upstream Cross Instance Relationships",
            "Downstream Cross Instance Relationships",
            "Release",
            "Tags",
            "# of Attachments",
            "DOORS Relationship",
            "DOORS ID",
            "Heading",
            "Rationale",
            "Change Driver",
            "Allocation/s",
            "Status",
            "Derived Requirement",
            "Security Requirement",
            "Safety Requirement",
            "Security Rationale",
            "Safety Rationale",
            "Customer ID",
            "Export Controlled",
            "Version",
            "Current version",
            "Last Activity Date",
            "Modified Date",
            "Created Date",
            "Locked",
            "Last Locked",
            "Last Locked By",
            "Created By",
            "Modified By",
            "# of Downstream Relationships",
            "# of Upstream Relationships",
            "Connected Users",
            "# of Comments",
            "# of Links"
            // Add any other *exact* field labels you see in your export
        };

        private static readonly HashSet<string> PostSectionLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "Tags:",
            "Attachments:",
            "Upstream Relationships:",
            "Relationships:",
            "Synchronized Items:",
            "Comments:"
        };

        /// <summary>Enable minimal debug captures for the first parsed requirement.</summary>
        public static bool DebugDump { get; set; } = false;



        // Debug buffers (first requirement only)
        private static readonly List<string> _debugHeaders = new();
        private static readonly List<string> _debugKvKeys = new();
        private static string _debugDescription = string.Empty;

        // --- Public API -----------------------------------------------------

        /// <summary>Parses a Jama “All Data” DOCX into a list of Requirement models.</summary>
        public static List<Requirement> Parse(string path)
        {
            bool suppressPostSection = false;

            var results = new List<Requirement>();

            using var doc = WordprocessingDocument.Open(path, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null) return results;

            var preludeParagraphs = new List<string>();
            var preludeTables = new List<LooseTable>();

            // ---------- helpers ----------
            static string NormalizeSpaces(string? s)
                => string.IsNullOrWhiteSpace(s) ? string.Empty
                                                : string.Join(" ", (s ?? string.Empty)
                                                        .Trim()
                                                        .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));

            static string NormTitle(string? s)
            {
                var t = NormalizeSpaces(s);
                if (t.EndsWith(":", StringComparison.Ordinal)) t = t[..^1].TrimEnd();
                return t.ToUpperInvariant();
            }

            static string StripTrailingColon(string? s)
                => (s ?? string.Empty).TrimEnd().TrimEnd(':').TrimEnd();

            static bool IsLikelyTableTitle(string? s)
            {
                var t = (s ?? string.Empty).Trim();
                if (t.Length == 0) return false;

                // Avoid obvious non-titles
                if (t.StartsWith("•") || t.StartsWith("- ")) return false;
                if (t.Length <= 3) return false; // e.g., "Tags" alone can be a KV label, not a title

                // Strong signals
                if (t.EndsWith(":", StringComparison.Ordinal)) return true;
                if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^(Table|Tbl)\s*\d+[:\.\-)]?\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return true;

                // Light heuristic: short-ish line before a table often is a title
                var wordCount = t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                return wordCount <= 10;
            }
            // -----------------------------

            int blockIdx = -1;
            foreach (var block in EnumerateBlocks(body))
            {
                blockIdx++;

                switch (block)
                {
                    case Paragraph p:
                        {
                            var text = Normalize(TextOf(p));
                            if (string.IsNullOrWhiteSpace(text)) break;

                            if (suppressPostSection)
                            {
                                // stop suppressing when a new section/header starts
                                if (text.EndsWith(":", StringComparison.Ordinal) || TryParseHeader(text, out _))
                                    suppressPostSection = false;
                                else
                                    break; // drop lines inside post section
                            }

                            if (PostSectionLabels.Contains(text))
                            {
                                suppressPostSection = true;
                                break;
                            }

                            preludeParagraphs.Add(text);
                            break;
                        }

                    case Table t:
                        {
                            if (TryBuildJamaKvWithFusedSupport(
                                    t,
                                    out var kv,
                                    out var kvKeys,
                                    out var fusedSupporting,
                                    JamaKvKeys,
                                    s => TestCaseEditorApp.Services.Logging.Log.Debug(s),
                                    blockIdx))
                            {
                                // If we split a fused table, assign a title from the preceding paragraph (if present)
                                // BUT: don't steal the paragraph if it's the first one after a header (that's likely the description)
                                if (fusedSupporting.Count > 0)
                                {
                                    var hasCandidate = preludeParagraphs.Count > 0;
                                    var candidate = hasCandidate ? preludeParagraphs[^1] : null;
                                    
                                    // Find if there's a header in the prelude
                                    int tempHeaderIdx = -1;
                                    for (int i = preludeParagraphs.Count - 1; i >= 0; i--)
                                    {
                                        if (TryParseHeader(preludeParagraphs[i], out var _))
                                        {
                                            tempHeaderIdx = i;
                                            break;
                                        }
                                    }
                                    
                                    // Only use the candidate as a title if:
                                    // 1. It looks like a title, AND
                                    // 2. It's NOT the first paragraph right after a header (which would be the description)
                                    bool canUseAsTitle = !string.IsNullOrWhiteSpace(candidate) 
                                                        && IsLikelyTableTitle(candidate)
                                                        && (tempHeaderIdx < 0 || preludeParagraphs.Count - 1 > tempHeaderIdx + 1);
                                    
                                    if (canUseAsTitle)
                                    {
                                        fusedSupporting[0].EditableTitle = StripTrailingColon(candidate);
                                        if (hasCandidate) preludeParagraphs.RemoveAt(preludeParagraphs.Count - 1); // prevent leakage into Supporting Info
                                    }
                                    preludeTables.AddRange(fusedSupporting);
                                }

                                // ---- Find the header in the prelude ----
                                int headerIdx = -1;
                                ParsedHeader? header = null;
                                for (int i = preludeParagraphs.Count - 1; i >= 0; i--)
                                {
                                    if (TryParseHeader(preludeParagraphs[i], out var h))
                                    {
                                        header = h;
                                        headerIdx = i;
                                        break;
                                    }
                                }

                                if (header is null)
                                {
                                    // No header found: clear buffers and keep scanning.
                                    preludeParagraphs.Clear();
                                    preludeTables.Clear();
                                    break;
                                }

                                // Extract description + supporting paragraphs (pre-header lines are ignored)
                                var (description, supportingParasRaw) = ExtractDescriptionAndSupporting(
                                    preludeParagraphs, headerIdx + 1, preludeParagraphs.Count - 1, JamaKvKeys);

                                // === Filter out any paragraph that equals a supporting table title ===
                                var titleSet = new HashSet<string>(
                                    preludeTables.Select(tl => NormTitle(tl?.EditableTitle ?? string.Empty))
                                                 .Where(x => !string.IsNullOrWhiteSpace(x)),
                                    StringComparer.Ordinal);

                                var seen = new HashSet<string>(StringComparer.Ordinal);
                                var supportingParas = new List<string>();
                                foreach (var para in supportingParasRaw)
                                {
                                    var ptxt = (para ?? string.Empty).Trim();
                                    if (ptxt.Length == 0) continue;
                                    if (titleSet.Contains(NormTitle(ptxt))) continue; // drop table titles
                                    if (seen.Add(ptxt)) supportingParas.Add(ptxt);     // de-dup
                                }
                                // =====================================================================

                                var req = new Requirement
                                {
                                    Item = header.Value.Item,
                                    Heading = header.Value.Heading ?? string.Empty,
                                    Name = header.Value.Name ?? string.Empty,
                                    Description = description ?? string.Empty,
                                    LooseContent = new RequirementLooseContent
                                    {
                                        Paragraphs = supportingParas,
                                        Tables = preludeTables.ToList()
                                    }
                                };

                                JamaRequirementMapper.MapFromKv(req, kv);
                                results.Add(req);

                                // After consuming a KV table, suppress the trailing post section until next header/label
                                suppressPostSection = true;

                                // Reset buffers for next requirement
                                preludeParagraphs.Clear();
                                preludeTables.Clear();
                            }
                            else
                            {
                                // Not a KV table ? treat as supporting; try to derive a title from the immediately preceding paragraph
                                // BUT: don't steal the paragraph if it's the first one after a header (that's likely the description)
                                var loose = ReadLooseTable(t);
                                var hasCandidate = preludeParagraphs.Count > 0;
                                var candidate = hasCandidate ? preludeParagraphs[^1] : null;
                                
                                // Find if there's a header in the prelude
                                int headerIdx = -1;
                                for (int i = preludeParagraphs.Count - 1; i >= 0; i--)
                                {
                                    if (TryParseHeader(preludeParagraphs[i], out var _))
                                    {
                                        headerIdx = i;
                                        break;
                                    }
                                }
                                
                                // Only use the candidate as a title if:
                                // 1. It looks like a title, AND
                                // 2. It's NOT the first paragraph right after a header (which would be the description)
                                bool canUseAsTitle = !string.IsNullOrWhiteSpace(candidate) 
                                                    && IsLikelyTableTitle(candidate)
                                                    && (headerIdx < 0 || preludeParagraphs.Count - 1 > headerIdx + 1);
                                
                                if (canUseAsTitle)
                                {
                                    loose.EditableTitle = StripTrailingColon(candidate);
                                    if (hasCandidate) preludeParagraphs.RemoveAt(preludeParagraphs.Count - 1); // prevent title leakage
                                }

                                if (loose.Rows.Count > 0)
                                    preludeTables.Add(loose);
                            }
                            break;
                        }

                    case SdtBlock:
                        // flattened in EnumerateBlocks
                        break;
                }
            }

            return results;
        }





        // --- Core helpers ---------------------------------------------------

        /// <summary>Enumerates top-level content blocks in visual order, flattening SdtBlocks and table cells.</summary>
        /// 
        public static List<Requirement> Parse(string path, bool debugDump)
        {
            var previous = DebugDump;
            try
            {
                DebugDump = debugDump;
                return Parse(path); // existing Parse(path)
            }
            finally
            {
                DebugDump = previous;
            }
        }

        private static IEnumerable<OpenXmlElement> EnumerateBlocks(Body body)
        {
            foreach (var el in body.Elements())
            {
                switch (el)
                {
                    case Paragraph p:
                        yield return p;
                        break;

                    case Table t:
                        yield return t;
                        break;

                    case SdtBlock sdt:
                        foreach (var inner in EnumerateBlocksInContainer(sdt))
                            yield return inner;
                        break;

                    default:
                        // Ignore SectionProperties, etc.
                        break;
                }
            }
        }

        /// <summary>Enumerates paragraphs/tables within an arbitrary container (SdtBlock, etc.).</summary>
        private static IEnumerable<OpenXmlElement> EnumerateBlocksInContainer(OpenXmlCompositeElement container)
        {
            foreach (var el in container.Elements())
            {
                switch (el)
                {
                    case Paragraph p:
                        yield return p;
                        break;

                    case Table t:
                        yield return t;
                        break;

                    case SdtBlock innerSdt:
                        foreach (var inner in EnumerateBlocksInContainer(innerSdt))
                            yield return inner;
                        break;

                    default:
                        break;
                }
            }
        }

        /// <summary>Reads the visible text of a paragraph.</summary>
        private static string TextOf(Paragraph p)
        {
            var sb = new StringBuilder();
            foreach (var t in p.Descendants<Text>())
                sb.Append(t.Text);
            // Preserve tabs: Word often uses <tab/> runs. Map them to '\t' if present.
            foreach (var tab in p.Descendants<TabChar>())
                sb.Append('\t');
            return sb.ToString();
        }

        private static string TextOf(TableCell cell)
        {
            if (cell is null) return string.Empty;
            // InnerText concatenates runs across paragraphs inside the cell.
            // If you need explicit newlines between paragraphs, use the generic overload below.
            return cell.InnerText ?? string.Empty;
        }

        private static string TextOf(OpenXmlElement element)
        {
            if (element is null) return string.Empty;
            // Join paragraph texts with line breaks to preserve row structure better
            var sb = new System.Text.StringBuilder();
            foreach (var p in element.Descendants<Paragraph>())
            {
                var t = p.InnerText;
                if (!string.IsNullOrWhiteSpace(t))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(t);
                }
            }
            // If there were no paragraphs, fall back to InnerText
            if (sb.Length == 0) return element.InnerText ?? string.Empty;
            return sb.ToString();
        }

        /// <summary>Normalizes Unicode: NBSP -> space; en/em dash & non-breaking hyphen -> '-'; collapses internal whitespace.</summary>
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            s = s
                .Replace('\u00A0', ' ')  // NBSP
                .Replace('\u2011', '-')  // non-breaking hyphen
                .Replace('\u2012', '-')  // figure dash
                .Replace('\u2013', '-')  // en dash
                .Replace('\u2014', '-')  // em dash
                .Replace('\u2212', '-'); // minus sign

            // Collapse repeated whitespace but keep tabs (for KV detection later)
            var cleaned = Regex.Replace(s, @"[ \t\r\n]+", m =>
            {
                var hasTab = m.Value.Contains('\t');
                return hasTab ? "\t" : " ";
            });

            return cleaned.Trim();
        }

        /// <summary>
        /// Attempts to parse a requirement header from a single line with optional leading numbering.
        /// Accepts patterns like:
        ///     "C1XMA2405-REQ_RC-110 3.2 Test Equipment Data Record"
        ///     "1.3 C1XMA2405-REQ_RC-103 3.1.1 Architecture SW Interfaces"
        /// </summary>
        private static bool TryParseHeader(string line, out ParsedHeader? header)
        {
            header = null;
            if (string.IsNullOrWhiteSpace(line)) return false;

            var m = HeaderRx.Match(line);
            if (!m.Success) return false;

            var item = m.Groups["item"].Value;
            var heading = m.Groups["heading"].Success ? m.Groups["heading"].Value : null;
            var name = m.Groups["name"].Value;

            // Guard to avoid matching "Global ID ..." lines
            if (!Regex.IsMatch(item, @"-REQ_RC-\d+$")) return false;

            header = new ParsedHeader(item, heading, name);
            return true;
        }

        private static bool TryBuildJamaKvWithFusedSupport(
            Table t,
            out Dictionary<string, string> kv,
            out List<string> kvKeys,
            out List<LooseTable> fusedSupporting,   // <<< NEW: any pre/post segments split out as supporting tables
            HashSet<string>? knownKeys = null,
            Action<string>? log = null,
            int blockIndex = -1)
        {
            kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            kvKeys = new List<string>();
            fusedSupporting = new List<LooseTable>();

            knownKeys ??= JamaKvKeys;

            // Anchors to fence the true KV region when Jama glues extra rows.
            var startSentinels = new[]
            {
        "# of Downstream Relationships",
        "Downstream Cross Instance Relationships"
    };
            var endSentinels = new[]
            {
        "# of Links"
    };

            // Gather ALL rows (we’ll need them to build fused support tables from the pre/post segments)
            var allRows = t.Elements<TableRow>().ToList();

            // Build a 2-cell-only projection for KV detection/windowing
            var rows2 = new List<(string left, string right, int rowIdxInDoc, int rowIdxInAll)>();
            for (int i = 0; i < allRows.Count; i++)
            {
                var row = allRows[i];
                var cells = row.Elements<TableCell>().ToList();
                if (cells.Count != 2) continue;

                string left = Normalize(TextOf(cells[0]));
                string right = Normalize(TextOf(cells[1]));
                if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right)) continue;

                // rowIdxInDoc = 1-based position of this row within the table (useful for logs)
                rows2.Add((left, right, i + 1, i));
            }

            if (rows2.Count == 0)
            {
                //log?.Invoke($"KV:Skipped Reason=No2CellRows BlockIdx={blockIndex}");
                return false;
            }

            // Find anchors in the 2-cell projection
            int startIdx2 = -1;
            int endIdx2 = -1;
            var startSet = new HashSet<string>(startSentinels.Select(Normalize), StringComparer.OrdinalIgnoreCase);
            var endSet = new HashSet<string>(endSentinels.Select(Normalize), StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < rows2.Count; i++)
            {
                if (startSet.Contains(rows2[i].left))
                {
                    startIdx2 = i;
                    break;
                }
            }
            if (startIdx2 >= 0)
            {
                for (int i = startIdx2 + 1; i < rows2.Count; i++)
                {
                    if (endSet.Contains(rows2[i].left))
                    {
                        endIdx2 = i;
                        break;
                    }
                }
            }

            bool usedAnchors = startIdx2 >= 0 && endIdx2 > startIdx2;

            // If we have anchors, define the KV window by mapping 2-cell indices back to original rows.
            int kvStartAll = -1, kvEndAll = -1; // indices into allRows (0-based, inclusive)
            IReadOnlyList<(string left, string right, int rowIdxInDoc, int rowIdxInAll)> window2;
            if (usedAnchors)
            {
                kvStartAll = rows2[startIdx2].rowIdxInAll;
                kvEndAll = rows2[endIdx2].rowIdxInAll;
                window2 = rows2.GetRange(startIdx2, endIdx2 - startIdx2 + 1);
            }
            else
            {
                // No anchors ? fall back to “all 2-cell rows” as the KV candidate region
                window2 = rows2;
            }

            // Extract KV pairs only for recognized keys within the chosen 2-cell window
            var recognized = new List<string>();
            foreach (var (left, right, _, _) in window2)
            {
                if (knownKeys.Contains(left))
                {
                    kv[left] = right;              // last write wins
                    recognized.Add(left);
                }
            }

            if (kv.Count < 3)
            {
                //string anchorNote = usedAnchors
                //    ? $"AnchorsUsed StartRow={rows2[startIdx2].rowIdxInDoc} EndRow={rows2[endIdx2].rowIdxInDoc}"
                //    : "NoAnchors";
                //log?.Invoke($"KV:Skipped Reason=TooFewRecognizedKeys Recognized={kv.Count} Required>=3 BlockIdx={blockIndex} {anchorNote}");
                kv.Clear();
                kvKeys.Clear();
                return false;
            }

            kvKeys.AddRange(recognized.Distinct(StringComparer.OrdinalIgnoreCase));

            // If we used anchors, split pre/post segments from ALL rows as fused supporting tables.
            if (usedAnchors)
            {
                // PRE segment: rows before kvStartAll
                if (kvStartAll > 0)
                {
                    var pre = BuildLooseSubtable(allRows, 0, kvStartAll - 1);
                    if (pre is not null) fusedSupporting.Add(pre);
                }
                // POST segment: rows after kvEndAll
                if (kvEndAll < allRows.Count - 1)
                {
                    var post = BuildLooseSubtable(allRows, kvEndAll + 1, allRows.Count - 1);
                    if (post is not null) fusedSupporting.Add(post);
                }

                //if (fusedSupporting.Count > 0)
                //{
                //    log?.Invoke($"KV:Accepted BlockIdx={blockIndex} Keys={kv.Count} WindowedByAnchors " +
                //                $"StartRow={rows2[startIdx2].rowIdxInDoc} EndRow={rows2[endIdx2].rowIdxInDoc} " +
                //                $"FusedSupportTables={fusedSupporting.Count}");
                //}
                //else
                //{
                //    log?.Invoke($"KV:Accepted BlockIdx={blockIndex} Keys={kv.Count} WindowedByAnchors " +
                //                $"StartRow={rows2[startIdx2].rowIdxInDoc} EndRow={rows2[endIdx2].rowIdxInDoc} " +
                //                $"FusedSupportTables=0");
                //}
            }
            //else
            //{
            //    log?.Invoke($"KV:Accepted BlockIdx={blockIndex} Keys={kv.Count} NoAnchors");
            //}

            return true;
        }

        // Build a LooseTable from a contiguous subset of table rows (by 0-based indices into allRows).
        // Heuristics: keep all cells; drop fully empty rows; title inference is left to your existing UI logic.
        // Returns null if the segment is effectively empty.
        private static LooseTable? BuildLooseSubtable(IReadOnlyList<TableRow> allRows, int startIdx, int endIdx)
        {
            if (startIdx < 0 || endIdx >= allRows.Count || startIdx > endIdx) return null;

            var rows = new List<List<string>>();
            for (int i = startIdx; i <= endIdx; i++)
            {
                var cells = allRows[i].Elements<TableCell>().ToList();
                var texts = new List<string>(cells.Count);
                foreach (var c in cells)
                    texts.Add(Normalize(TextOf(c)));

                // skip rows that are entirely empty
                if (texts.All(string.IsNullOrWhiteSpace)) continue;

                rows.Add(texts);
            }

            if (rows.Count == 0) return null;

            return new LooseTable
            {
                EditableTitle = InferPossibleTitle(rows),
                Rows = rows
            };
        }

        // Tiny helper: if the first row is a single non-empty cell and not a known KV key, treat it as a "title".
        // You can replace this with your existing title inference or leave it minimal and let the UI handle titles.
        private static string InferPossibleTitle(List<List<string>> rows)
        {
            if (rows.Count == 0) return string.Empty;

            var first = rows[0];
            if (first.Count == 1)
            {
                var t = first[0];
                if (!string.IsNullOrWhiteSpace(t) && !JamaKvKeys.Contains(t))
                {
                    return t; // Keep the row content in Rows; EditableTitle just mirrors it for now.
                }
            }
            return string.Empty;
        }


        /// <summary>Reads a non-KV table into a LooseTable (title left empty; UI lets user edit).</summary>
        private static LooseTable ReadLooseTable(Table t)
        {
            var loose = new LooseTable
            {
                EditableTitle = string.Empty,
                Rows = new List<List<string>>()
            };

            foreach (var r in t.Elements<TableRow>())
            {
                var row = new List<string>();
                foreach (var c in r.Elements<TableCell>())
                    row.Add(Normalize(CellText(c)));
                loose.Rows.Add(row);
            }

            return loose;
        }

        /// <summary>Extracts visible text from a TableCell.</summary>
        private static string CellText(TableCell c)
        {
            var sb = new StringBuilder();
            foreach (var p in c.Elements<Paragraph>())
            {
                var s = TextOf(p);
                if (!string.IsNullOrWhiteSpace(s))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(s);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// From prelude paragraphs between [start..end], picks the first non-bullet, non-KV-looking line as Description.
        /// Remaining non-empty lines become Supporting Information.
        /// </summary>
        private static (string? description, IEnumerable<string> supporting) ExtractDescriptionAndSupporting(
            List<string> prelude, int start, int end, HashSet<string> kvKeys)
        {
            string? description = null;
            var supporting = new List<string>();

            for (int i = start; i <= end && i < prelude.Count; i++)
            {
                var line = prelude[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (IsParagraphKvLine(line, kvKeys))
                {
                    // Drop any KV-looking lines from description/supporting
                    continue;
                }

                if (description is null && !IsBulletOrNumbered(line))
                {
                    description = line;
                }
                else
                {
                    supporting.Add(line);
                }
            }

            return (description, supporting);
        }

        /// <summary>Returns true if a paragraph looks like "Key<TAB>Value" and Key is a known Jama KV key.</summary>
        private static bool IsParagraphKvLine(string line, HashSet<string> kvKeys)
        {
            // Accept only lines that *contain* a tab and start with a known key followed by a tab.
            // (This protects against "Global ID ..." lines becoming headers.)
            var idx = line.IndexOf('\t');
            if (idx <= 0) return false;

            var left = line[..idx].Trim();
            return kvKeys.Contains(left);
        }

        /// <summary>Detect simple bullet/numbered patterns; we also treat "• ..." as bullet.</summary>
        private static bool IsBulletOrNumbered(string line)
        {
            // Rough but effective: leading bullet, dash, or numbering (1., 1.2., a), -, •)
            if (line.StartsWith("•", StringComparison.Ordinal)) return true;
            if (line.StartsWith("-", StringComparison.Ordinal)) return true;
            if (Regex.IsMatch(line, @"^\d+(\.\d+)*\.\s+")) return true;         // "1. " or "1.2. "
            if (Regex.IsMatch(line, @"^[a-zA-Z]\)\s+")) return true;            // "a) "
            if (Regex.IsMatch(line, @"^(\*|\u2022)\s+")) return true;           // "* " or •
            return false;
        }

        // --- Debug ----------------------------------------------------------

        private static void ResetDebug()
        {
            _debugHeaders.Clear();
            _debugKvKeys.Clear();
            _debugDescription = string.Empty;
        }

        private static void DumpDebug()
        {
            // Keep this minimal; you can redirect to your logging facility if desired.
            TestCaseEditorApp.Services.Logging.Log.Debug("[JamaAllDataDocxParser] Debug (first requirement):");
            if (_debugHeaders.Count > 0)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("  Last 3 prelude lines near header:");
                foreach (var h in _debugHeaders)
                    TestCaseEditorApp.Services.Logging.Log.Debug("    " + h);
            }

            if (_debugKvKeys.Count > 0)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("  KV keys detected:");
                TestCaseEditorApp.Services.Logging.Log.Debug("    " + string.Join(", ", _debugKvKeys.Distinct()));
            }

            if (!string.IsNullOrEmpty(_debugDescription))
                TestCaseEditorApp.Services.Logging.Log.Debug("  Description: " + _debugDescription);
        }

        // --- Types ----------------------------------------------------------

        private readonly record struct ParsedHeader(string Item, string? Heading, string? Name);
    }
}
