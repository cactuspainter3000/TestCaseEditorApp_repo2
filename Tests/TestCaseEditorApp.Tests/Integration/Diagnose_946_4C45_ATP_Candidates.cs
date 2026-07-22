using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests.Integration
{
    [TestClass]
    public class Diagnose_946_4C45_ATP_Candidates
    {
        [TestMethod]
        [Timeout(30000)]
        public async Task Analyze_Raw_ATP_Candidates_Before_Filtering()
        {
            var fixturePath = @"D:\TestCaseEditorApp_repo2\Tests\Fixtures\ATP\946-4C45-001A.docx";

            Assert.IsTrue(File.Exists(fixturePath), $"Fixture not found: {fixturePath}");

            Console.WriteLine($"Testing ATP extraction gate: {fixturePath}");
            Console.WriteLine("");

            // Read the document
            var fileBytes = await File.ReadAllBytesAsync(fixturePath);
            var parser = new JamaDocumentParserService(
                new Mock<IJamaConnectService>().Object,
                new Mock<IAnythingLLMService>().Object,
                userSettingsService: new Mock<IUserSettingsService>().Object);

            // We'll need to extract content using the parser's private methods
            // For now, use reflection to call ExtractAtpStepClausesAsync
            
            var localAttachment = new JamaAttachment
            {
                Id = -1,
                Name = Path.GetFileName(fixturePath),
                FileName = Path.GetFileName(fixturePath),
                MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileSize = fileBytes.Length
            };

            // Extract text from the attachment
            var extractMethod = typeof(JamaDocumentParserService).GetMethod(
                "ExtractAttachmentTextForIndexingAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(JamaAttachment), typeof(byte[]) },
                null);

            var documentContent = await (Task<string>)extractMethod.Invoke(parser, new object[] { localAttachment, fileBytes });

            Console.WriteLine($"Document content length: {documentContent?.Length ?? 0} chars");
            Console.WriteLine("");

            // Extract ATP candidates using reflection
            var extractAtpMethod = typeof(JamaDocumentParserService).GetMethod(
                "ExtractAtpStepClausesAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(string), typeof(System.Threading.CancellationToken) },
                null);

            var rawCandidates = await (Task<List<string>>)extractAtpMethod.Invoke(
                parser, 
                new object[] { documentContent, System.Threading.CancellationToken.None });

            Console.WriteLine($"=== RAW ATP CANDIDATES (BEFORE GATE FILTERING) ===");
            Console.WriteLine($"Total extracted: {rawCandidates.Count}");
            Console.WriteLine("");

            // Show all 34 raw candidates with details
            for (int i = 0; i < rawCandidates.Count; i++)
            {
                var candidate = rawCandidates[i];
                Console.WriteLine($"[{i + 1:D2}] Length={candidate.Length}");
                Console.WriteLine($"     {candidate.Substring(0, Math.Min(140, candidate.Length))}");
                Console.WriteLine("");
            }

            Assert.IsTrue(rawCandidates.Count > 0, "ATP extraction found no candidates!");
        }
    }
}
