using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Extraction;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.Services.Templates;

namespace TestCaseEditorApp.Tests.Integration
{
    [TestClass]
    public class JamaDocumentParserIntegrationTests
    {
        [TestMethod]
        public async Task ParseLocalDocumentAsync_RealAtpFixture_MaintainsCurrentExtractionBaseline()
        {
            var documentPath = ResolveRepoFilePath("Tests", "Fixtures", "ATP", "946-4DC0-001_C4B_DHM_ATP_Rev-.docx");
            Assert.IsTrue(File.Exists(documentPath), $"Expected ATP fixture at {documentPath}");

            var jamaService = new Mock<IJamaConnectService>();
            jamaService.SetupGet(s => s.IsConfigured).Returns(true);

            var anythingLlmService = new Mock<IAnythingLLMService>();

            var parser = new JamaDocumentParserService(
                jamaService.Object,
                anythingLlmService.Object,
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
                atpStepParser: null);

            var progressMessages = new List<string>();
            var result = await parser.ParseLocalDocumentAsync(
                documentPath,
                message => progressMessages.Add(message),
                null,
                CancellationToken.None);

            Assert.AreEqual(72, result.Count, "ATP local extraction baseline changed. Inspect staged parser filters before accepting a new count.");

            var descriptions = result
                .Select(requirement => requirement.Description ?? string.Empty)
                .ToList();

            Assert.AreEqual(
                1,
                descriptions.Count(text => text.Contains("+4P5VDC_HLDUP is within the range [4.37, 4.64] VDC", StringComparison.OrdinalIgnoreCase)),
                "Numeric-prefix duplicate collapse regressed for +4P5VDC_HLDUP.");

            Assert.AreEqual(
                1,
                descriptions.Count(text => text.Contains("+25VDC_HLDUP is within the range [24.7, 26.75] VDC", StringComparison.OrdinalIgnoreCase)),
                "Numeric-prefix duplicate collapse regressed for +25VDC_HLDUP.");

            Assert.AreEqual(
                1,
                descriptions.Count(text => text.Contains("+3.3VDC is within the range [3.17, 3.43] VDC for at least 110 ms", StringComparison.OrdinalIgnoreCase)),
                "Numeric-prefix duplicate collapse regressed for the +3.3VDC hold-up clause.");

            Assert.IsTrue(
                descriptions.Any(text => text.Contains("Minimum input impedance for equipment measuring voltages with test connector shall be 1 Mohm.", StringComparison.OrdinalIgnoreCase)),
                "The explicit equipment constraint clause should remain included.");

            Assert.IsFalse(
                descriptions.Any(text => text.Contains("display head level tests should always set PWR_WARN_F to GND prior to removing low voltage power", StringComparison.OrdinalIgnoreCase)),
                "Recommended power-down procedure guidance should remain filtered.");

            Assert.IsFalse(
                descriptions.Any(text => text.Contains("Test equipment circuitry used to drive the TTL inputs of the control card should be powered up at the same time", StringComparison.OrdinalIgnoreCase)),
                "Recommended power-up procedure guidance should remain filtered.");

            Assert.IsTrue(
                progressMessages.Any(message => message.Contains("kept 72", StringComparison.OrdinalIgnoreCase)
                    && message.Contains("deterministic-filtered 10", StringComparison.OrdinalIgnoreCase)
                    && message.Contains("numeric-prefix-deduped 27", StringComparison.OrdinalIgnoreCase)),
                "Expected the local extraction summary to report the current staged baseline metrics.");
        }

