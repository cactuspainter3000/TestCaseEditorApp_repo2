using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Services
{
    public sealed record JamaFieldCoverageResult(
        IReadOnlyList<string> ExpectedFields,
        IReadOnlyList<string> ActualFields,
        IReadOnlyList<string> MissingExpectedFields,
        IReadOnlyList<string> UnexpectedActualFields)
    {
        public bool IsComplete => MissingExpectedFields.Count == 0;
    }

    public sealed record JamaFieldCoverageSummary(
        int TotalRequirements,
        int CompleteRequirements,
        int IncompleteRequirements,
        int RequirementsWithUnexpectedFields,
        IReadOnlyDictionary<string, int> MissingFieldCounts,
        IReadOnlyDictionary<string, int> UnexpectedFieldCounts)
    {
        public bool HasIssues => IncompleteRequirements > 0 || RequirementsWithUnexpectedFields > 0;
    }

    public sealed record JamaFieldCoverageHealth(
        bool IsContractHealthy,
        IReadOnlyList<string> MissingCoreFields);

    public static class JamaRequirementFieldCoverage
    {
        private static readonly string[] ExpectedFields =
        {
            "Item ID",
            "Name",
            "Requirement Description",
            "Global ID",
            "API ID",
            "Item Type",
            "DOORS ID",
            "Item Path",
            "Project",
            "Project Defined",
            "Release",
            "Relationship Status",
            "DOORS Relationship",
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
            "Derived Requirement",
            "Export Controlled",
            "Customer ID",
            "FDAL",
            "Key Characteristics",
            "Heading",
            "Rationale",
            "Compliance Rationale",
            "Change Driver",
            "Allocation/s",
            "Status",
            "Requirement Type",
            "Safety Requirement",
            "Safety Rationale",
            "Security Requirement",
            "Security Rationale",
            "Validation Method/s",
            "Validation Evidence",
            "Validation Conclusion",
            "Verification Method/s",
            "Robust Requirement",
            "Robust Rationale",
            "Tags",
            "Upstream Relationships",
            "Relationships",
            "Synchronized Items",
            "Comments",
            "# of Downstream Relationships",
            "# of Upstream Relationships",
            "Connected Users",
            "# of Attachments",
            "# of Comments",
            "# of Links"
        };

        private static readonly string[] CoreContractFields =
        {
            "Item ID",
            "Name",
            "Requirement Description",
            "Global ID",
            "Item Type",
            "Status",
            "Validation Method/s",
            "Verification Method/s"
        };

        private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> CoreFieldAliases =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Validation Method/s"] = new[] { "Validation Method/s", "Validation Methods" },
                ["Verification Method/s"] = new[] { "Verification Method/s", "Verification Methods" }
            };

        public static IReadOnlyList<string> GetExpectedFields() => ExpectedFields;

        public static JamaFieldCoverageResult AnalyzeExportKeys(IEnumerable<string?> actualKeys)
        {
            var actual = actualKeys
                .Select(NormalizeKey)
                .Where(key => key.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return BuildCoverageResult(actual);
        }

        public static JamaFieldCoverageResult AnalyzeFieldDictionary(JsonElement fieldsArray)
        {
            var actual = new List<string>();

            if (fieldsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var field in fieldsArray.EnumerateArray())
                {
                    var fieldName = GetFieldName(field);
                    if (!string.IsNullOrWhiteSpace(fieldName))
                    {
                        actual.Add(fieldName);
                    }
                }
            }

            actual = actual
                .Select(NormalizeKey)
                .Where(key => key.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return BuildCoverageResult(actual);
        }

        public static JamaFieldCoverageSummary Summarize(IEnumerable<JamaFieldCoverageResult> results)
        {
            var resultList = results?.ToList() ?? new List<JamaFieldCoverageResult>();

            var missingFieldCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var unexpectedFieldCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var result in resultList)
            {
                foreach (var missingField in result.MissingExpectedFields)
                {
                    missingFieldCounts[missingField] = missingFieldCounts.TryGetValue(missingField, out var count)
                        ? count + 1
                        : 1;
                }

                foreach (var unexpectedField in result.UnexpectedActualFields)
                {
                    unexpectedFieldCounts[unexpectedField] = unexpectedFieldCounts.TryGetValue(unexpectedField, out var count)
                        ? count + 1
                        : 1;
                }
            }

            var completeRequirements = resultList.Count(result => result.IsComplete);
            var incompleteRequirements = resultList.Count - completeRequirements;
            var requirementsWithUnexpectedFields = resultList.Count(result => result.UnexpectedActualFields.Count > 0);

            return new JamaFieldCoverageSummary(
                TotalRequirements: resultList.Count,
                CompleteRequirements: completeRequirements,
                IncompleteRequirements: incompleteRequirements,
                RequirementsWithUnexpectedFields: requirementsWithUnexpectedFields,
                MissingFieldCounts: missingFieldCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase),
                UnexpectedFieldCounts: unexpectedFieldCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase));
        }

        public static string FormatCoverageResultForReport(JamaFieldCoverageResult coverage, int maxListItems = 15)
        {
            var health = EvaluateCoverageHealth(coverage);
            var missing = coverage.MissingExpectedFields.Count == 0
                ? "none"
                : string.Join(", ", coverage.MissingExpectedFields.Take(maxListItems));
            var unexpected = coverage.UnexpectedActualFields.Count == 0
                ? "none"
                : string.Join(", ", coverage.UnexpectedActualFields.Take(maxListItems));
            var missingCore = health.MissingCoreFields.Count == 0
                ? "none"
                : string.Join(", ", health.MissingCoreFields);

            var report = new StringBuilder();
            report.AppendLine($"- Expected fields: {coverage.ExpectedFields.Count}");
            report.AppendLine($"- Actual fields discovered: {coverage.ActualFields.Count}");
            report.AppendLine($"- Contract status: {(health.IsContractHealthy ? "PASS" : "FAIL")}");
            report.AppendLine($"- Missing core fields: {missingCore}");
            report.AppendLine($"- Missing expected fields: {missing}");
            report.Append($"- Unexpected actual fields: {unexpected}");
            return report.ToString();
        }

        public static JamaFieldCoverageHealth EvaluateCoverageHealth(JamaFieldCoverageResult coverage)
        {
            var actualSet = new HashSet<string>(coverage.ActualFields, StringComparer.OrdinalIgnoreCase);
            var missingCoreFields = CoreContractFields
                .Where(field => !IsCoreFieldPresent(actualSet, field))
                .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new JamaFieldCoverageHealth(
                IsContractHealthy: missingCoreFields.Count == 0,
                MissingCoreFields: missingCoreFields);
        }

        private static bool IsCoreFieldPresent(IReadOnlySet<string> actualSet, string requiredField)
        {
            if (actualSet.Contains(requiredField))
            {
                return true;
            }

            if (!CoreFieldAliases.TryGetValue(requiredField, out var aliases))
            {
                return false;
            }

            return aliases.Any(actualSet.Contains);
        }

        private static JamaFieldCoverageResult BuildCoverageResult(IReadOnlyCollection<string> actual)
        {
            var expected = ExpectedFields
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var expectedSet = new HashSet<string>(expected, StringComparer.OrdinalIgnoreCase);
            var actualSet = new HashSet<string>(actual, StringComparer.OrdinalIgnoreCase);

            var missing = expected
                .Where(field => !actualSet.Contains(field))
                .ToList();

            var unexpected = actual
                .Where(field => !expectedSet.Contains(field))
                .ToList();

            return new JamaFieldCoverageResult(expected, actual.ToList(), missing, unexpected);
        }

        private static string GetFieldName(JsonElement field)
        {
            return GetStringProperty(field, "fieldName")
                ?? GetStringProperty(field, "name")
                ?? GetStringProperty(field, "apiName")
                ?? GetStringProperty(field, "key")
                ?? string.Empty;
        }

        private static string? GetStringProperty(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var prop))
            {
                return null;
            }

            return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
        }

        private static string NormalizeKey(string? key)
        {
            var value = (key ?? string.Empty).Trim();
            while (value.EndsWith(":", StringComparison.Ordinal))
            {
                value = value[..^1].TrimEnd();
            }

            return value;
        }
    }
}