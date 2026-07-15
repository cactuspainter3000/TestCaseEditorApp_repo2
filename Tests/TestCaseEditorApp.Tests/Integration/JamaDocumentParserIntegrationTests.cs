using System;
using System.Collections.Generic;
using System.Text;
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
            Assert.AreEqual("High", requirement.AnalysisPriority);
            Assert.AreEqual("Missing Modal Verb", requirement.FixType);
            Assert.AreEqual("NeedsReview - verify exact tolerance", requirement.DispositionRecommendation);
            Assert.IsFalse(string.IsNullOrWhiteSpace(requirement.SuggestedRewrite));
            Assert.AreEqual("3.2.1", requirement.SourcePrefix);
            Assert.AreEqual("Section 3.2.1 Power-on timing", requirement.SourcePrefixEvidence);
            Assert.IsTrue(requirement.SourcePrefixConfidence.HasValue && requirement.SourcePrefixConfidence.Value >= 0.9);
        }
    }
}
