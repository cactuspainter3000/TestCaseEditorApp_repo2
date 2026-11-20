using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.OpenProject.Services
{
    public static class JamaWorkspaceRequirementRefresh
    {
        public static List<Requirement> Merge(
            IEnumerable<Requirement> cachedRequirements, IEnumerable<Requirement> jamaRequirements)
        {
            var classified = cachedRequirements
                .Where(requirement => requirement.SourceDocumentLevel is RequirementSourceLevel.L0 or RequirementSourceLevel.L1)
                .ToList();
            var published = classified
                .Select(requirement => (Id: GetJamaId(requirement), Requirement: requirement))
                .Where(entry => entry.Id.HasValue)
                .ToLookup(entry => entry.Id.GetValueOrDefault(), entry => entry.Requirement);
            var refreshed = jamaRequirements.ToList();
            var matched = new HashSet<Requirement>();

            foreach (var remote in refreshed)
            {
                if (GetJamaId(remote) is not { } id)
                    continue;

                var candidates = published[id];
                var local = candidates.FirstOrDefault();
                if (local == null)
                    continue;
                if (candidates.Skip(1).Any() || !matched.Add(local))
                    throw new InvalidOperationException($"Ambiguous document requirement match for Jama item ID {id}.");

                PreserveWorkspaceData(local, remote);
            }

            // A missing remote item is not authority to delete local review work or test cases.
            refreshed.AddRange(classified.Where(requirement => !matched.Contains(requirement)));
            return refreshed;
        }

        private static int? GetJamaId(Requirement requirement) =>
            int.TryParse(requirement.ApiId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
                ? id : null;

        private static void PreserveWorkspaceData(Requirement local, Requirement remote)
        {
            remote.Item = local.Item;
            remote.GlobalId = local.GlobalId;
            remote.SourceDocumentLevel = local.SourceDocumentLevel;
            remote.SourceDocumentName = local.SourceDocumentName;
            remote.SourceSection = local.SourceSection;
            remote.SourcePrefix = local.SourcePrefix;
            remote.SourcePrefixType = local.SourcePrefixType;
            remote.SourcePrefixEvidence = local.SourcePrefixEvidence;
            remote.SourcePrefixConfidence = local.SourcePrefixConfidence;
            remote.TraceReference = local.TraceReference;
            remote.SourceAttachmentId = local.SourceAttachmentId;
            remote.SourceJamaItemId = local.SourceJamaItemId;
            remote.AtpDerivation = local.AtpDerivation;

            remote.SelectedAssumptionKeys = local.SelectedAssumptionKeys;
            remote.ClarifyingQuestions = local.ClarifyingQuestions;
            remote.Analysis = local.Analysis;
            remote.CurrentResponse = local.CurrentResponse;
            if (local.GeneratedTestCases != null)
            {
                foreach (var testCase in local.GeneratedTestCases)
                    remote.GeneratedTestCases.Add(testCase);
            }
        }
    }
}
