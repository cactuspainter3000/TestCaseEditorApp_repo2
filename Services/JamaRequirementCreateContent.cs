using System;
using System.Collections.Generic;
using System.Net;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    public sealed record JamaRequirementCreateContent(string Name, string Description, string SourceProvenance)
    {
        public static JamaRequirementCreateContent FromRequirement(Requirement requirement)
        {
            var name = !string.IsNullOrWhiteSpace(requirement.Name) ? requirement.Name
                : !string.IsNullOrWhiteSpace(requirement.Item) ? requirement.Item : "Extracted Requirement";
            var description = !string.IsNullOrWhiteSpace(requirement.Description) ? requirement.Description : name;

            if (requirement.SourceDocumentLevel is not (RequirementSourceLevel.L0 or RequirementSourceLevel.L1))
                return new(name, description, string.Empty);

            var lines = new List<string> { $"Source document level: {requirement.SourceDocumentLevel}" };
            AddSourceValue(lines, "Source document", requirement.SourceDocumentName);
            AddSourceValue(lines, "Source section", requirement.SourceSection);
            AddSourceValue(lines, "Source reference", requirement.TraceReference);

            return new(name,
                $"<p>{WebUtility.HtmlEncode(description).ReplaceLineEndings("<br />")}</p>" +
                $"<p>{string.Join("<br />", lines.ConvertAll(WebUtility.HtmlEncode))}</p>",
                string.Join(Environment.NewLine, lines));
        }

        private static void AddSourceValue(List<string> lines, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value.Trim(), "UNK", StringComparison.OrdinalIgnoreCase))
                lines.Add($"{label}: {value.Trim()}");
        }
    }
}
