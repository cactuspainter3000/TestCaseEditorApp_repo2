using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services
{
    /// <summary>
    /// Prints post-processed inspection output for the requirements parsed from a Jama DOC/DOCX.
    /// No behavior changes to parsing; pure logging for human review.
    /// </summary>
    public static class JamaImportInspection
    {
        // Tuning knobs (change as you like)
        public static int MaxRequirements = 999;        // how many requirements to print
        public static int MaxDescChars = 400;           // truncate long descriptions
        public static int MaxParagraphsToShow = 6;      // supporting paras preview count
        public static int MaxTablesToShow = 4;          // supporting tables preview count
        public static int MaxCellsPerRowPreview = 6;    // how many cells print in each preview row
        public static int MaxCellChars = 50;            // truncate long cell text
        public static bool ShowFullDescription = false; // set true to avoid truncation

        /// <summary>
        /// One-liner: parse a file and print the inspection log.
        /// </summary>
        public static void PrintForFile(string path)
        {
            var reqs = JamaAllDataDocxParser.Parse(path) ?? new List<Requirement>();
            Print(reqs);
        }

        /// <summary>
        /// Print the inspection log for an already-parsed list of requirements.
        /// </summary>
        public static void Print(IEnumerable<Requirement> requirements)
        {
            var list = (requirements ?? Enumerable.Empty<Requirement>()).ToList();
            Log($"======= JAMA IMPORT INSPECTION =======");
            Log($"Count: {list.Count}");
            Log($"======================================");

            int shown = 0;
            foreach (var r in list)
            {
                if (r == null) continue;
                if (shown++ >= MaxRequirements) break;
                PrintRequirement(r, shown);
            }

            Log($"============ SUMMARY =================");
            Log($"Printed: {Math.Min(shown, MaxRequirements)} of {list.Count} requirements");
            Log($"======================================");
        }

        private static void PrintRequirement(Requirement r, int index)
        {
            var item = r?.Item ?? "<null>";
            var heading = r?.Heading ?? "";
            var name = r?.Name ?? "";
            var desc = r?.Description ?? "";

            // Verification fields (robust to nulls)
            var vmRaw = r?.VerificationMethodRaw ?? "";
            var vms = r?.VerificationMethods != null
                ? string.Join(", ", r.VerificationMethods)
                : "";
            var primary = r != null ? r.Method.ToString() : "Unknown";

            // Optional Jama fields (guarded; present in your model per spec)
            var status = SafeReflect(r, "Status");
            var version = SafeReflect(r, "Version");
            var globalId = SafeReflect(r, "GlobalId");

            Log($"");
            Log($"=== [{index:000}] {item} {heading} {name}".Trim());
            Log($"Item:    {item}");
            if (!string.IsNullOrWhiteSpace(heading)) Log($"Heading: {heading}");
            if (!string.IsNullOrWhiteSpace(name)) Log($"Name:    {name}");

            // Description
            var descOut = ShowFullDescription ? desc : Trunc(desc, MaxDescChars);
            Log($"Desc:    {(string.IsNullOrWhiteSpace(descOut) ? "<none>" : descOut)}");

            // Verification
            if (!string.IsNullOrWhiteSpace(vmRaw) || !string.IsNullOrWhiteSpace(vms))
            {
                Log($"Verify:  Raw='{vmRaw}'  Parsed=[{vms}]  Primary={primary}");
            }

            // Selected Jama meta (if present)
            var jamaMeta = new List<string>();
            if (!string.IsNullOrWhiteSpace(status)) jamaMeta.Add($"Status={status}");
            if (!string.IsNullOrWhiteSpace(version)) jamaMeta.Add($"Version={version}");
            if (!string.IsNullOrWhiteSpace(globalId)) jamaMeta.Add($"GlobalId={globalId}");
            if (jamaMeta.Count > 0) Log("Meta:    " + string.Join(" | ", jamaMeta));

            // Supporting Information: paragraphs
            var paras = r?.LooseContent?.Paragraphs ?? new List<string>();
            Log($"Supp:    Paragraphs={paras.Count}  Tables={(r?.LooseContent?.Tables?.Count ?? 0)}");

            if (paras.Count > 0)
            {
                int i = 0;
                foreach (var p in paras.Take(MaxParagraphsToShow))
                {
                    Log($"  P[{i++:00}]: {Trunc(p ?? "", 200)}");
                }
                if (paras.Count > MaxParagraphsToShow)
                    Log($"  ... ({paras.Count - MaxParagraphsToShow} more paragraphs)");
            }

            // Supporting Information: tables
            var tables = r?.LooseContent?.Tables ?? new List<LooseTable>();
            if (tables.Count > 0)
            {
                int tIndex = 0;
                foreach (var t in tables.Take(MaxTablesToShow))
                {
                    // Table dims
                    var rows = t?.Rows ?? new List<List<string>>();
                    int rowCount = rows.Count;
                    int colCount = rows.Count > 0 ? rows.Max(row => (row?.Count ?? 0)) : 0;

                    var title = (t?.EditableTitle ?? "").Trim();
                    Log($"  T[{tIndex:00}]: {rowCount}x{colCount}" + (title.Length > 0 ? $"  Title='{Trunc(title, 80)}'" : ""));

                    // Show first 2 rows preview
                    for (int rr = 0; rr < Math.Min(2, rowCount); rr++)
                    {
                        var cells = rows[rr] ?? new List<string>();
                        var preview = cells
                            .Take(MaxCellsPerRowPreview)
                            .Select(c => Trunc(c ?? "", MaxCellChars).Replace("\t", "?TAB?"))
                            .ToList();
                        Log($"         r{rr:00} | " + string.Join(" | ", preview));
                    }

                    tIndex++;
                }
                if (tables.Count > MaxTablesToShow)
                    Log($"  ... ({tables.Count - MaxTablesToShow} more tables)");
            }
        }

        // --- Helpers -------------------------------------------------------

        private static void Log(string s) => TestCaseEditorApp.Services.Logging.Log.Debug(s);

        private static string Trunc(string s, int max)
        {
            if (s is null) return "";
            if (max <= 0) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, Math.Max(0, max - 1)) + "…";
        }

        /// <summary>
        /// Attempts to read a simple property by name (Status, Version, GlobalId, etc.) without
        /// hard-binding to your model; returns empty string if missing.
        /// </summary>
        private static string SafeReflect(Requirement? r, string propName)
        {
            if (r == null) return "";
            try
            {
                var pi = r.GetType().GetProperty(propName);
                if (pi == null) return "";
                var val = pi.GetValue(r);
                return val?.ToString() ?? "";
            }
            catch { return ""; }
        }
    }
}

