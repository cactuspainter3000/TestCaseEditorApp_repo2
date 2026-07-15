using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.Services.Extraction
{
    public sealed class DocumentRequirementExtractionService : IDocumentRequirementExtractionService
    {
        private const double AcceptedConfidenceThreshold = 0.75;
        private const double ReviewPromotionThreshold = 0.25;
        private const int MaxLlmReverseValidationCandidates = 40;

        private static readonly Regex ModalVerbRegex = new(@"\b(shall|must|will|should)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HeadingRegex = new(@"^(?:section\s*[:\-]?\s*)?(?<prefix>\d+(?:\.\d+)+|[A-Za-z][A-Za-z0-9]*(?:[_-][A-Za-z0-9]+)+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ExplicitIdRegex = new(@"\b(?:id|section|sec\.?|clause)\s*:\s*(?<prefix>[A-Za-z0-9][A-Za-z0-9_.\-]*(?:[_-][A-Za-z0-9]+)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex StandaloneIdRegex = new(@"\b(?<prefix>[A-Za-z][A-Za-z0-9]*(?:[_-][A-Za-z0-9]+)+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TocArtifactRegex = new(@"\bPAGEREF\b|\bTOC\s+\\o\b|^Contents\b|^Revision History\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BoilerplateCoverRegex = new(@"Acceptance Test Procedure|Document Number:|CAGE Code:|Rockwell Collins|All rights reserved", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FormulaArtifactRegex = new(@"\b(?:equation|formula|where\s*:|\bREF\s+_Ref|A\/D\s+data|2['’]s\s+complement)\b|=\s*[-+]?\d", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly ILogger<DocumentRequirementExtractionService> _logger;
        private readonly ITextGenerationService? _textGenerationService;

        public DocumentRequirementExtractionService(
            ILogger<DocumentRequirementExtractionService> logger,
            ITextGenerationService? textGenerationService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _textGenerationService = textGenerationService;
        }

        public async Task<DocumentRequirementExtractionResult> AnalyzeAsync(string documentContent, string documentName, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;

            var result = new DocumentRequirementExtractionResult
            {
                DocumentName = documentName ?? string.Empty,
                OriginalContent = documentContent ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(documentContent))
            {
                result.StageMetrics.Add(CreateMetric("ingest", 0, 0, 0, TimeSpan.Zero, "Empty document content"));
                return result;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var lines = SplitLines(documentContent);
            result.StageMetrics.Add(CreateMetric("ingest", lines.Count, lines.Count, 0, stopwatch.Elapsed, "Document text ingested"));

            stopwatch.Restart();
            var blocks = BuildBlocks(lines);
            result.Blocks.AddRange(blocks);
            result.StageMetrics.Add(CreateMetric("segmentation", lines.Count, blocks.Count, 0, stopwatch.Elapsed, "Paragraph, heading, table-row and list-item segmentation"));

            stopwatch.Restart();
            var candidates = HarvestCandidates(blocks);
            result.Candidates.AddRange(candidates);
            result.StageMetrics.Add(CreateMetric("candidate_harvest", blocks.Count, candidates.Count, blocks.Count - candidates.Count, stopwatch.Elapsed, "Deterministic explicit-ID/modality harvest"));

            stopwatch.Restart();
            var acceptedBlocks = blocks
                .Where(block => block.Kind is DocumentBlockKind.Heading or DocumentBlockKind.TableRow or DocumentBlockKind.ListItem || block.HasRequirementLanguage || block.HasExplicitIdentifier)
                .Select(block => block.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            result.NormalizedContent = string.Join("\n", acceptedBlocks.Distinct(StringComparer.OrdinalIgnoreCase));
            result.StageMetrics.Add(CreateMetric("noise_suppression", blocks.Count, acceptedBlocks.Count, blocks.Count - acceptedBlocks.Count, stopwatch.Elapsed, "Removed repeated boilerplate, low-signal paragraphs and filler"));

            _logger.LogInformation(
                "[ExtractionFoundation] {DocumentName}: {BlockCount} blocks, {CandidateCount} candidates, {AcceptedCount} accepted by deterministic harvest",
                documentName,
                result.Blocks.Count,
                result.Candidates.Count,
                result.AcceptedCandidateCount);

            return result;
        }

        public async Task<IReadOnlyList<ReverseValidationVerdict>> ValidateRequirementsAsync(
            IReadOnlyList<Requirement> requirements,
            string documentContent,
            string documentName,
            CancellationToken cancellationToken = default)
        {
            if (requirements == null || requirements.Count == 0)
            {
                return Array.Empty<ReverseValidationVerdict>();
            }

            var requirementList = requirements.Where(r => r != null).ToList();
            if (requirementList.Count == 0)
            {
                return Array.Empty<ReverseValidationVerdict>();
            }

            var groundingBySubject = requirementList
                .ToDictionary(
                    requirement => GetRequirementSubjectId(requirement),
                    requirement => AssessSourceGrounding(requirement, documentContent),
                    StringComparer.OrdinalIgnoreCase);

            var llmValidationTargets = requirementList;
            if (requirementList.Count > MaxLlmReverseValidationCandidates)
            {
                llmValidationTargets = requirementList
                    .OrderBy(requirement => groundingBySubject[GetRequirementSubjectId(requirement)].IsGrounded)
                    .ThenBy(requirement => groundingBySubject[GetRequirementSubjectId(requirement)].MatchScore)
                    .ThenBy(requirement => GetRequirementSubjectId(requirement), StringComparer.OrdinalIgnoreCase)
                    .Take(MaxLlmReverseValidationCandidates)
                    .ToList();

                _logger.LogInformation(
                    "[ExtractionFoundation] Reverse validation sampling enabled for {DocumentName}: validating {ValidatedCount}/{TotalCount} requirements with LLM (deterministic grounding still runs for all).",
                    documentName,
                    llmValidationTargets.Count,
                    requirementList.Count);
            }

            if (_textGenerationService == null)
            {
                return requirementList
                    .Select(requirement => ApplyGroundingToVerdict(
                        BuildRuleBasedVerdict(requirement),
                        groundingBySubject[GetRequirementSubjectId(requirement)]))
                    .ToList();
            }

            var verdicts = new List<ReverseValidationVerdict>();
            const int batchSize = 8;

            foreach (var batch in llmValidationTargets.Chunk(batchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var prompt = BuildReverseValidationPrompt(batch, documentContent, documentName, groundingBySubject);
                string? response = null;
                try
                {
                    response = await _textGenerationService.GenerateAsync(prompt, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ExtractionFoundation] Reverse validation LLM call failed; using rule-based verdicts for this batch");
                }

                var parsed = TryParseReverseValidationResponse(response, batch);
                var batchVerdicts = parsed ?? batch.Select(BuildRuleBasedVerdict).ToList();
                verdicts.AddRange(batchVerdicts.Select(verdict =>
                {
                    if (!groundingBySubject.TryGetValue(verdict.SubjectId, out var grounding))
                    {
                        return verdict;
                    }

                    return ApplyGroundingToVerdict(verdict, grounding);
                }));
            }

            var validatedSubjects = verdicts
                .Select(verdict => verdict.SubjectId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var requirement in requirementList.Where(requirement => !validatedSubjects.Contains(GetRequirementSubjectId(requirement))))
            {
                var subjectId = GetRequirementSubjectId(requirement);
                var grounding = groundingBySubject[subjectId];
                var fallbackVerdict = ApplyGroundingToVerdict(BuildRuleBasedVerdict(requirement), grounding);
                verdicts.Add(fallbackVerdict);
            }

            return verdicts;
        }

        private static List<(int LineNumber, string Text)> SplitLines(string documentContent)
        {
            return documentContent
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select((line, index) => (LineNumber: index + 1, Text: line?.TrimEnd() ?? string.Empty))
                .ToList();
        }

        private static List<DocumentBlock> BuildBlocks(List<(int LineNumber, string Text)> lines)
        {
            var blocks = new List<DocumentBlock>();
            var buffer = new List<(int LineNumber, string Text)>();

            void FlushBuffer()
            {
                if (buffer.Count == 0)
                {
                    return;
                }

                var blockText = string.Join("\n", buffer.Select(line => line.Text)).Trim();
                if (!string.IsNullOrWhiteSpace(blockText))
                {
                    blocks.Add(CreateBlock(blocks.Count, buffer, blockText));
                }

                buffer.Clear();
            }

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.Text))
                {
                    FlushBuffer();
                    continue;
                }

                if (IsStandaloneHeading(line.Text) && buffer.Count > 0)
                {
                    FlushBuffer();
                }

                buffer.Add(line);
            }

            FlushBuffer();
            return blocks;
        }

        private static DocumentBlock CreateBlock(int blockIndex, List<(int LineNumber, string Text)> lines, string text)
        {
            var normalizedText = Regex.Replace(text, @"\s+", " ").Trim();
            var firstLine = lines.First().LineNumber;
            var lastLine = lines.Last().LineNumber;

            var hasModal = ModalVerbRegex.IsMatch(normalizedText);
            var prefixMatch = ExtractPrefix(normalizedText);
            var hasExplicitIdentifier = !string.IsNullOrWhiteSpace(prefixMatch.Prefix);
            var kind = ClassifyBlock(normalizedText, hasModal, hasExplicitIdentifier, lines);
            var evidenceScore = CalculateEvidenceScore(normalizedText, hasModal, hasExplicitIdentifier, kind);

            return new DocumentBlock
            {
                BlockIndex = blockIndex,
                Kind = kind,
                StartLine = firstLine,
                EndLine = lastLine,
                Text = text,
                NormalizedText = normalizedText,
                SourcePrefix = prefixMatch.Prefix,
                SourcePrefixType = prefixMatch.Type,
                SourcePrefixEvidence = prefixMatch.Evidence,
                EvidenceScore = evidenceScore,
                HasRequirementLanguage = hasModal,
                HasExplicitIdentifier = hasExplicitIdentifier,
                NoiseReason = IsNoise(normalizedText, hasModal, hasExplicitIdentifier, kind) ? "Low signal block" : null
            };
        }

        private static DocumentBlockKind ClassifyBlock(string normalizedText, bool hasModal, bool hasExplicitIdentifier, List<(int LineNumber, string Text)> lines)
        {
            if (TocArtifactRegex.IsMatch(normalizedText))
            {
                return DocumentBlockKind.Noise;
            }

            if (lines.Count == 1 && IsStandaloneHeading(lines[0].Text))
            {
                return DocumentBlockKind.Heading;
            }

            if (normalizedText.Contains('\t') || normalizedText.StartsWith("|") || normalizedText.Count(c => c == '|') >= 2)
            {
                return DocumentBlockKind.TableRow;
            }

            if (Regex.IsMatch(normalizedText, @"^\s*(?:[-*•]|\d+[.)])\s+", RegexOptions.Compiled))
            {
                return DocumentBlockKind.ListItem;
            }

            if (hasModal || hasExplicitIdentifier)
            {
                return DocumentBlockKind.Paragraph;
            }

            return DocumentBlockKind.Noise;
        }

        private static bool IsNoise(string normalizedText, bool hasModal, bool hasExplicitIdentifier, DocumentBlockKind kind)
        {
            if (kind == DocumentBlockKind.Noise)
            {
                return true;
            }

            if (TocArtifactRegex.IsMatch(normalizedText))
            {
                return true;
            }

            if (BoilerplateCoverRegex.IsMatch(normalizedText) && normalizedText.Length > 120)
            {
                return true;
            }

            if (normalizedText.Length < 25 && !hasModal && !hasExplicitIdentifier)
            {
                return true;
            }

            if (Regex.IsMatch(normalizedText, @"^(?:page\s+\d+|copyright|revision history|document history|confidential)", RegexOptions.IgnoreCase | RegexOptions.Compiled))
            {
                return true;
            }

            return false;
        }

        private static double CalculateEvidenceScore(string normalizedText, bool hasModal, bool hasExplicitIdentifier, DocumentBlockKind kind)
        {
            var score = 0.1;

            if (hasModal)
            {
                score += 0.35;
            }

            if (hasExplicitIdentifier)
            {
                score += 0.3;
            }

            if (kind == DocumentBlockKind.Heading)
            {
                score += 0.15;
            }

            if (kind == DocumentBlockKind.TableRow)
            {
                score += 0.1;
            }

            if (normalizedText.Length is >= 20 and <= 500)
            {
                score += 0.1;
            }

            if (Regex.IsMatch(normalizedText, @"^(?:note|warning|example|revision)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled))
            {
                score -= 0.3;
            }

            return Math.Clamp(score, 0.0, 1.0);
        }

        private List<DocumentRequirementCandidate> HarvestCandidates(List<DocumentBlock> blocks)
        {
            var candidates = new List<DocumentRequirementCandidate>();

            foreach (var block in blocks)
            {
                if (block.Kind == DocumentBlockKind.Noise)
                {
                    continue;
                }

                if (!block.HasRequirementLanguage && !block.HasExplicitIdentifier && block.Kind != DocumentBlockKind.Heading)
                {
                    continue;
                }

                if (IsLikelyFormulaBlock(block.NormalizedText) && !block.HasRequirementLanguage)
                {
                    continue;
                }

                var candidateId = $"cand-{block.BlockIndex + 1:D4}";
                var confidence = Math.Clamp(block.EvidenceScore + (block.HasRequirementLanguage ? 0.05 : -0.05), 0.0, 1.0);
                var status = confidence >= AcceptedConfidenceThreshold
                    ? ExtractionCandidateStatus.Accepted
                    : ExtractionCandidateStatus.NeedsReview;

                // Keep non-modal technical tokens visible for review, but do not auto-accept them.
                if (!block.HasRequirementLanguage && status == ExtractionCandidateStatus.Accepted)
                {
                    status = ExtractionCandidateStatus.NeedsReview;
                }

                var analysisFlags = BuildAnalysisFlags(block, confidence, status);
                var triage = BuildAnalysisTriage(block, confidence, analysisFlags);

                var rejectionReason = status == ExtractionCandidateStatus.Rejected
                    ? BuildLowEvidenceReason(block, confidence)
                    : null;

                candidates.Add(new DocumentRequirementCandidate
                {
                    CandidateId = candidateId,
                    BlockIndex = block.BlockIndex,
                    RawText = block.Text,
                    NormalizedText = block.NormalizedText,
                    SourcePrefix = block.SourcePrefix,
                    SourcePrefixType = block.SourcePrefixType,
                    SourcePrefixEvidence = block.SourcePrefixEvidence,
                    Confidence = confidence,
                    EvidenceScore = block.EvidenceScore,
                    Status = status,
                    RejectionReason = rejectionReason,
                    AnalysisFlags = analysisFlags,
                    AnalysisPriority = triage.Priority,
                    FixType = triage.FixType,
                    SuggestedRewrite = triage.SuggestedRewrite,
                    DispositionRecommendation = triage.Disposition,
                    StartLine = block.StartLine,
                    EndLine = block.EndLine,
                    EvidenceSnippets = new List<string>
                    {
                        block.SourcePrefixEvidence ?? block.NormalizedText
                    }
                });
            }

            return candidates;
        }

        private static (string Priority, string FixType, string SuggestedRewrite, string Disposition) BuildAnalysisTriage(
            DocumentBlock block,
            double confidence,
            IReadOnlyCollection<string> analysisFlags)
        {
            var hasMissingModal = analysisFlags.Any(flag => flag.Equals("Missing Modal Verb", StringComparison.OrdinalIgnoreCase));
            var hasMissingIdentifier = analysisFlags.Any(flag => flag.Equals("Missing Explicit Identifier", StringComparison.OrdinalIgnoreCase));
            var hasFormulaDominant = analysisFlags.Any(flag => flag.Equals("Formula/Procedure-Dominant Text", StringComparison.OrdinalIgnoreCase));

            var lowRiskIdentifierOnly = hasMissingIdentifier && !hasMissingModal && confidence >= 0.55;

            var priority = confidence < 0.25 || (hasMissingModal && hasMissingIdentifier)
                ? "High"
                : confidence < 0.40 || hasMissingModal || (hasMissingIdentifier && !lowRiskIdentifierOnly)
                    ? "Medium"
                    : "Low";

            var fixType = hasMissingModal && hasMissingIdentifier
                ? "Normative Rewrite + Identifier Assignment"
                : hasMissingModal
                    ? "Normative Rewrite"
                    : hasMissingIdentifier
                        ? "Identifier Assignment"
                        : hasFormulaDominant
                            ? "Procedure-to-Requirement Rewrite"
                            : "Human Review";

            var disposition = confidence < 0.25
                ? "KeepForAnalysisOnly"
                : confidence < 0.45
                    ? "KeepWithHumanReview"
                    : lowRiskIdentifierOnly
                        ? "PromoteForUseWithTracking"
                        : "PromoteForUse";

            var suggestedRewrite = BuildSuggestedRewrite(block.NormalizedText, hasMissingModal, hasMissingIdentifier, hasFormulaDominant);

            return (priority, fixType, suggestedRewrite, disposition);
        }

        private static string BuildSuggestedRewrite(string normalizedText, bool hasMissingModal, bool hasMissingIdentifier, bool hasFormulaDominant)
        {
            var cleaned = Regex.Replace(normalizedText ?? string.Empty, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return "The system shall define a verifiable requirement statement based on the cited source evidence.";
            }

            if (cleaned.Length > 180)
            {
                cleaned = cleaned.Substring(0, 180).TrimEnd() + "...";
            }

            if (hasFormulaDominant)
            {
                return $"The system shall satisfy the quantitative constraints and verification equations described in: {cleaned}";
            }

            if (hasMissingModal)
            {
                return $"The system shall satisfy the following requirement intent: {cleaned}";
            }

            if (hasMissingIdentifier)
            {
                return $"[Assign requirement ID] The system shall meet the requirement statement: {cleaned}";
            }

            if (!ModalVerbRegex.IsMatch(cleaned))
            {
                return $"The system shall meet the requirement statement: {cleaned}";
            }

            return cleaned;
        }

        private static List<string> BuildAnalysisFlags(DocumentBlock block, double confidence, ExtractionCandidateStatus status)
        {
            var flags = new List<string>();

            if (!block.HasRequirementLanguage)
            {
                flags.Add("Missing Modal Verb");
            }

            if (!block.HasExplicitIdentifier)
            {
                flags.Add("Missing Explicit Identifier");
            }

            if (IsLikelyFormulaBlock(block.NormalizedText) && !block.HasRequirementLanguage)
            {
                flags.Add("Formula/Procedure-Dominant Text");
            }

            if (block.NormalizedText.Length < 20)
            {
                flags.Add("Insufficient Context Length");
            }

            if (confidence < ReviewPromotionThreshold)
            {
                flags.Add("Very Low Composite Evidence");
            }
            else if (confidence < AcceptedConfidenceThreshold)
            {
                flags.Add("Needs Human Reconciliation");
            }

            if (status == ExtractionCandidateStatus.NeedsReview)
            {
                flags.Add("Prioritize In Requirement Analysis Prompt");
            }

            return flags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string BuildLowEvidenceReason(DocumentBlock block, double confidence)
        {
            var reasons = new List<string>();

            if (!block.HasRequirementLanguage)
            {
                reasons.Add("missing modal requirement verb (shall/must/will/should)");
            }

            if (!block.HasExplicitIdentifier)
            {
                reasons.Add("missing explicit requirement identifier/prefix");
            }

            if (IsLikelyFormulaBlock(block.NormalizedText) && !block.HasRequirementLanguage)
            {
                reasons.Add("formula-like/procedural text without normative requirement language");
            }

            if (block.NormalizedText.Length < 20)
            {
                reasons.Add("very short statement with insufficient context");
            }

            if (reasons.Count == 0)
            {
                reasons.Add("insufficient composite evidence");
            }

            return $"Low evidence ({confidence:F2}): {string.Join("; ", reasons)}";
        }

        private static bool IsStandaloneHeading(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (HeadingRegex.IsMatch(text))
            {
                return true;
            }

            return text.Length < 90 && text == text.ToUpperInvariant() && text.Any(char.IsLetter);
        }

        private static bool IsLikelyFormulaBlock(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (FormulaArtifactRegex.IsMatch(text))
            {
                return true;
            }

            var equalsCount = text.Count(ch => ch == '=');
            var digitCount = text.Count(char.IsDigit);
            return equalsCount >= 2 && digitCount >= 6;
        }

        private static (string? Prefix, string? Type, string? Evidence) ExtractPrefix(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return (null, null, null);
            }

            var explicitMatch = ExplicitIdRegex.Match(text);
            if (explicitMatch.Success)
            {
                var prefix = explicitMatch.Groups["prefix"].Value.Trim().Trim('.');
                return (prefix, InferPrefixType(prefix), explicitMatch.Value.Trim());
            }

            var headingMatch = HeadingRegex.Match(text);
            if (headingMatch.Success)
            {
                var prefix = headingMatch.Groups["prefix"].Value.Trim().Trim('.');
                return (prefix, "section", headingMatch.Value.Trim());
            }

            var standaloneMatch = StandaloneIdRegex.Match(text);
            if (standaloneMatch.Success)
            {
                var prefix = standaloneMatch.Groups["prefix"].Value.Trim().Trim('.');
                return (prefix, InferPrefixType(prefix), standaloneMatch.Value.Trim());
            }

            return (null, null, null);
        }

        private static string InferPrefixType(string prefix)
        {
            if (Regex.IsMatch(prefix, @"^\d+(?:\.\d+)+$", RegexOptions.Compiled))
            {
                return "section";
            }

            if (Regex.IsMatch(prefix, @"^[A-Za-z][A-Za-z0-9]*[_-][A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.Compiled))
            {
                return "document_id";
            }

            return "unknown";
        }

        private static DocumentExtractionStageMetrics CreateMetric(string stageName, int inputCount, int outputCount, int rejectedCount, TimeSpan elapsed, string notes)
        {
            return new DocumentExtractionStageMetrics
            {
                StageName = stageName,
                InputCount = inputCount,
                OutputCount = outputCount,
                RejectedCount = rejectedCount,
                Elapsed = elapsed,
                Notes = notes
            };
        }

        private static ReverseValidationVerdict BuildRuleBasedVerdict(Requirement requirement)
        {
            var evidence = string.Join(" | ", new[]
                {
                    requirement.SourcePrefixEvidence,
                    requirement.SourcePrefix,
                    requirement.TraceReference,
                    requirement.Description
                }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Take(2));

            var text = $"{requirement.Name} {requirement.Description}".Trim();
            var hasModal = ModalVerbRegex.IsMatch(text);
            var hasEvidence = !string.IsNullOrWhiteSpace(requirement.SourcePrefixEvidence) || !string.IsNullOrWhiteSpace(requirement.SourcePrefix);
            var confidence = Math.Clamp((hasModal ? 0.55 : 0.25) + (hasEvidence ? 0.25 : 0.0), 0.0, 1.0);

            return new ReverseValidationVerdict
            {
                SubjectId = GetRequirementSubjectId(requirement),
                IsLegit = hasModal || hasEvidence,
                Action = hasModal || hasEvidence ? ReverseValidationAction.Accept : ReverseValidationAction.Review,
                Confidence = confidence,
                Summary = hasModal
                    ? "Requirement language and evidence are present"
                    : "Missing strong requirement language; review recommended",
                Evidence = evidence,
                Issues = hasModal
                    ? new List<string>()
                    : new List<string> { "Requirement language is weak or absent" }
            };
        }

        private static ReverseValidationVerdict ApplyGroundingToVerdict(ReverseValidationVerdict verdict, SourceGroundingAssessment grounding)
        {
            if (grounding.IsGrounded)
            {
                verdict.Evidence = string.IsNullOrWhiteSpace(verdict.Evidence)
                    ? grounding.EvidenceSnippet
                    : $"{verdict.Evidence} | source_grounding: {grounding.EvidenceSnippet}";

                if (string.IsNullOrWhiteSpace(verdict.Summary))
                {
                    verdict.Summary = "Source grounding confirmed";
                }
                else if (!verdict.Summary.Contains("grounding", StringComparison.OrdinalIgnoreCase))
                {
                    verdict.Summary = $"{verdict.Summary}; source grounding confirmed";
                }

                verdict.Confidence = Math.Clamp(Math.Max(verdict.Confidence, grounding.MatchScore), 0.0, 1.0);
                return verdict;
            }

            if (verdict.Action == ReverseValidationAction.Accept)
            {
                verdict.Action = ReverseValidationAction.Review;
                verdict.IsLegit = false;
            }

            verdict.Confidence = Math.Min(verdict.Confidence, Math.Max(0.15, grounding.MatchScore));
            verdict.Issues ??= new List<string>();
            if (!verdict.Issues.Any(issue => issue.Contains("source grounding", StringComparison.OrdinalIgnoreCase)))
            {
                verdict.Issues.Add("No direct source grounding match found in attachment text");
            }

            if (string.IsNullOrWhiteSpace(verdict.Summary))
            {
                verdict.Summary = "Source grounding not found; manual review required";
            }
            else if (!verdict.Summary.Contains("grounding", StringComparison.OrdinalIgnoreCase))
            {
                verdict.Summary = $"{verdict.Summary}; source grounding not found";
            }

            verdict.Evidence = string.IsNullOrWhiteSpace(verdict.Evidence)
                ? grounding.EvidenceSnippet
                : $"{verdict.Evidence} | source_grounding: {grounding.EvidenceSnippet}";

            return verdict;
        }

        private static string GetRequirementSubjectId(Requirement requirement)
        {
            return !string.IsNullOrWhiteSpace(requirement.TraceReference)
                ? requirement.TraceReference
                : !string.IsNullOrWhiteSpace(requirement.GlobalId)
                    ? requirement.GlobalId
                    : !string.IsNullOrWhiteSpace(requirement.Item)
                        ? requirement.Item
                        : requirement.Name;
        }

        private string BuildReverseValidationPrompt(
            IReadOnlyList<Requirement> requirements,
            string documentContent,
            string documentName,
            IReadOnlyDictionary<string, SourceGroundingAssessment>? groundingBySubject = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are performing reverse validation on extracted requirements.");
            sb.AppendLine("Your task is to determine whether each candidate is fully supported by the source document and whether any part is suspicious, fabricated, or missing evidence.");
            sb.AppendLine("Primary check: confirm the attachment text contains supporting source language for each requirement (exact or semantically close). If source grounding is weak/absent, use action=review or reject.");
            sb.AppendLine("Do NOT invent missing data. If evidence is weak, mark the requirement for review.");
            sb.AppendLine();
            sb.AppendLine($"DOCUMENT: {documentName}");
            sb.AppendLine();
            sb.AppendLine("SOURCE DOCUMENT CONTEXT:");
            sb.AppendLine(Truncate(documentContent, 10000));
            sb.AppendLine();
            sb.AppendLine("REQUIREMENTS TO VALIDATE:");

            foreach (var requirement in requirements)
            {
                sb.AppendLine("---");
                sb.AppendLine($"subject_id: {GetRequirementSubjectId(requirement)}");
                sb.AppendLine($"name: {requirement.Name}");
                sb.AppendLine($"description: {Truncate(requirement.Description, 600)}");
                sb.AppendLine($"source_prefix: {requirement.SourcePrefix ?? "UNK"}");
                sb.AppendLine($"source_evidence: {Truncate(requirement.SourcePrefixEvidence, 250)}");
                sb.AppendLine($"trace_reference: {requirement.TraceReference}");

                if (groundingBySubject != null && groundingBySubject.TryGetValue(GetRequirementSubjectId(requirement), out var grounding))
                {
                    sb.AppendLine($"grounding_hint_match_score: {grounding.MatchScore:F2}");
                    sb.AppendLine($"grounding_hint_evidence: {Truncate(grounding.EvidenceSnippet, 250)}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Return ONLY valid JSON with this exact schema:");
            sb.AppendLine(@"{
  ""verdicts"": [
    {
      ""subject_id"": ""..."",
      ""is_legit"": true,
      ""action"": ""accept|review|reject"",
      ""confidence"": 0.0,
      ""summary"": ""..."",
      ""issues"": [""...""],
      ""evidence"": ""...""
    }
  ]
}");

            return sb.ToString();
        }

        private IReadOnlyList<ReverseValidationVerdict>? TryParseReverseValidationResponse(string? response, IReadOnlyList<Requirement> requirements)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return null;
            }

            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                return null;
            }

            var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            try
            {
                var envelope = JsonSerializer.Deserialize<ReverseValidationEnvelope>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (envelope?.Verdicts == null || envelope.Verdicts.Count == 0)
                {
                    return null;
                }

                var verdicts = new List<ReverseValidationVerdict>();
                foreach (var verdict in envelope.Verdicts)
                {
                    if (string.IsNullOrWhiteSpace(verdict.SubjectId))
                    {
                        continue;
                    }

                    verdict.Action = ParseAction(verdict.ActionText);
                    verdicts.Add(new ReverseValidationVerdict
                    {
                        SubjectId = verdict.SubjectId.Trim(),
                        IsLegit = verdict.IsLegit,
                        Action = verdict.Action,
                        Confidence = Math.Clamp(verdict.Confidence, 0.0, 1.0),
                        Summary = verdict.Summary ?? string.Empty,
                        Issues = verdict.Issues ?? new List<string>(),
                        Evidence = verdict.Evidence
                    });
                }

                if (verdicts.Count == 0)
                {
                    return null;
                }

                var knownIds = requirements.Select(GetRequirementSubjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return verdicts.Where(v => knownIds.Contains(v.SubjectId)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ExtractionFoundation] Could not parse reverse validation JSON; falling back to rule-based verdicts");
                return null;
            }
        }

        private static ReverseValidationAction ParseAction(string? action)
        {
            return action?.Trim().ToLowerInvariant() switch
            {
                "accept" => ReverseValidationAction.Accept,
                "review" => ReverseValidationAction.Review,
                "reject" => ReverseValidationAction.Reject,
                _ => ReverseValidationAction.Review
            };
        }

        private static string Truncate(string? value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = value.Trim();
            return text.Length <= maxChars ? text : text[..maxChars] + "...";
        }

        private static SourceGroundingAssessment AssessSourceGrounding(Requirement requirement, string documentContent)
        {
            if (string.IsNullOrWhiteSpace(documentContent))
            {
                return new SourceGroundingAssessment
                {
                    IsGrounded = false,
                    MatchScore = 0.0,
                    EvidenceSnippet = "Document content unavailable for grounding"
                };
            }

            var clause = ExtractRequirementClauseForGrounding(requirement);
            if (string.IsNullOrWhiteSpace(clause))
            {
                return new SourceGroundingAssessment
                {
                    IsGrounded = false,
                    MatchScore = 0.0,
                    EvidenceSnippet = "Requirement clause unavailable for grounding"
                };
            }

            var normalizedClause = NormalizeForGrounding(clause);
            var normalizedDoc = NormalizeForGrounding(documentContent);

            if (normalizedClause.Length >= 24 && normalizedDoc.Contains(normalizedClause, StringComparison.Ordinal))
            {
                return new SourceGroundingAssessment
                {
                    IsGrounded = true,
                    MatchScore = 0.95,
                    EvidenceSnippet = $"Exact normalized clause match: {Truncate(clause, 180)}"
                };
            }

            var tokens = normalizedClause
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length >= 4)
                .Distinct(StringComparer.Ordinal)
                .Take(16)
                .ToList();

            if (tokens.Count == 0)
            {
                return new SourceGroundingAssessment
                {
                    IsGrounded = false,
                    MatchScore = 0.1,
                    EvidenceSnippet = "Insufficient lexical tokens for grounding"
                };
            }

            var matched = tokens.Where(token => normalizedDoc.Contains(token, StringComparison.Ordinal)).ToList();
            var score = (double)matched.Count / tokens.Count;
            var grounded = score >= 0.60;

            return new SourceGroundingAssessment
            {
                IsGrounded = grounded,
                MatchScore = score,
                EvidenceSnippet = grounded
                    ? $"Token overlap grounded ({matched.Count}/{tokens.Count}): {string.Join(", ", matched.Take(6))}"
                    : $"Weak token overlap ({matched.Count}/{tokens.Count}); top tokens: {string.Join(", ", tokens.Take(6))}"
            };
        }

        private static string ExtractRequirementClauseForGrounding(Requirement requirement)
        {
            var raw = requirement.Description ?? requirement.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var firstLine = raw
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => !line.StartsWith("Source:", StringComparison.OrdinalIgnoreCase)
                    && !line.StartsWith("From:", StringComparison.OrdinalIgnoreCase)
                    && !line.StartsWith("Confidence:", StringComparison.OrdinalIgnoreCase));

            var clause = firstLine ?? raw.Trim();
            clause = Regex.Replace(clause, @"^\s*[A-Za-z0-9][A-Za-z0-9_.\-]{2,80}\s*[:\-]\s*", string.Empty);
            clause = Regex.Replace(clause, @"\s+", " ").Trim();
            return clause;
        }

        private static string NormalizeForGrounding(string text)
        {
            var lower = text.ToLowerInvariant();
            lower = Regex.Replace(lower, @"[^a-z0-9\s]", " ");
            lower = Regex.Replace(lower, @"\s+", " ").Trim();
            return lower;
        }

        private sealed class ReverseValidationEnvelope
        {
            public List<ReverseValidationCandidate> Verdicts { get; set; } = new();
        }

        private sealed class SourceGroundingAssessment
        {
            public bool IsGrounded { get; set; }
            public double MatchScore { get; set; }
            public string EvidenceSnippet { get; set; } = string.Empty;
        }

        private sealed class ReverseValidationCandidate
        {
            public string SubjectId { get; set; } = string.Empty;
            public bool IsLegit { get; set; }
            public string? ActionText { get; set; }
            public double Confidence { get; set; }
            public string? Summary { get; set; }
            public List<string>? Issues { get; set; }
            public string? Evidence { get; set; }

            [System.Text.Json.Serialization.JsonIgnore]
            public ReverseValidationAction Action { get; set; } = ReverseValidationAction.Review;
        }
    }
}