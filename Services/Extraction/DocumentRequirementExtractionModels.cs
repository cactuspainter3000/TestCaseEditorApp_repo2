using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TestCaseEditorApp.Services.Extraction
{
    public enum DocumentBlockKind
    {
        Paragraph,
        Heading,
        TableRow,
        ListItem,
        Noise
    }

    public enum ExtractionCandidateStatus
    {
        Pending,
        Accepted,
        NeedsReview,
        Rejected
    }

    public enum ReverseValidationAction
    {
        Accept,
        Review,
        Reject
    }

    public sealed class DocumentBlock
    {
        public int BlockIndex { get; set; }
        public DocumentBlockKind Kind { get; set; } = DocumentBlockKind.Paragraph;
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string Text { get; set; } = string.Empty;
        public string NormalizedText { get; set; } = string.Empty;
        public string? SourcePrefix { get; set; }
        public string? SourcePrefixType { get; set; }
        public string? SourcePrefixEvidence { get; set; }
        public double EvidenceScore { get; set; }
        public bool HasRequirementLanguage { get; set; }
        public bool HasExplicitIdentifier { get; set; }
        public string? NoiseReason { get; set; }
    }

    public sealed class DocumentRequirementCandidate
    {
        public string CandidateId { get; set; } = string.Empty;
        public int BlockIndex { get; set; }
        public string RawText { get; set; } = string.Empty;
        public string NormalizedText { get; set; } = string.Empty;
        public string? SourcePrefix { get; set; }
        public string? SourcePrefixType { get; set; }
        public string? SourcePrefixEvidence { get; set; }
        public double Confidence { get; set; }
        public double EvidenceScore { get; set; }
        public ExtractionCandidateStatus Status { get; set; } = ExtractionCandidateStatus.Pending;
        public string? RejectionReason { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public List<string> EvidenceSnippets { get; set; } = new();
    }

    public sealed class DocumentExtractionStageMetrics
    {
        public string StageName { get; set; } = string.Empty;
        public int InputCount { get; set; }
        public int OutputCount { get; set; }
        public int RejectedCount { get; set; }
        public TimeSpan Elapsed { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    public sealed class DocumentRequirementExtractionResult
    {
        public string DocumentName { get; set; } = string.Empty;
        public string OriginalContent { get; set; } = string.Empty;
        public string NormalizedContent { get; set; } = string.Empty;
        public List<DocumentBlock> Blocks { get; set; } = new();
        public List<DocumentRequirementCandidate> Candidates { get; set; } = new();
        public List<DocumentExtractionStageMetrics> StageMetrics { get; set; } = new();

        public int CandidateCount => Candidates.Count;
        public int AcceptedCandidateCount => Candidates.Count(c => c.Status == ExtractionCandidateStatus.Accepted);
        public int ReviewCandidateCount => Candidates.Count(c => c.Status == ExtractionCandidateStatus.NeedsReview);
        public int RejectedCandidateCount => Candidates.Count(c => c.Status == ExtractionCandidateStatus.Rejected);

        public string BuildEvidenceLedger(int maxItems = 12)
        {
            if (Candidates.Count == 0)
            {
                return "<no candidates>";
            }

            var sb = new StringBuilder();
            foreach (var candidate in Candidates
                .OrderByDescending(c => c.Confidence)
                .ThenBy(c => c.BlockIndex)
                .Take(Math.Max(1, maxItems)))
            {
                sb.AppendLine($"- {candidate.CandidateId} | status={candidate.Status} | confidence={candidate.Confidence:F2} | prefix={(candidate.SourcePrefix ?? "UNK")}");
                sb.AppendLine($"  text: {Truncate(candidate.NormalizedText, 240)}");
                if (!string.IsNullOrWhiteSpace(candidate.SourcePrefixEvidence))
                {
                    sb.AppendLine($"  evidence: {Truncate(candidate.SourcePrefixEvidence, 180)}");
                }
                if (!string.IsNullOrWhiteSpace(candidate.RejectionReason))
                {
                    sb.AppendLine($"  rejection: {Truncate(candidate.RejectionReason, 180)}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        public string BuildPromptContext(int maxChars = 12000)
        {
            var evidenceLedger = BuildEvidenceLedger();
            var promptContext = string.IsNullOrWhiteSpace(evidenceLedger)
                ? NormalizedContent
                : $"{NormalizedContent}\n\n[Evidence Ledger]\n{evidenceLedger}";

            if (promptContext.Length <= maxChars)
            {
                return promptContext;
            }

            return promptContext[..maxChars] + "...";
        }

        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
        }
    }

    public sealed class ReverseValidationVerdict
    {
        public string SubjectId { get; set; } = string.Empty;
        public bool IsLegit { get; set; }
        public ReverseValidationAction Action { get; set; } = ReverseValidationAction.Review;
        public double Confidence { get; set; }
        public string Summary { get; set; } = string.Empty;
        public List<string> Issues { get; set; } = new();
        public string? Evidence { get; set; }
    }
}