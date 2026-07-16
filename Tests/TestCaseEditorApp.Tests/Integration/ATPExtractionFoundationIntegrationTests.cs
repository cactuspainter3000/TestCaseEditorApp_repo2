using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Extraction;

namespace TestCaseEditorApp.Tests.Integration
{
    [TestClass]
    public class ATPExtractionFoundationIntegrationTests
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public void Analyze_RealATPDocument_ProducesCandidates_WithoutSyntheticDocHeaders()
        {
            var documentPath = ResolveRepoFilePath("Tests", "Fixtures", "ATP", "946-4DC0-001_C4B_DHM_ATP_Rev-.docx");
            Assert.IsTrue(File.Exists(documentPath), $"Expected ATP fixture at {documentPath}");

            var extractedText = ExtractWordDocumentText(documentPath);
            Assert.IsFalse(string.IsNullOrWhiteSpace(extractedText), "ATP fixture should yield extractable document text.");

            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var logger = loggerFactory.CreateLogger<DocumentRequirementExtractionService>();
            var service = new DocumentRequirementExtractionService(logger);

            var result = service.AnalyzeAsync(extractedText, Path.GetFileName(documentPath)).GetAwaiter().GetResult();

            Console.WriteLine("--- ATP Blocks ---");
            foreach (var block in result.Blocks.Take(20))
            {
                Console.WriteLine($"[{block.BlockIndex:D3}] {block.Kind} {block.StartLine}-{block.EndLine} prefix={block.SourcePrefix ?? "UNK"} score={block.EvidenceScore:F2}");
                Console.WriteLine(block.NormalizedText);
                Console.WriteLine();
            }

            Console.WriteLine("--- ATP Candidates ---");
            Console.WriteLine(result.BuildEvidenceLedger(20));

            TestContext.WriteLine($"Blocks: {result.Blocks.Count}");
            TestContext.WriteLine($"Candidates: {result.Candidates.Count}");
            TestContext.WriteLine($"Accepted: {result.AcceptedCandidateCount}, Review: {result.ReviewCandidateCount}, Rejected: {result.RejectedCandidateCount}");
            TestContext.WriteLine(result.BuildEvidenceLedger(8));

            var rejected = result.Candidates
                .Where(candidate => candidate.Status == ExtractionCandidateStatus.Rejected)
                .ToList();
            var rejectionHistogram = rejected
                .GroupBy(candidate => string.IsNullOrWhiteSpace(candidate.RejectionReason) ? "<no reason>" : candidate.RejectionReason!)
                .OrderByDescending(group => group.Count())
                .Take(8)
                .Select(group => $"{group.Count(),2} x {group.Key}")
                .ToList();

            var promotedReviewCount = result.Candidates.Count(candidate =>
                candidate.Status == ExtractionCandidateStatus.NeedsReview &&
                candidate.Confidence < 0.50);

            var analysisFlagHistogram = result.Candidates
                .Where(candidate => candidate.AnalysisFlags != null && candidate.AnalysisFlags.Count > 0)
                .SelectMany(candidate => candidate.AnalysisFlags)
                .GroupBy(flag => flag)
                .OrderByDescending(group => group.Count())
                .Take(10)
                .Select(group => $"{group.Count(),2} x {group.Key}")
                .ToList();

            TestContext.WriteLine("Rejection Criteria Histogram (top reasons):");
            if (rejectionHistogram.Count == 0)
            {
                TestContext.WriteLine("  <none>");
            }
            else
            {
                foreach (var line in rejectionHistogram)
                {
                    TestContext.WriteLine($"  {line}");
                }
            }
            TestContext.WriteLine($"Borderline candidates promoted to human review (confidence < 0.50): {promotedReviewCount}");
            TestContext.WriteLine("Analysis Flag Histogram (top flags):");
            if (analysisFlagHistogram.Count == 0)
            {
                TestContext.WriteLine("  <none>");
            }
            else
            {
                foreach (var line in analysisFlagHistogram)
                {
                    TestContext.WriteLine($"  {line}");
                }
            }

            var missingModalCandidates = result.Candidates
                .Where(candidate => candidate.AnalysisFlags.Contains("Missing Modal Verb"))
                .ToList();
            var missingIdentifierCandidates = result.Candidates
                .Where(candidate => candidate.AnalysisFlags.Contains("Missing Explicit Identifier"))
                .ToList();

            Assert.IsTrue(result.RejectedCandidateCount == 0,
                "Candidates should be retained for analysis/review rather than hard-rejected.");
            Assert.IsTrue(
                missingModalCandidates.All(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.SuggestedRewrite)
                    && candidate.SuggestedRewrite.Contains("shall", StringComparison.OrdinalIgnoreCase)),
                "Missing-modal candidates should include a suggested rewrite with normative language.");
            Assert.IsTrue(
                missingIdentifierCandidates.All(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.FixType)
                    && candidate.FixType.Contains("Identifier", StringComparison.OrdinalIgnoreCase)),
                "Missing-identifier candidates should include identifier-focused fix guidance.");
            Assert.IsTrue(
                result.Candidates.Where(candidate => candidate.AnalysisFlags.Count > 0).All(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.AnalysisPriority)
                    && !string.IsNullOrWhiteSpace(candidate.DispositionRecommendation)),
                "Flagged candidates should include triage priority and disposition metadata.");

            Assert.IsTrue(result.Blocks.Count > 0, "The foundation should segment the ATP document into blocks.");
            Assert.IsTrue(result.Candidates.Count > 0, "The foundation should harvest at least one requirement candidate from the ATP document.");
            Assert.IsTrue(result.Candidates.Any(candidate => candidate.Status is ExtractionCandidateStatus.Accepted or ExtractionCandidateStatus.NeedsReview),
                "The ATP document should produce at least one candidate worthy of acceptance or review.");
            Assert.IsFalse(
                result.Candidates.Any(candidate => !string.IsNullOrWhiteSpace(candidate.SourcePrefix) && Regex.IsMatch(candidate.SourcePrefix, @"^DOC-\d{3}$", RegexOptions.IgnoreCase)),
                "Synthetic DOC-### source prefixes must never be produced.");
            Assert.IsFalse(
                result.Candidates.Any(candidate =>
                    (!string.IsNullOrWhiteSpace(candidate.RawText) && Regex.IsMatch(candidate.RawText, @"\b(TOC|PAGEREF)\b", RegexOptions.IgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(candidate.NormalizedText) && Regex.IsMatch(candidate.NormalizedText, @"\b(TOC|PAGEREF)\b", RegexOptions.IgnoreCase)) ||
                    candidate.EvidenceSnippets.Any(snippet => !string.IsNullOrWhiteSpace(snippet) && Regex.IsMatch(snippet, @"\b(TOC|PAGEREF)\b", RegexOptions.IgnoreCase))),
                "Extractor candidates should not contain TOC or page-reference artifacts.");
        }

        [TestMethod]
        public void Analyze_ATRExportDocument_ProducesCandidates_WithTriageMetadata()
        {
            var documentPath = ResolveRepoFilePath("exports", "document-artifacts", "20260714-193344", "ATR_Export.docx");
            Assert.IsTrue(File.Exists(documentPath), $"Expected ATR export fixture at {documentPath}");

            var extractedText = ExtractWordDocumentText(documentPath);
            Assert.IsFalse(string.IsNullOrWhiteSpace(extractedText), "ATR export should yield extractable document text.");

            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var logger = loggerFactory.CreateLogger<DocumentRequirementExtractionService>();
            var service = new DocumentRequirementExtractionService(logger);

            var result = service.AnalyzeAsync(extractedText, Path.GetFileName(documentPath)).GetAwaiter().GetResult();

            TestContext.WriteLine($"ATR Blocks: {result.Blocks.Count}");
            TestContext.WriteLine($"ATR Candidates: {result.Candidates.Count}");
            TestContext.WriteLine($"ATR Accepted: {result.AcceptedCandidateCount}, Review: {result.ReviewCandidateCount}, Rejected: {result.RejectedCandidateCount}");
            TestContext.WriteLine(result.BuildEvidenceLedger(8));

            var reviewCandidates = result.Candidates
                .Where(candidate => candidate.Status == ExtractionCandidateStatus.NeedsReview)
                .ToList();

            var reviewFlagHistogram = reviewCandidates
                .SelectMany(candidate => candidate.AnalysisFlags ?? new List<string>())
                .GroupBy(flag => string.IsNullOrWhiteSpace(flag) ? "<none>" : flag)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Count(),3} x {group.Key}")
                .ToList();

            var reviewPriorityHistogram = reviewCandidates
                .GroupBy(candidate => string.IsNullOrWhiteSpace(candidate.AnalysisPriority) ? "<none>" : candidate.AnalysisPriority)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Count(),3} x {group.Key}")
                .ToList();

            var reviewFixTypeHistogram = reviewCandidates
                .GroupBy(candidate => string.IsNullOrWhiteSpace(candidate.FixType) ? "<none>" : candidate.FixType)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Count(),3} x {group.Key}")
                .ToList();

            var reviewDispositionHistogram = reviewCandidates
                .GroupBy(candidate => string.IsNullOrWhiteSpace(candidate.DispositionRecommendation) ? "<none>" : candidate.DispositionRecommendation)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Count(),3} x {group.Key}")
                .ToList();

            var reviewConfidenceBuckets = new[]
            {
                new { Label = "<0.25", Count = reviewCandidates.Count(candidate => candidate.Confidence < 0.25) },
                new { Label = "0.25-0.45", Count = reviewCandidates.Count(candidate => candidate.Confidence >= 0.25 && candidate.Confidence < 0.45) },
                new { Label = "0.45-0.65", Count = reviewCandidates.Count(candidate => candidate.Confidence >= 0.45 && candidate.Confidence < 0.65) },
                new { Label = "0.65-0.75", Count = reviewCandidates.Count(candidate => candidate.Confidence >= 0.65 && candidate.Confidence < 0.75) },
                new { Label = ">=0.75", Count = reviewCandidates.Count(candidate => candidate.Confidence >= 0.75) }
            };

            TestContext.WriteLine("ATR Review Flag Histogram:");
            foreach (var line in reviewFlagHistogram)
            {
                TestContext.WriteLine($"  {line}");
            }

            TestContext.WriteLine("ATR Review Priority Histogram:");
            foreach (var line in reviewPriorityHistogram)
            {
                TestContext.WriteLine($"  {line}");
            }

            TestContext.WriteLine("ATR Review FixType Histogram:");
            foreach (var line in reviewFixTypeHistogram)
            {
                TestContext.WriteLine($"  {line}");
            }

            TestContext.WriteLine("ATR Review Disposition Histogram:");
            foreach (var line in reviewDispositionHistogram)
            {
                TestContext.WriteLine($"  {line}");
            }

            TestContext.WriteLine("ATR Review Confidence Buckets:");
            foreach (var bucket in reviewConfidenceBuckets)
            {
                TestContext.WriteLine($"  {bucket.Count,3} x {bucket.Label}");
            }

            TestContext.WriteLine("ATR Review Sample (first 8):");
            foreach (var candidate in reviewCandidates.Take(8))
            {
                TestContext.WriteLine(
                    $"  {candidate.CandidateId} | confidence={candidate.Confidence:F2} | prefix={candidate.SourcePrefix ?? "UNK"} | text={candidate.NormalizedText}");
            }

            Assert.IsTrue(result.Blocks.Count > 0, "ATR export should segment into blocks.");
            Assert.IsTrue(result.Candidates.Count > 0, "ATR export should produce at least one requirement candidate.");
            Assert.IsTrue(result.RejectedCandidateCount == 0,
                "ATR candidates should be retained for analysis/review instead of hard-rejected.");
            Assert.IsTrue(
                result.Candidates.Where(candidate => candidate.AnalysisFlags.Count > 0).All(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.AnalysisPriority)
                    && !string.IsNullOrWhiteSpace(candidate.DispositionRecommendation)),
                "Flagged ATR candidates should include triage priority and disposition metadata.");
            Assert.IsFalse(
                result.Candidates.Any(candidate => !string.IsNullOrWhiteSpace(candidate.SourcePrefix) && Regex.IsMatch(candidate.SourcePrefix, @"^DOC-\d{3}$", RegexOptions.IgnoreCase)),
                "ATR extraction must not emit synthetic DOC-### source prefixes.");
            Assert.IsFalse(
                result.Candidates.Any(candidate =>
                    (!string.IsNullOrWhiteSpace(candidate.RawText) && Regex.IsMatch(candidate.RawText, @"\b(TOC|PAGEREF)\b", RegexOptions.IgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(candidate.NormalizedText) && Regex.IsMatch(candidate.NormalizedText, @"\b(TOC|PAGEREF)\b", RegexOptions.IgnoreCase)) ||
                    candidate.EvidenceSnippets.Any(snippet => !string.IsNullOrWhiteSpace(snippet) && Regex.IsMatch(snippet, @"\b(TOC|PAGEREF)\b", RegexOptions.IgnoreCase))),
                "ATR extraction should not surface TOC/PAGEREF artifacts.");
        }

        [TestMethod]
        public void Analyze_RealATPDocument_ExcludesRecommendedProcedureGuidance_ButKeepsTechnicalConstraints()
        {
            var documentPath = ResolveRepoFilePath("Tests", "Fixtures", "ATP", "946-4DC0-001_C4B_DHM_ATP_Rev-.docx");
            Assert.IsTrue(File.Exists(documentPath), $"Expected ATP fixture at {documentPath}");

            var extractedText = ExtractWordDocumentText(documentPath);
            Assert.IsFalse(string.IsNullOrWhiteSpace(extractedText), "ATP fixture should yield extractable document text.");

            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var logger = loggerFactory.CreateLogger<DocumentRequirementExtractionService>();
            var service = new DocumentRequirementExtractionService(logger);

            var result = service.AnalyzeAsync(extractedText, Path.GetFileName(documentPath)).GetAwaiter().GetResult();

            var candidateTexts = result.Candidates
                .Select(candidate => candidate.NormalizedText ?? string.Empty)
                .ToList();

            Assert.AreEqual(
                1,
                candidateTexts.Count(text => text.Contains("Minimum input impedance for equipment measuring voltages with test connector shall be 1 Mohm", StringComparison.OrdinalIgnoreCase)),
                "The explicit impedance constraint should remain harvestable from the ATP fixture.");

            Assert.IsFalse(
                candidateTexts.Any(text => text.Contains("display head level tests should always set PWR_WARN_F to GND prior to removing low voltage power", StringComparison.OrdinalIgnoreCase)),
                "Recommended power-down procedure guidance should not be promoted as a production extraction candidate.");

            Assert.IsFalse(
                candidateTexts.Any(text => text.Contains("Test equipment circuitry used to drive the TTL inputs of the control card should be powered up at the same time", StringComparison.OrdinalIgnoreCase)),
                "Recommended power-up procedure guidance should not be promoted as a production extraction candidate.");
        }

        [TestMethod]
        public void Analyze_ATRExportDocument_NumberedNonVerificationClause_IsNotOverNormalized()
        {
            var documentPath = ResolveRepoFilePath("exports", "document-artifacts", "20260714-193344", "ATR_Export.docx");
            Assert.IsTrue(File.Exists(documentPath), $"Expected ATR export fixture at {documentPath}");

            var extractedText = ExtractWordDocumentText(documentPath);
            Assert.IsFalse(string.IsNullOrWhiteSpace(extractedText), "ATR export should yield extractable document text.");

            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var logger = loggerFactory.CreateLogger<DocumentRequirementExtractionService>();
            var service = new DocumentRequirementExtractionService(logger);

            var result = service.AnalyzeAsync(extractedText, Path.GetFileName(documentPath)).GetAwaiter().GetResult();
            var nonVerificationClause = result.Candidates
                .Select(candidate => candidate.NormalizedText ?? string.Empty)
                .FirstOrDefault(text =>
                    Regex.IsMatch(text, @"\b(shall|must|should|will)\b", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(text, @"\bshall\s+verify\b", RegexOptions.IgnoreCase));

            Assert.IsFalse(
                string.IsNullOrWhiteSpace(nonVerificationClause),
                "ATR fixture should provide at least one non-verification normative clause for normalization validation.");

            var normalizeMethod = typeof(JamaDocumentParserService).GetMethod(
                "NormalizeCandidateKey",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(normalizeMethod, "Expected private NormalizeCandidateKey helper.");

            var numberedClause = $"7.4.2 {nonVerificationClause}";
            var numberedArgs = new object[] { numberedClause, false };
            var plainArgs = new object[] { nonVerificationClause!, false };

            var numberedKey = (string)normalizeMethod!.Invoke(null, numberedArgs)!;
            var plainKey = (string)normalizeMethod.Invoke(null, plainArgs)!;

            Assert.AreNotEqual(
                plainKey,
                numberedKey,
                "Numbered ATR non-verification clauses should not be collapsed by verification-prefix normalization.");
            Assert.AreEqual(false, (bool)numberedArgs[1], "Expected numbered ATR non-verification clause to remain unstripped.");
            Assert.AreEqual(false, (bool)plainArgs[1], "Expected unnumbered ATR non-verification clause to remain unflagged.");
        }

        private static string ExtractWordDocumentText(string documentPath)
        {
            using var stream = File.OpenRead(documentPath);
            using var wordDocument = WordprocessingDocument.Open(stream, false);
            var body = wordDocument.MainDocumentPart?.Document?.Body;
            if (body == null)
            {
                return string.Empty;
            }

            var lines = body
                .Elements<Paragraph>()
                .Select(paragraph => paragraph.InnerText?.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Cast<string>()
                .ToList();

            foreach (var table in body.Elements<Table>())
            {
                foreach (var row in table.Elements<TableRow>())
                {
                    var rowText = string.Join("\t", row.Elements<TableCell>().Select(cell => cell.InnerText?.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)));
                    if (!string.IsNullOrWhiteSpace(rowText))
                    {
                        lines.Add(rowText);
                    }
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string ResolveRepoFilePath(params string[] relativeParts)
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "TestCaseEditorApp.csproj")))
                {
                    return Path.Combine(current.FullName, Path.Combine(relativeParts));
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root from test base directory.");
        }
    }
}