        [TestMethod]
        public async Task ParseAttachmentAsync_DirectRagTemplateForm_MapsExtractionTriageFields()
        {
            var attachment = new JamaAttachment
            {
                Id = 1201,
                FileName = "sample-atp.txt",
                Name = "sample-atp.txt",
                MimeType = "text/plain",
                FileSize = 512,
                Item = 9021
            };

            var attachmentContent = string.Join(Environment.NewLine, new[]
            {
                "Section 3.2.1 Power-on timing",
                "The test solution shall verify power-on timing within 5 seconds under nominal load.",
                "Acceptance criteria: pass when timing is <= 5 seconds."
            });

            var extractedEnvelopeJson = "{" +
                "\"requirements\":[{" +
                    "\"id\":\"REQ-001\"," +
                    "\"text\":\"The test solution shall verify power-on timing within 5 seconds under nominal load.\"," +
                    "\"category\":\"Functional\"," +
                    "\"page\":\"Page 4\"," +
                    "\"section\":\"3.2.1\"," +
                    "\"source_prefix\":\"3.2.1\"," +
                    "\"source_prefix_type\":\"section\"," +
                    "\"source_prefix_evidence\":\"Section 3.2.1 Power-on timing\"," +
                    "\"source_prefix_confidence\":0.91," +
                    "\"analysis_priority\":\"High\"," +
                    "\"fix_type\":\"Missing Modal Verb\"," +
                    "\"suggested_rewrite\":\"The test solution shall verify power-on timing within 5 seconds under nominal load.\"," +
                    "\"disposition_recommendation\":\"NeedsReview - verify exact tolerance\"," +
                    "\"confidence\":0.88" +
                "}]," +
                "\"metadata\":{\"total_requirements\":1,\"document_name\":\"sample-atp.txt\",\"extraction_method\":\"template_form\"}" +
            "}";

            var jamaService = new Mock<IJamaConnectService>();
            jamaService.SetupGet(s => s.IsConfigured).Returns(true);
            jamaService
                .Setup(s => s.DownloadAttachmentAsync(attachment.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Encoding.UTF8.GetBytes(attachmentContent));

            var anythingLlmService = new Mock<IAnythingLLMService>();
            var directRagService = new Mock<IDirectRagService>();
            directRagService.SetupGet(s => s.IsConfigured).Returns(true);
            directRagService
                .Setup(s => s.ValidateAttachmentIndexesAsync(
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyCollection<JamaAttachment>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<int, AttachmentIndexValidationResult>
                {
                    [attachment.Id] = new AttachmentIndexValidationResult
                    {
                        AttachmentId = attachment.Id,
                        State = AttachmentIndexValidationState.NotIndexed,
                        ScrapeBlocked = false,
                        Message = "Not indexed"
                    }
                });
            directRagService
                .Setup(s => s.GetProjectIndexStatsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentIndexStats { ProjectId = 77, TotalDocuments = 0, TotalChunks = 0 });
            directRagService
                .Setup(s => s.ClearProjectIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            directRagService
                .Setup(s => s.IndexDocumentAsync(It.IsAny<JamaAttachment>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            directRagService
                .Setup(s => s.GetRequirementAnalysisContextAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(attachmentContent);

            var textGenerationService = new Mock<ITextGenerationService>();
            textGenerationService
                .Setup(s => s.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string prompt, CancellationToken _) =>
                {
                    if (prompt.StartsWith("Test", StringComparison.OrdinalIgnoreCase))
                    {
                        return "ok";
                    }

                    return extractedEnvelopeJson;
                });

            var envelopeService = new OutputEnvelopeService(new EnvelopeSchemaService());
            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var extractionLogger = loggerFactory.CreateLogger<DocumentRequirementExtractionService>();
            var extractionService = new DocumentRequirementExtractionService(extractionLogger);

            var parser = new JamaDocumentParserService(
                jamaService.Object,
                anythingLlmService.Object,
                directRagService.Object,
                textGenerationService.Object,
                derivationService: null,
                envelopeService: envelopeService,
                qualityService: null,
                complianceWrapper: null,
                abTestingFramework: null,
                telemetryService: null,
                ollamaProcessManager: null,
                ollamaStatusMonitor: null,
                documentExtractionService: extractionService);

            var result = await parser.ParseAttachmentAsync(attachment, 77);

            Assert.AreEqual(1, result.Count, "Expected one parsed requirement from template-form envelope.");

            var requirement = result[0];
            Assert.AreEqual("REQ-001", requirement.GlobalId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(requirement.Description));
        }

        [TestMethod]
        public async Task TemplateFormInput_RealAtpFixture_PreservesTechnicalCanaries_AndExcludesProcedureGuidance()
        {
            var documentPath = ResolveRepoFilePath("Tests", "Fixtures", "ATP", "946-4DC0-001_C4B_DHM_ATP_Rev-.docx");
            Assert.IsTrue(File.Exists(documentPath), $"Expected ATP fixture at {documentPath}");

            var jamaService = new Mock<IJamaConnectService>();
            jamaService.SetupGet(s => s.IsConfigured).Returns(true);

            var anythingLlmService = new Mock<IAnythingLLMService>();
            var parser = new JamaDocumentParserService(
                jamaService.Object,
                anythingLlmService.Object,
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
                atpStepParser: null);

            var documentContent = await ExtractAttachmentTextForIndexingForTestAsync(parser, documentPath);
            Assert.IsFalse(string.IsNullOrWhiteSpace(documentContent), "Expected ATP fixture to yield extractable document content.");

            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var extractionLogger = loggerFactory.CreateLogger<DocumentRequirementExtractionService>();
            var extractionService = new DocumentRequirementExtractionService(extractionLogger);
            var foundation = await extractionService.AnalyzeAsync(documentContent, Path.GetFileName(documentPath));

            var contextContent = foundation.BuildPromptContext(12000);
            var templateInput = BuildTemplateExtractionInputForTest(documentContent, contextContent);

            Assert.IsFalse(string.IsNullOrWhiteSpace(templateInput), "Template-form input should not be empty for the ATP fixture.");
            Assert.IsTrue(
                templateInput.Contains("Minimum input impedance for equipment measuring voltages with test connector shall be 1 Mohm", StringComparison.OrdinalIgnoreCase),
                "Template-form input should retain the explicit impedance constraint.");
            Assert.IsTrue(
                templateInput.Contains("+3.3VDC is within the range [3.17, 3.43] VDC for at least 110 ms", StringComparison.OrdinalIgnoreCase),
                "Template-form input should retain the hold-up timing voltage constraint.");
            Assert.IsFalse(
                templateInput.Contains("display head level tests should always set PWR_WARN_F to GND prior to removing low voltage power", StringComparison.OrdinalIgnoreCase),
                "Template-form input should exclude recommended power-down procedure guidance.");
            Assert.IsFalse(
                templateInput.Contains("Test equipment circuitry used to drive the TTL inputs of the control card should be powered up at the same time", StringComparison.OrdinalIgnoreCase),
                "Template-form input should exclude recommended power-up procedure guidance.");
        }

        [TestMethod]
        public void NormalizeCandidateKey_GenericNumericClausePrefix_DedupesWithoutAptPhraseCoupling()
        {
            var method = typeof(JamaDocumentParserService).GetMethod(
                "NormalizeCandidateKey",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "Expected private NormalizeCandidateKey helper.");

            var withClauseNumber = "4.2.1 The system shall verify maintenance events are recorded within 5 seconds.";
            var withoutClauseNumber = "The system shall verify maintenance events are recorded within 5 seconds.";

            var argsA = new object[] { withClauseNumber, false };
            var argsB = new object[] { withoutClauseNumber, false };
            var keyWithPrefix = (string)method!.Invoke(null, argsA)!;
            var keyWithoutPrefix = (string)method!.Invoke(null, argsB)!;

            Assert.AreEqual(keyWithoutPrefix, keyWithPrefix, "Generic numbered clause prefixes should normalize to the same dedupe key.");
            Assert.AreEqual(true, (bool)argsA[1], "Expected numbered clause to be flagged as having a stripped numeric prefix.");
            Assert.AreEqual(false, (bool)argsB[1], "Expected unnumbered clause to remain unflagged.");

            var numericValueStart = "+3.3VDC shall remain within range during startup.";
            var argsC = new object[] { numericValueStart, false };
            var numericKey = (string)method.Invoke(null, argsC)!;
            Assert.IsTrue(
                numericKey.Contains("3 3vdc", StringComparison.OrdinalIgnoreCase),
                "Numeric measurement tokens at clause start should not be treated as removable numbering prefixes.");
            Assert.AreEqual(false, (bool)argsC[1], "Expected numeric measurement clause not to be flagged as a stripped numbering prefix.");
        }

        [TestMethod]
        public async Task ParseAttachmentAsync_RealAtpFixture_TemplateFormOutput_PreservesCanaries_EndToEnd()
        {
            var documentPath = ResolveRepoFilePath("Tests", "Fixtures", "ATP", "946-4DC0-001_C4B_DHM_ATP_Rev-.docx");
            Assert.IsTrue(File.Exists(documentPath), $"Expected ATP fixture at {documentPath}");

            const string impedanceCanary = "Minimum input impedance for equipment measuring voltages with test connector shall be 1 Mohm";
            const string holdupCanary = "+3.3VDC is within the range [3.17, 3.43] VDC for at least 110 ms";
            const string procedureDownCanary = "display head level tests should always set PWR_WARN_F to GND prior to removing low voltage power";
            const string procedureUpCanary = "Test equipment circuitry used to drive the TTL inputs of the control card should be powered up at the same time";

            var bootstrapJamaService = new Mock<IJamaConnectService>();
            bootstrapJamaService.SetupGet(s => s.IsConfigured).Returns(true);
            var bootstrapAnythingLlmService = new Mock<IAnythingLLMService>();

            var bootstrapParser = new JamaDocumentParserService(
                bootstrapJamaService.Object,
                bootstrapAnythingLlmService.Object,
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
                atpStepParser: null);

            var documentContent = await ExtractAttachmentTextForIndexingForTestAsync(bootstrapParser, documentPath);
            Assert.IsFalse(string.IsNullOrWhiteSpace(documentContent), "Expected ATP fixture to yield extractable document content.");

            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var extractionLogger = loggerFactory.CreateLogger<DocumentRequirementExtractionService>();
            var extractionService = new DocumentRequirementExtractionService(extractionLogger);
            var foundation = await extractionService.AnalyzeAsync(documentContent, Path.GetFileName(documentPath));
            var contextContent = foundation.BuildPromptContext(12000);
            var templateInput = BuildTemplateExtractionInputForTest(documentContent, contextContent);
            var directRagContext = BuildControlledDirectRagContextForTest(templateInput);

            Assert.IsTrue(templateInput.Contains(impedanceCanary, StringComparison.OrdinalIgnoreCase), "Template input should include impedance canary.");
            Assert.IsTrue(templateInput.Contains(holdupCanary, StringComparison.OrdinalIgnoreCase), "Template input should include hold-up canary.");
            Assert.IsFalse(templateInput.Contains(procedureDownCanary, StringComparison.OrdinalIgnoreCase), "Template input should exclude power-down procedure canary.");
            Assert.IsFalse(templateInput.Contains(procedureUpCanary, StringComparison.OrdinalIgnoreCase), "Template input should exclude power-up procedure canary.");

            var attachment = new JamaAttachment
            {
                Id = 946001,
                FileName = Path.GetFileName(documentPath),
                Name = Path.GetFileName(documentPath),
                MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileSize = new FileInfo(documentPath).Length,
                Item = 0
            };

            var attachmentBytes = await File.ReadAllBytesAsync(documentPath);

            var jamaService = new Mock<IJamaConnectService>();
            jamaService.SetupGet(s => s.IsConfigured).Returns(true);
            jamaService
                .Setup(s => s.DownloadAttachmentAsync(attachment.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(attachmentBytes);

            var anythingLlmService = new Mock<IAnythingLLMService>();
            var directRagService = new Mock<IDirectRagService>();
            directRagService.SetupGet(s => s.IsConfigured).Returns(true);
            directRagService
                .Setup(s => s.ValidateAttachmentIndexesAsync(
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyCollection<JamaAttachment>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<int, AttachmentIndexValidationResult>
                {
                    [attachment.Id] = new AttachmentIndexValidationResult
                    {
                        AttachmentId = attachment.Id,
                        State = AttachmentIndexValidationState.NotIndexed,
                        ScrapeBlocked = false,
                        Message = "Not indexed"
                    }
                });
            directRagService
                .Setup(s => s.GetProjectIndexStatsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentIndexStats { ProjectId = 77, TotalDocuments = 0, TotalChunks = 0 });
            directRagService
                .Setup(s => s.ClearProjectIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            directRagService
                .Setup(s => s.IndexDocumentAsync(It.IsAny<JamaAttachment>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            directRagService
                .Setup(s => s.GetRequirementAnalysisContextAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(directRagContext);

            string? extractionPrompt = null;
            var textGenerationService = new Mock<ITextGenerationService>();
            textGenerationService
                .Setup(s => s.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string prompt, CancellationToken _) =>
                {
                    if (prompt.StartsWith("Test", StringComparison.OrdinalIgnoreCase))
                    {
                        return "ok";
                    }

                    extractionPrompt = prompt;
                    return BuildSyntheticTemplateEnvelopeFromPrompt(prompt);
                });

            var envelopeService = new OutputEnvelopeService(new EnvelopeSchemaService());
            var parser = new JamaDocumentParserService(
                jamaService.Object,
                anythingLlmService.Object,
                directRagService.Object,
                textGenerationService.Object,
                derivationService: null,
                envelopeService: envelopeService,
                qualityService: null,
                complianceWrapper: null,
                abTestingFramework: null,
                telemetryService: null,
                ollamaProcessManager: null,
                ollamaStatusMonitor: null,
                documentExtractionService: extractionService,
                atpStepParser: null);

            var result = await parser.ParseAttachmentAsync(attachment, 77);

            Assert.IsFalse(string.IsNullOrWhiteSpace(extractionPrompt), "Expected template-form extraction prompt to be sent to text generation service.");
            var promptHasImpedance = extractionPrompt!.Contains(impedanceCanary, StringComparison.OrdinalIgnoreCase);
            var promptHasHoldup = PromptContainsHoldUpCanary(extractionPrompt);
            var promptHasProcedureDown = extractionPrompt.Contains(procedureDownCanary, StringComparison.OrdinalIgnoreCase);
            var promptHasProcedureUp = extractionPrompt.Contains(procedureUpCanary, StringComparison.OrdinalIgnoreCase);

            Assert.IsTrue(promptHasImpedance, "Prompt should retain impedance canary in CONTENT block.");
            Assert.IsFalse(promptHasProcedureDown, "Prompt should exclude power-down procedure canary.");
            Assert.IsFalse(promptHasProcedureUp, "Prompt should exclude power-up procedure canary.");

            var descriptions = result.Select(requirement => requirement.Description ?? string.Empty).ToList();
            var finalHasImpedance = descriptions.Any(text => text.Contains(impedanceCanary, StringComparison.OrdinalIgnoreCase));
            var finalHasHoldup = descriptions.Any(text => text.Contains(holdupCanary, StringComparison.OrdinalIgnoreCase));
            var finalHasProcedureDown = descriptions.Any(text => text.Contains(procedureDownCanary, StringComparison.OrdinalIgnoreCase));
            var finalHasProcedureUp = descriptions.Any(text => text.Contains(procedureUpCanary, StringComparison.OrdinalIgnoreCase));

            Assert.IsTrue(finalHasImpedance, "Final parsed output should retain impedance canary.");
            Assert.IsFalse(finalHasProcedureDown, "Final parsed output should exclude power-down procedure canary.");
            Assert.IsFalse(finalHasProcedureUp, "Final parsed output should exclude power-up procedure canary.");

            if (promptHasHoldup)
            {
                Assert.IsTrue(finalHasHoldup, "When hold-up canary reaches prompt content, final parsed output should retain it.");
            }

            Console.WriteLine(
                $"Downstream matrix: foundationCandidates={foundation.Candidates.Count}, templateInputLen={templateInput.Length}, finalRequirements={result.Count}, " +
                $"impedance(input/prompt/final)=true/{promptHasImpedance}/{finalHasImpedance}, " +
                $"holdup(input/prompt/final)=true/{promptHasHoldup}/{finalHasHoldup}, " +
                $"procDown(input/prompt/final)=false/{promptHasProcedureDown}/{finalHasProcedureDown}, " +
                $"procUp(input/prompt/final)=false/{promptHasProcedureUp}/{finalHasProcedureUp}");
        }

        [TestMethod]
        public async Task ParseAttachmentAsync_RealAtrFixture_RagContextStageMatrix_PreservesTechnicalCanary_WithoutProcedureReintroduction()
        {
            var documentPath = ResolveRepoFilePath("exports", "document-artifacts", "20260714-193344", "ATR_Export.docx");
            Assert.IsTrue(File.Exists(documentPath), $"Expected ATR fixture at {documentPath}");

            var bootstrapJamaService = new Mock<IJamaConnectService>();
            bootstrapJamaService.SetupGet(s => s.IsConfigured).Returns(true);
            var bootstrapAnythingLlmService = new Mock<IAnythingLLMService>();

            var bootstrapParser = new JamaDocumentParserService(
                bootstrapJamaService.Object,
                bootstrapAnythingLlmService.Object,
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
                atpStepParser: null);

            var documentContent = await ExtractAttachmentTextForIndexingForTestAsync(bootstrapParser, documentPath);
            Assert.IsFalse(string.IsNullOrWhiteSpace(documentContent), "Expected ATR fixture to yield extractable document content.");

            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var extractionLogger = loggerFactory.CreateLogger<DocumentRequirementExtractionService>();
            var extractionService = new DocumentRequirementExtractionService(extractionLogger);
            var foundation = await extractionService.AnalyzeAsync(documentContent, Path.GetFileName(documentPath));
            var contextContent = foundation.BuildPromptContext(12000);
            var templateInput = BuildTemplateExtractionInputForTest(documentContent, contextContent);

            var technicalCanary = foundation.Candidates
                .Select(candidate => (candidate.NormalizedText ?? string.Empty).Trim())
                .FirstOrDefault(text =>
                    text.Length >= 45 &&
                    Regex.IsMatch(text, @"\b(shall|must|will)\b", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(text, @"\bshould\b", RegexOptions.IgnoreCase));

            Assert.IsFalse(string.IsNullOrWhiteSpace(technicalCanary), "ATR fixture should yield a normative technical canary for stage-matrix validation.");
            Assert.IsTrue(
                templateInput.Contains(technicalCanary!, StringComparison.OrdinalIgnoreCase),
                "Template input should retain the ATR technical canary.");

            const string procedureGuidancePoison = "Recommended setup guidance should always sequence bench supplies before enabling any functional test rail.";
            var directRagContext = BuildControlledDirectRagContextForAtrTest(technicalCanary!, templateInput, procedureGuidancePoison);

            Assert.IsTrue(directRagContext.Contains(technicalCanary!, StringComparison.OrdinalIgnoreCase), "Controlled ATR RAG context should include the technical canary.");
            Assert.IsTrue(directRagContext.Contains(procedureGuidancePoison, StringComparison.OrdinalIgnoreCase), "Controlled ATR RAG context should include the late poisoned procedure guidance.");

            var attachment = new JamaAttachment
            {
                Id = 686001,
                FileName = Path.GetFileName(documentPath),
                Name = Path.GetFileName(documentPath),
                MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileSize = new FileInfo(documentPath).Length,
                Item = 0
            };

            var attachmentBytes = await File.ReadAllBytesAsync(documentPath);

            var jamaService = new Mock<IJamaConnectService>();
            jamaService.SetupGet(s => s.IsConfigured).Returns(true);
            jamaService
                .Setup(s => s.DownloadAttachmentAsync(attachment.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(attachmentBytes);

            var anythingLlmService = new Mock<IAnythingLLMService>();
            var directRagService = new Mock<IDirectRagService>();
            directRagService.SetupGet(s => s.IsConfigured).Returns(true);
            directRagService
                .Setup(s => s.ValidateAttachmentIndexesAsync(
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyCollection<JamaAttachment>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<int, AttachmentIndexValidationResult>
                {
                    [attachment.Id] = new AttachmentIndexValidationResult
                    {
                        AttachmentId = attachment.Id,
                        State = AttachmentIndexValidationState.NotIndexed,
                        ScrapeBlocked = false,
                        Message = "Not indexed"
                    }
                });
            directRagService
                .Setup(s => s.GetProjectIndexStatsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentIndexStats { ProjectId = 686, TotalDocuments = 0, TotalChunks = 0 });
            directRagService
                .Setup(s => s.ClearProjectIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            directRagService
                .Setup(s => s.IndexDocumentAsync(It.IsAny<JamaAttachment>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            directRagService
                .Setup(s => s.GetRequirementAnalysisContextAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(directRagContext);

            string? extractionPrompt = null;
            var textGenerationService = new Mock<ITextGenerationService>();
            textGenerationService
                .Setup(s => s.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string prompt, CancellationToken _) =>
                {
                    if (prompt.StartsWith("Test", StringComparison.OrdinalIgnoreCase))
                    {
                        return "ok";
                    }

                    extractionPrompt ??= prompt;
                    return BuildSyntheticTemplateEnvelopeForAtrPrompt(prompt, technicalCanary!, procedureGuidancePoison);
                });

            var envelopeService = new OutputEnvelopeService(new EnvelopeSchemaService());
            var parser = new JamaDocumentParserService(
                jamaService.Object,
                anythingLlmService.Object,
                directRagService.Object,
                textGenerationService.Object,
                derivationService: null,
                envelopeService: envelopeService,
                qualityService: null,
                complianceWrapper: null,
                abTestingFramework: null,
                telemetryService: null,
                ollamaProcessManager: null,
                ollamaStatusMonitor: null,
                documentExtractionService: extractionService,
                atpStepParser: null);

            var result = await parser.ParseAttachmentAsync(attachment, 686);

            Assert.IsFalse(string.IsNullOrWhiteSpace(extractionPrompt), "Expected ATR template-form extraction prompt to be sent to text generation service.");
            var promptHasTechnicalCanary = extractionPrompt!.Contains(technicalCanary!, StringComparison.OrdinalIgnoreCase);
            var promptHasProcedurePoison = extractionPrompt.Contains(procedureGuidancePoison, StringComparison.OrdinalIgnoreCase);

            Assert.IsTrue(promptHasTechnicalCanary, "Prompt should retain ATR technical canary from retrieved context.");
            Assert.IsFalse(promptHasProcedurePoison, "Prompt should not reintroduce late procedural guidance from low-priority retrieval tail.");

            var descriptions = result.Select(requirement => requirement.Description ?? string.Empty).ToList();
            var finalHasTechnicalCanary = result.Any(requirement => string.Equals(requirement.GlobalId, "REQ-ATR-TECH-001", StringComparison.OrdinalIgnoreCase));
            var finalHasProcedurePoison = descriptions.Any(text => text.Contains(procedureGuidancePoison, StringComparison.OrdinalIgnoreCase));

            Assert.IsTrue(finalHasTechnicalCanary, "Final parsed ATR output should retain technical canary grounded by retrieved context.");
            Assert.IsFalse(finalHasProcedurePoison, "Final parsed ATR output should not include procedural guidance poison from retrieval tail.");

            Console.WriteLine(
                $"ATR RAG matrix: foundationCandidates={foundation.Candidates.Count}, templateInputLen={templateInput.Length}, ragContextLen={directRagContext.Length}, finalRequirements={result.Count}, " +
                $"technical(input/rag/prompt/final)=true/true/{promptHasTechnicalCanary}/{finalHasTechnicalCanary}, " +
                $"procedurePoison(input/rag/prompt/final)=false/true/{promptHasProcedurePoison}/{finalHasProcedurePoison}");
        }

        [TestMethod]
        public async Task ParseAttachmentAsync_RealAtrFixture_LowContextCoverage_AppendsFocusedRecovery_AndPreservesTechnicalCanary()
        {
            var documentPath = ResolveRepoFilePath("exports", "document-artifacts", "20260714-193344", "ATR_Export.docx");
            Assert.IsTrue(File.Exists(documentPath), $"Expected ATR fixture at {documentPath}");

            var bootstrapJamaService = new Mock<IJamaConnectService>();
            bootstrapJamaService.SetupGet(s => s.IsConfigured).Returns(true);
            var bootstrapAnythingLlmService = new Mock<IAnythingLLMService>();

            var bootstrapParser = new JamaDocumentParserService(
                bootstrapJamaService.Object,
                bootstrapAnythingLlmService.Object,
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
                atpStepParser: null);

            var documentContent = await ExtractAttachmentTextForIndexingForTestAsync(bootstrapParser, documentPath);
            Assert.IsFalse(string.IsNullOrWhiteSpace(documentContent), "Expected ATR fixture to yield extractable document content.");

            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var extractionLogger = loggerFactory.CreateLogger<DocumentRequirementExtractionService>();
            var extractionService = new DocumentRequirementExtractionService(extractionLogger);
            var foundation = await extractionService.AnalyzeAsync(documentContent, Path.GetFileName(documentPath));

            var technicalCanary = foundation.Candidates
                .Select(candidate => (candidate.NormalizedText ?? string.Empty).Trim())
                .FirstOrDefault(text =>
                    text.Length >= 45 &&
                    Regex.IsMatch(text, @"\b(shall|must|will)\b", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(text, @"\bshould\b", RegexOptions.IgnoreCase));

            Assert.IsFalse(string.IsNullOrWhiteSpace(technicalCanary), "ATR fixture should yield a normative technical canary for low-context recovery validation.");

            var sparseDirectRagContext = "Sparse retrieval summary: mission monitoring constraints apply during verification phases.";
            Assert.IsTrue(sparseDirectRagContext.Length > 50, "Sparse RAG context should avoid the insufficient-context fallback path.");

            var attachment = new JamaAttachment
            {
                Id = 686002,
                FileName = Path.GetFileName(documentPath),
                Name = Path.GetFileName(documentPath),
                MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileSize = new FileInfo(documentPath).Length,
                Item = 0
            };

            var attachmentBytes = await File.ReadAllBytesAsync(documentPath);

            var jamaService = new Mock<IJamaConnectService>();
            jamaService.SetupGet(s => s.IsConfigured).Returns(true);
            jamaService
                .Setup(s => s.DownloadAttachmentAsync(attachment.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(attachmentBytes);

            var anythingLlmService = new Mock<IAnythingLLMService>();
            var directRagService = new Mock<IDirectRagService>();
            directRagService.SetupGet(s => s.IsConfigured).Returns(true);
            directRagService
                .Setup(s => s.ValidateAttachmentIndexesAsync(
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyCollection<JamaAttachment>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<int, AttachmentIndexValidationResult>
                {
                    [attachment.Id] = new AttachmentIndexValidationResult
                    {
                        AttachmentId = attachment.Id,
                        State = AttachmentIndexValidationState.NotIndexed,
                        ScrapeBlocked = false,
                        Message = "Not indexed"
                    }
                });
            directRagService
                .Setup(s => s.GetProjectIndexStatsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentIndexStats { ProjectId = 686, TotalDocuments = 0, TotalChunks = 0 });
            directRagService
                .Setup(s => s.ClearProjectIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            directRagService
                .Setup(s => s.IndexDocumentAsync(It.IsAny<JamaAttachment>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            directRagService
                .Setup(s => s.GetRequirementAnalysisContextAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sparseDirectRagContext);

            string? extractionPrompt = null;
            var textGenerationService = new Mock<ITextGenerationService>();
            textGenerationService
                .Setup(s => s.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string prompt, CancellationToken _) =>
                {
                    if (prompt.StartsWith("Test", StringComparison.OrdinalIgnoreCase))
                    {
                        return "ok";
                    }

                    extractionPrompt ??= prompt;
                    return BuildSyntheticTemplateEnvelopeForAtrPrompt(prompt, technicalCanary!, string.Empty);
                });

            var envelopeService = new OutputEnvelopeService(new EnvelopeSchemaService());
            var parser = new JamaDocumentParserService(
                jamaService.Object,
                anythingLlmService.Object,
                directRagService.Object,
                textGenerationService.Object,
                derivationService: null,
                envelopeService: envelopeService,
                qualityService: null,
                complianceWrapper: null,
                abTestingFramework: null,
                telemetryService: null,
                ollamaProcessManager: null,
                ollamaStatusMonitor: null,
                documentExtractionService: extractionService,
                atpStepParser: null);

            var result = await parser.ParseAttachmentAsync(attachment, 686);

            Assert.IsFalse(string.IsNullOrWhiteSpace(extractionPrompt), "Expected ATR template-form extraction prompt to be sent to text generation service.");
            Assert.IsTrue(extractionPrompt!.Length > sparseDirectRagContext.Length + 500, "Low coverage path should materially expand sparse context before prompt submission.");
            Assert.IsTrue(extractionPrompt.Contains(technicalCanary!, StringComparison.OrdinalIgnoreCase), "Focused recovery path should restore technical canary context into the prompt.");

            var finalHasTechnicalCanary = result.Any(requirement => string.Equals(requirement.GlobalId, "REQ-ATR-TECH-001", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(finalHasTechnicalCanary, "Final parsed ATR output should preserve technical canary when focused recovery path is active.");

            Console.WriteLine(
                $"ATR low-coverage matrix: foundationCandidates={foundation.Candidates.Count}, sparseContextLen={sparseDirectRagContext.Length}, finalRequirements={result.Count}, " +
                $"promptExpanded={extractionPrompt.Length > sparseDirectRagContext.Length + 500}, technicalInPrompt={extractionPrompt.Contains(technicalCanary!, StringComparison.OrdinalIgnoreCase)}, technicalInFinal={finalHasTechnicalCanary}");
        }

        [TestMethod]
        public async Task ParseAttachmentAsync_RealAtrFixture_LowContextCoverage_WithProcedurePoison_ExcludesPoisonFromPromptAndFinal()
        {
            var documentPath = ResolveRepoFilePath("exports", "document-artifacts", "20260714-193344", "ATR_Export.docx");
            Assert.IsTrue(File.Exists(documentPath), $"Expected ATR fixture at {documentPath}");

            var bootstrapJamaService = new Mock<IJamaConnectService>();
            bootstrapJamaService.SetupGet(s => s.IsConfigured).Returns(true);
            var bootstrapAnythingLlmService = new Mock<IAnythingLLMService>();

            var bootstrapParser = new JamaDocumentParserService(
                bootstrapJamaService.Object,
                bootstrapAnythingLlmService.Object,
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
                atpStepParser: null);

            var documentContent = await ExtractAttachmentTextForIndexingForTestAsync(bootstrapParser, documentPath);
            Assert.IsFalse(string.IsNullOrWhiteSpace(documentContent), "Expected ATR fixture to yield extractable document content.");

            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var extractionLogger = loggerFactory.CreateLogger<DocumentRequirementExtractionService>();
            var extractionService = new DocumentRequirementExtractionService(extractionLogger);
            var foundation = await extractionService.AnalyzeAsync(documentContent, Path.GetFileName(documentPath));

            var technicalCanary = foundation.Candidates
                .Select(candidate => (candidate.NormalizedText ?? string.Empty).Trim())
                .FirstOrDefault(text =>
                    text.Length >= 45 &&
                    Regex.IsMatch(text, @"\b(shall|must|will)\b", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(text, @"\bshould\b", RegexOptions.IgnoreCase));

            Assert.IsFalse(string.IsNullOrWhiteSpace(technicalCanary), "ATR fixture should yield a normative technical canary for poisoned low-context validation.");

            const string procedurePoison = "Recommended setup guidance should always sequence bench supplies before enabling any functional test rail.";
            var sparsePoisonedContext = $"Sparse retrieval summary: mission monitoring constraints apply during verification phases. {procedurePoison}";

            var attachment = new JamaAttachment
            {
                Id = 686003,
                FileName = Path.GetFileName(documentPath),
                Name = Path.GetFileName(documentPath),
                MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileSize = new FileInfo(documentPath).Length,
                Item = 0
            };

            var attachmentBytes = await File.ReadAllBytesAsync(documentPath);

            var jamaService = new Mock<IJamaConnectService>();
            jamaService.SetupGet(s => s.IsConfigured).Returns(true);
            jamaService
                .Setup(s => s.DownloadAttachmentAsync(attachment.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(attachmentBytes);

            var anythingLlmService = new Mock<IAnythingLLMService>();
            var directRagService = new Mock<IDirectRagService>();
            directRagService.SetupGet(s => s.IsConfigured).Returns(true);
            directRagService
                .Setup(s => s.ValidateAttachmentIndexesAsync(
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyCollection<JamaAttachment>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<int, AttachmentIndexValidationResult>
                {
                    [attachment.Id] = new AttachmentIndexValidationResult
                    {
                        AttachmentId = attachment.Id,
                        State = AttachmentIndexValidationState.NotIndexed,
                        ScrapeBlocked = false,
                        Message = "Not indexed"
                    }
                });
            directRagService
                .Setup(s => s.GetProjectIndexStatsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentIndexStats { ProjectId = 686, TotalDocuments = 0, TotalChunks = 0 });
            directRagService
                .Setup(s => s.ClearProjectIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            directRagService
                .Setup(s => s.IndexDocumentAsync(It.IsAny<JamaAttachment>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            directRagService
                .Setup(s => s.GetRequirementAnalysisContextAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sparsePoisonedContext);

            string? extractionPrompt = null;
            var textGenerationService = new Mock<ITextGenerationService>();
            textGenerationService
                .Setup(s => s.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string prompt, CancellationToken _) =>
                {
                    if (prompt.StartsWith("Test", StringComparison.OrdinalIgnoreCase))
                    {
                        return "ok";
                    }

                    extractionPrompt ??= prompt;
                    return BuildSyntheticTemplateEnvelopeForAtrPrompt(prompt, technicalCanary!, procedurePoison);
                });

            var envelopeService = new OutputEnvelopeService(new EnvelopeSchemaService());
            var parser = new JamaDocumentParserService(
                jamaService.Object,
                anythingLlmService.Object,
                directRagService.Object,
                textGenerationService.Object,
                derivationService: null,
                envelopeService: envelopeService,
                qualityService: null,
                complianceWrapper: null,
                abTestingFramework: null,
                telemetryService: null,
                ollamaProcessManager: null,
                ollamaStatusMonitor: null,
                documentExtractionService: extractionService,
                atpStepParser: null);

            var result = await parser.ParseAttachmentAsync(attachment, 686);

            Assert.IsFalse(string.IsNullOrWhiteSpace(extractionPrompt), "Expected ATR template-form extraction prompt to be sent to text generation service.");
            Assert.IsTrue(extractionPrompt!.Contains(technicalCanary!, StringComparison.OrdinalIgnoreCase), "Prompt should preserve ATR technical canary under low-context recovery.");
            Assert.IsFalse(extractionPrompt.Contains(procedurePoison, StringComparison.OrdinalIgnoreCase), "Prompt should exclude injected procedural poison guidance after recovery normalization.");

            var finalHasTechnicalCanary = result.Any(requirement => string.Equals(requirement.GlobalId, "REQ-ATR-TECH-001", StringComparison.OrdinalIgnoreCase));
            var finalHasProcedurePoison = result.Any(requirement =>
                (requirement.Description ?? string.Empty).Contains(procedurePoison, StringComparison.OrdinalIgnoreCase));

            Assert.IsTrue(finalHasTechnicalCanary, "Final parsed ATR output should preserve technical canary under poisoned low-context input.");
            Assert.IsFalse(finalHasProcedurePoison, "Final parsed ATR output should exclude injected procedural poison guidance.");

            Console.WriteLine(
                $"ATR low-coverage poison matrix: foundationCandidates={foundation.Candidates.Count}, sparsePoisonLen={sparsePoisonedContext.Length}, finalRequirements={result.Count}, " +
                $"technicalInPrompt={extractionPrompt.Contains(technicalCanary!, StringComparison.OrdinalIgnoreCase)}, poisonInPrompt={extractionPrompt.Contains(procedurePoison, StringComparison.OrdinalIgnoreCase)}, " +
                $"technicalInFinal={finalHasTechnicalCanary}, poisonInFinal={finalHasProcedurePoison}");
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

        private static async Task<string> ExtractAttachmentTextForIndexingForTestAsync(JamaDocumentParserService parser, string documentPath)
        {
            var method = typeof(JamaDocumentParserService).GetMethod(
                "ExtractAttachmentTextForIndexingAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "Expected private ExtractAttachmentTextForIndexingAsync method.");

            var fileInfo = new FileInfo(documentPath);
            var attachment = new JamaAttachment
            {
                Id = -(Math.Abs(documentPath.GetHashCode()) + 1),
                Name = fileInfo.Name,
                FileName = fileInfo.Name,
                MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileSize = fileInfo.Length,
                Item = 0,
                CreatedDate = DateTime.UtcNow.ToString("O")
            };

            var fileBytes = await File.ReadAllBytesAsync(documentPath);
            var task = (Task<string>)method!.Invoke(parser, new object[] { attachment, fileBytes })!;
            return await task;
        }

        private static string BuildTemplateExtractionInputForTest(string documentContent, string contextContent)
        {
            var method = typeof(JamaDocumentParserService).GetMethod(
                "BuildTemplateExtractionInput",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "Expected private BuildTemplateExtractionInput helper.");

            return (string)method!.Invoke(null, new object[] { documentContent, contextContent })!;
        }

        private static string BuildSyntheticTemplateEnvelopeFromPrompt(string prompt)
        {
            var requirements = new List<object>();

            if (prompt.Contains("Minimum input impedance for equipment measuring voltages with test connector shall be 1 Mohm", StringComparison.OrdinalIgnoreCase))
            {
                requirements.Add(new
                {
                    id = "REQ-IMP-001",
                    text = "Minimum input impedance for equipment measuring voltages with test connector shall be 1 Mohm.",
                    category = "Interface",
                    page = "Page 1",
                    section = "Electrical Constraints",
                    source_prefix = "UNK",
                    source_prefix_type = "unknown",
                    source_prefix_evidence = "Minimum input impedance...",
                    source_prefix_confidence = 0.9,
                    confidence = 0.9
                });
            }

            if (PromptContainsHoldUpCanary(prompt))
            {
                requirements.Add(new
                {
                    id = "REQ-HLD-001",
                    text = "+3.3VDC is within the range [3.17, 3.43] VDC for at least 110 ms.",
                    category = "Performance",
                    page = "Page 1",
                    section = "Power Hold-Up",
                    source_prefix = "UNK",
                    source_prefix_type = "unknown",
                    source_prefix_evidence = "+3.3VDC is within the range...",
                    source_prefix_confidence = 0.9,
                    confidence = 0.9
                });
            }

            if (prompt.Contains("display head level tests should always set PWR_WARN_F to GND prior to removing low voltage power", StringComparison.OrdinalIgnoreCase))
            {
                requirements.Add(new
                {
                    id = "REQ-PROC-DOWN",
                    text = "display head level tests should always set PWR_WARN_F to GND prior to removing low voltage power.",
                    category = "Test",
                    page = "Page 1",
                    section = "Recommended Power Down",
                    source_prefix = "UNK",
                    source_prefix_type = "unknown",
                    source_prefix_evidence = "display head level tests should always set PWR_WARN_F...",
                    source_prefix_confidence = 0.9,
                    confidence = 0.8
                });
            }

            if (prompt.Contains("Test equipment circuitry used to drive the TTL inputs of the control card should be powered up at the same time", StringComparison.OrdinalIgnoreCase))
            {
                requirements.Add(new
                {
                    id = "REQ-PROC-UP",
                    text = "Test equipment circuitry used to drive the TTL inputs of the control card should be powered up at the same time.",
                    category = "Test",
                    page = "Page 1",
                    section = "Recommended Power Up",
                    source_prefix = "UNK",
                    source_prefix_type = "unknown",
                    source_prefix_evidence = "Test equipment circuitry used to drive the TTL inputs...",
                    source_prefix_confidence = 0.9,
                    confidence = 0.8
                });
            }

            var envelope = new
            {
                requirements,
                metadata = new
                {
                    total_requirements = requirements.Count,
                    document_name = "fixture-atp.docx",
                    extraction_method = "template_form"
                }
            };

            return JsonSerializer.Serialize(envelope);
        }

        private static bool PromptContainsHoldUpCanary(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return Regex.IsMatch(
                text,
                @"\+?3\.3VDC\s+is\s+within\s+the\s+range\s*\[3\.17,\s*3\.43\]\s*VDC\s+for\s+at\s+least\s+110\s*ms",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        private static string BuildControlledDirectRagContextForTest(string templateInput)
        {
            const string canaryHeader = "Minimum input impedance for equipment measuring voltages with test connector shall be 1 Mohm.\n+3.3VDC is within the range [3.17, 3.43] VDC for at least 110 ms.\n";

            var baseContext = string.IsNullOrWhiteSpace(templateInput)
                ? "Minimum input impedance for equipment measuring voltages with test connector shall be 1 Mohm. +3.3VDC is within the range [3.17, 3.43] VDC for at least 110 ms."
                : templateInput;

            var builder = new StringBuilder();
            builder.Append(canaryHeader);
            while (builder.Length < 15050)
            {
                builder.Append(baseContext);
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static string BuildControlledDirectRagContextForAtrTest(string technicalCanary, string templateInput, string lateProcedureGuidance)
        {
            var baseContext = string.IsNullOrWhiteSpace(templateInput)
                ? technicalCanary
                : templateInput;

            var builder = new StringBuilder();
            builder.AppendLine($"[Top-Ranked Technical Context]\n{technicalCanary}");
            builder.AppendLine(baseContext);

            while (builder.Length < 12350)
            {
                builder.AppendLine(baseContext);
            }

            builder.AppendLine("[Low-Ranked Tail Guidance]");
            builder.AppendLine(lateProcedureGuidance);

            return builder.ToString();
        }

        private static string BuildSyntheticTemplateEnvelopeForAtrPrompt(string prompt, string technicalCanary, string procedureGuidancePoison)
        {
            var requirements = new List<object>();

            if (prompt.Contains(technicalCanary, StringComparison.OrdinalIgnoreCase))
            {
                requirements.Add(new
                {
                    id = "REQ-ATR-TECH-001",
                    text = technicalCanary,
                    category = "Functional",
                    page = "Page 1",
                    section = "ATR Derived",
                    source_prefix = "UNK",
                    source_prefix_type = "unknown",
                    source_prefix_evidence = "Top-ranked technical context",
                    source_prefix_confidence = 0.9,
                    confidence = 0.9
                });
            }

            if (!string.IsNullOrWhiteSpace(procedureGuidancePoison) &&
                prompt.Contains(procedureGuidancePoison, StringComparison.OrdinalIgnoreCase))
            {
                requirements.Add(new
                {
                    id = "REQ-ATR-PROC-001",
                    text = procedureGuidancePoison,
                    category = "Test",
                    page = "Page 1",
                    section = "Low-Ranked Tail Guidance",
                    source_prefix = "UNK",
                    source_prefix_type = "unknown",
                    source_prefix_evidence = "Low-ranked tail guidance",
                    source_prefix_confidence = 0.7,
                    confidence = 0.7
                });
            }

            var envelope = new
            {
                requirements,
                metadata = new
                {
                    total_requirements = requirements.Count,
                    document_name = "fixture-atr.docx",
                    extraction_method = "template_form"
                }
            };

            return JsonSerializer.Serialize(envelope);
        }
    }
}
