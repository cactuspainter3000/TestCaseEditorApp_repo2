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
    public class Diagnose_946_4C45_001A_Extraction
    {
        private JamaDocumentParserService _parser;

        [TestInitialize]
        public void Setup()
        {
            // Create minimal mocks for required dependencies
            var jamaService = new Mock<IJamaConnectService>();
            var llmService = new Mock<IAnythingLLMService>();
            var userSettingsService = new Mock<IUserSettingsService>();
            userSettingsService
                .Setup(s => s.LoadSettings())
                .Returns(new AppUserSettings { EnableAnythingLlmFallback = false });

            _parser = new JamaDocumentParserService(
                jamaService.Object,
                llmService.Object,
                directRagService: null,
                textGenerationService: null,
                derivationService: null,
                envelopeService: null,
                qualityService: null,
                complianceWrapper: null,
                abTestingFramework: null,
                telemetryService: null,
                ollamaProcessManager: null,
                ollamaStatusMonitor: null,
                documentExtractionService: null,
                atpStepParser: null,
                userSettingsService: userSettingsService.Object);
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task Analyze_946_4C45_001A_ExtractionBehavior()
        {
            // Load the problematic ATP document using absolute path
            var fixturePath = @"D:\TestCaseEditorApp_repo2\Tests\Fixtures\ATP\946-4C45-001A.docx";

            Assert.IsTrue(File.Exists(fixturePath), $"Fixture not found: {fixturePath}");

            Console.WriteLine($"Testing: {fixturePath}");
            Console.WriteLine($"File size: {new FileInfo(fixturePath).Length} bytes");
            Console.WriteLine("");

            // Extract using ParseLocalDocumentAsync (no AnythingLLM call, just deterministic extraction)
            Console.WriteLine("=== EXTRACTION RESULTS ===");
            var result = await _parser.ParseLocalDocumentAsync(fixturePath);

            Console.WriteLine($"Total extracted: {result.Count}");
            Console.WriteLine("");

            // Show first 20 requirements
            Console.WriteLine("=== REQUIREMENTS EXTRACTED ===");
            for (int i = 0; i < Math.Min(20, result.Count); i++)
            {
                Console.WriteLine($"[{i + 1}] {result[i].Description.Substring(0, Math.Min(120, result[i].Description.Length))}");
            }

            if (result.Count > 20)
            {
                Console.WriteLine($"... and {result.Count - 20} more");
            }

            Console.WriteLine("");
            Console.WriteLine("=== SUMMARY STATISTICS ===");

            // Check for requirement keywords
            int shallCount = 0, willCount = 0, mustCount = 0;
            foreach (var req in result)
            {
                if (req.Description.Contains("shall", StringComparison.OrdinalIgnoreCase)) shallCount++;
                if (req.Description.Contains("will", StringComparison.OrdinalIgnoreCase)) willCount++;
                if (req.Description.Contains("must", StringComparison.OrdinalIgnoreCase)) mustCount++;
            }
            Console.WriteLine($"'shall' requirements: {shallCount}");
            Console.WriteLine($"'will' requirements: {willCount}");
            Console.WriteLine($"'must' requirements: {mustCount}");

            Console.WriteLine("");
            Console.WriteLine("=== DOCUMENT STRUCTURE ANALYSIS ===");

            // Read raw Word XML to diagnose
            try
            {
                using (var zip = System.IO.Compression.ZipFile.OpenRead(fixturePath))
                {
                    var docEntry = zip.GetEntry("word/document.xml");
                    if (docEntry != null)
                    {
                        using (var stream = docEntry.Open())
                        using (var reader = new StreamReader(stream))
                        {
                            var xml = reader.ReadToEnd();
                            
                            // Count paragraphs, tables, lists
                            var paraCount = System.Text.RegularExpressions.Regex.Matches(xml, "<w:p>").Count;
                            var tableCount = System.Text.RegularExpressions.Regex.Matches(xml, "<w:tbl>").Count;
                            var listCount = System.Text.RegularExpressions.Regex.Matches(xml, "<w:numPr>").Count;
                            var textElements = System.Text.RegularExpressions.Regex.Matches(xml, "<w:t>").Count;

                            Console.WriteLine($"Raw paragraph count: {paraCount}");
                            Console.WriteLine($"Table count: {tableCount}");
                            Console.WriteLine($"List/numbered items: {listCount}");
                            Console.WriteLine($"Text elements: {textElements}");
                            Console.WriteLine($"Total XML size: {xml.Length} bytes");
                            
                            if (xml.Length < 5000)
                            {
                                Console.WriteLine("");
                                Console.WriteLine("=== DOCUMENT XML (FULL - SMALL FILE) ===");
                                Console.WriteLine(xml);
                            }
                            else
                            {
                                Console.WriteLine("");
                                Console.WriteLine("=== DOCUMENT XML (FIRST 4000 CHARS) ===");
                                Console.WriteLine(xml.Substring(0, 4000));
                                Console.WriteLine("...[truncated]...");
                                Console.WriteLine("");
                                Console.WriteLine("=== DOCUMENT XML (LAST 2000 CHARS) ===");
                                Console.WriteLine(xml.Substring(Math.Max(0, xml.Length - 2000)));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not read raw XML: {ex.Message}");
            }

            Assert.IsTrue(result.Count > 0, "No requirements extracted at all!");
        }
    }
}
