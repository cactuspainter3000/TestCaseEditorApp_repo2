using System.Globalization;
using System.Text.RegularExpressions;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services
{
    public interface IRequirementEnricher
    {
        void Enrich(IList<Requirement> requirements);
    }

    public static class JamaRequirementMapper
    {
        /// <summary>
        /// Map a KV bag (from one exported item) into an existing Requirement instance.
        /// We only backfill header-derived fields (Item/Name/Description) when they’re empty.
        /// </summary>
        public static void MapFromKv(Requirement r, IReadOnlyDictionary<string, string> kv)
        {
            // ===== Header-derived fields (backfill if empty) =====
            var itemId = Get(kv, "Item ID");
            if (!string.IsNullOrWhiteSpace(itemId) && string.IsNullOrWhiteSpace(r.Item))
                r.Item = itemId;

            var name = Get(kv, "Name");
            if (!string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(r.Name))
                r.Name = name;

            var reqDesc = Get(kv, "Requirement Description");
            if (!string.IsNullOrWhiteSpace(reqDesc) && string.IsNullOrWhiteSpace(r.Description))
                r.Description = reqDesc;

            // ===== Identification & linkage =====
            r.GlobalId = Get(kv, "Global ID");
            r.ApiId = Get(kv, "API ID");
            r.ItemType = Get(kv, "Item Type");
            r.DoorsId = Get(kv, "DOORS ID");
            r.ItemPath = Get(kv, "Item Path");

            // ===== Project / release =====
            r.Project = Get(kv, "Project");
            r.ProjectDefined = Get(kv, "Project Defined");
            r.Release = Get(kv, "Release");
            r.RelationshipStatus = Get(kv, "Relationship Status");
            r.DoorsRelationship = Get(kv, "DOORS Relationship");

            // ===== Versioning & activity =====
            r.Version = Get(kv, "Version");
            r.CurrentVersion = Get(kv, "Current version");

            r.LastActivityDateRaw = Get(kv, "Last Activity Date");
            r.LastActivityDate = ParseDateTime(r.LastActivityDateRaw);

            r.ModifiedDateRaw = Get(kv, "Modified Date");
            r.ModifiedDate = ParseDateTime(r.ModifiedDateRaw);

            r.CreatedDateRaw = Get(kv, "Created Date");
            r.CreatedDate = ParseDateTime(r.CreatedDateRaw);

            // ===== Locking =====
            r.Locked = Get(kv, "Locked");
            r.LastLockedRaw = Get(kv, "Last Locked");
            r.LastLocked = ParseDateTime(r.LastLockedRaw);
            r.LastLockedBy = Get(kv, "Last Locked By");

            // ===== People =====
            r.CreatedBy = Get(kv, "Created By");
            r.ModifiedBy = Get(kv, "Modified By");

            // ===== Compliance / classification =====
            r.DerivedRequirement = Get(kv, "Derived Requirement");
            r.ExportControlled = Get(kv, "Export Controlled");
            r.CustomerId = Get(kv, "Customer ID");
            r.Fdal = Get(kv, "FDAL");
            r.KeyCharacteristics = Get(kv, "Key Characteristics");

            // ===== Heading & rationale =====
            r.Heading = Get(kv, "Heading");
            r.Rationale = Get(kv, "Rationale");
            r.ComplianceRationale = Get(kv, "Compliance Rationale");
            r.ChangeDriver = Get(kv, "Change Driver");
            r.Allocations = Get(kv, "Allocation/s");

            // ===== Status / type =====
            r.Status = Get(kv, "Status");
            r.RequirementType = Get(kv, "Requirement Type");

            // ===== Safety / security =====
            r.SafetyRequirement = Get(kv, "Safety Requirement");
            r.SafetyRationale = Get(kv, "Safety Rationale");
            r.SecurityRequirement = Get(kv, "Security Requirement");
            r.SecurityRationale = Get(kv, "Security Rationale");

            // --------------- Validation (string ? enum list) ---------------
            r.ValidationMethodRaw = Get(kv, "Validation Method/s");
            r.ValidationMethods = ParseValidationMethods(r.ValidationMethodRaw);

            r.ValidationEvidence = Get(kv, "Validation Evidence");
            r.ValidationConclusion = Get(kv, "Validation Conclusion");

            // --------------- Verification (string ? enum list + primary) ---------------
            r.VerificationMethodRaw = Get(kv, "Verification Method/s");
            r.VerificationMethods = ParseVerificationMethods(r.VerificationMethodRaw);

            r.Method = (r.VerificationMethods.Count > 0)
                ? r.VerificationMethods[0]
                : VerificationMethod.Inspection; // or .Inspection if you prefer a non-empty default

            // ===== Robust requirement extras =====
            r.RobustRequirement = Get(kv, "Robust Requirement");
            r.RobustRationale = Get(kv, "Robust Rationale");

            // ===== Tags & trailing text blocks =====
            r.Tags = GetTrailingOrInline(kv, "Tags");
            r.TagList = SplitTokensFlexible(r.Tags).ToList();
            r.UpstreamRelationshipsText = GetTrailingOrInline(kv, "Upstream Relationships");
            r.RelationshipsText = GetTrailingOrInline(kv, "Relationships");
            r.SynchronizedItemsText = GetTrailingOrInline(kv, "Synchronized Items");
            r.CommentsText = GetTrailingOrInline(kv, "Comments");

            // ===== Counts =====
            r.NumberOfDownstreamRelationships = ParseInt(Get(kv, "# of Downstream Relationships"));
            r.NumberOfUpstreamRelationships = ParseInt(Get(kv, "# of Upstream Relationships"));
            r.ConnectedUsers = ParseInt(Get(kv, "Connected Users"));
            r.NumberOfAttachments = ParseInt(Get(kv, "# of Attachments"));
            r.NumberOfComments = ParseInt(Get(kv, "# of Comments"));
            r.NumberOfLinks = ParseInt(Get(kv, "# of Links"));
        }

        /* ===================== Helpers ===================== */

        private static string Get(IReadOnlyDictionary<string, string> kv, string key)
            => kv.TryGetValue(key, out var v) ? (v ?? string.Empty).Trim() : string.Empty;

        /// Accept both the KV key (e.g., "Tags") and trailing form ("Tags:") when present.
        private static string GetTrailingOrInline(IReadOnlyDictionary<string, string> kv, string baseKey)
        {
            if (kv.TryGetValue(baseKey, out var v)) return v?.Trim() ?? string.Empty;
            var alt = baseKey.EndsWith(":") ? baseKey : baseKey + ":";
            return kv.TryGetValue(alt, out var v2) ? v2?.Trim() ?? string.Empty : string.Empty;
        }

        private static int ParseInt(string s)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

        private static DateTime? ParseDateTime(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            // Handles "2025-03-04 20:08:22.0", "03/05/2025", "2025-02-27", etc.
            string[] formats =
            {
                "yyyy-MM-dd HH:mm:ss.FFF",
                "yyyy-MM-dd HH:mm:ss",
                "M/d/yyyy",
                "MM/dd/yyyy",
                "yyyy-MM-dd",
                "M/d/yyyy h:mm tt",
                "MM/dd/yyyy h:mm tt"
            };

            if (DateTime.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture,
                                       DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out var dt))
                return dt;

            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
                return dt;

            return null;
        }

        private static IEnumerable<string> SplitTokensFlexible(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) yield break;

            foreach (var tok in Regex.Split(raw, @"\s*(?:,|;|/|\band\b)\s*", RegexOptions.IgnoreCase))
            {
                var t = tok.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        private static List<VerificationMethod> ParseVerificationMethods(string? raw)
        {
            var list = new List<VerificationMethod>();
            foreach (var t in SplitTokensFlexible(raw))
            {
                switch (t.ToLowerInvariant())
                {
                    case "analysis": list.Add(VerificationMethod.Analysis); break;
                    case "simulation": list.Add(VerificationMethod.Simulation); break;
                    case "demonstration": list.Add(VerificationMethod.Demonstration); break;
                    case "inspection": list.Add(VerificationMethod.Inspection); break;
                    case "service history": list.Add(VerificationMethod.ServiceHistory); break;
                    case "test": list.Add(VerificationMethod.Test); break;
                    case "test unintended function": list.Add(VerificationMethod.TestUnintendedFunction); break;
                    case "unassigned": list.Add(VerificationMethod.Unassigned); break;
                    case "verified at another level": list.Add(VerificationMethod.VerifiedAtAnotherLevel); break;
                    default: break; // ignore unknown tokens
                }
            }
            return list.Distinct().ToList();
        }

        private static List<ValidationMethod> ParseValidationMethods(string? raw)
        {
            var list = new List<ValidationMethod>();
            foreach (var t in SplitTokensFlexible(raw))
            {
                switch (t.ToLowerInvariant())
                {
                    case "unassigned": list.Add(ValidationMethod.Unassigned); break;
                    case "analysis": list.Add(ValidationMethod.Analysis); break;
                    case "engineering judgement":
                    case "engineering judgment": list.Add(ValidationMethod.EngineeringJudgement); break;
                    case "modeling":
                    case "modelling": list.Add(ValidationMethod.Modeling); break;
                    case "similarity": list.Add(ValidationMethod.Similarity); break;
                    case "test": list.Add(ValidationMethod.Test); break;
                    case "traceability": list.Add(ValidationMethod.Traceability); break;
                    case "review": list.Add(ValidationMethod.Review); break;
                    case "n/a":
                    case "na":
                    case "not applicable": list.Add(ValidationMethod.NotApplicable); break;
                    default: break;
                }
            }
            return list.Distinct().ToList();
        }

        // ---------- Optional: KV builder for raw "Key<TAB>Value" lines ----------
        public static class JamaKvBuilder
        {
            /// <summary>
            /// Build KVs from lines like: "Verification Method/s\tDemonstration,Test".
            /// Allows multi-line values: subsequent non-keyed lines append to the last key's value.
            /// </summary>
            public static Dictionary<string, string> Build(IEnumerable<string> lines)
            {
                var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? currentKey = null;

                foreach (var raw in lines)
                {
                    var line = (raw ?? string.Empty).TrimEnd();

                    // Key \t Value
                    var tab = line.IndexOf('\t');
                    if (tab > 0)
                    {
                        var key = line[..tab].Trim();
                        var val = line[(tab + 1)..].Trim();
                        if (key.Length > 0)
                        {
                            kv[key] = val;
                            currentKey = key;
                            continue;
                        }
                    }

                    // Continuation (append) if we have an active key
                    if (!string.IsNullOrWhiteSpace(line) && currentKey != null)
                    {
                        kv[currentKey] = (kv[currentKey] + " " + line).Trim();
                    }
                    else
                    {
                        currentKey = null;
                    }
                }

                return kv;
            }
        }
    }
}
