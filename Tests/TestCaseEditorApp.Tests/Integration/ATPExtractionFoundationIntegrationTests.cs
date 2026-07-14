using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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