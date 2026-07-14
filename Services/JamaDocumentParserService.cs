using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.Services.Templates; // Template Form Architecture (Phase 6)
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Spreadsheet;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for parsing Jama attachments (PDFs, Word, Excel) using AnythingLLM
    /// Extracts requirements and metadata from source documents
    /// Implements IJamaDocumentParserService following Architectural Guide AI patterns
    /// </summary>
    public class JamaDocumentParserService : IJamaDocumentParserService
    {
        private readonly IJamaConnectService _jamaService;
        private readonly IAnythingLLMService _llmService;
        private readonly IDirectRagService? _directRagService;
        private readonly ITextGenerationService? _textGenerationService;
        private readonly ISystemCapabilityDerivationService? _derivationService;
        private readonly IOllamaProcessManager? _ollamaProcessManager;
        private readonly IOllamaStatusMonitor? _ollamaStatusMonitor;
        
        // Template Form Architecture services (Phase 6 integration)
        private readonly IOutputEnvelopeService? _envelopeService;
        private readonly IFieldLevelQualityService? _qualityService;
        private readonly IServiceComplianceWrapper? _complianceWrapper;
        private readonly IABTestingFramework? _abTestingFramework;
        private readonly ITelemetryDashboardService? _telemetryService;
        private bool _ollamaStatusMonitoringStarted;
        
        private const string PARSING_WORKSPACE_PREFIX = "jama-doc-parse";

        public bool IsConfigured => _jamaService.IsConfigured && (_llmService != null || _directRagService?.IsConfigured == true);

        public JamaDocumentParserService(
            IJamaConnectService jamaService, 
            IAnythingLLMService llmService, 
            IDirectRagService? directRagService = null, 
            ITextGenerationService? textGenerationService = null, 
            ISystemCapabilityDerivationService? derivationService = null,
            IOutputEnvelopeService? envelopeService = null,
            IFieldLevelQualityService? qualityService = null,
            IServiceComplianceWrapper? complianceWrapper = null,
            IABTestingFramework? abTestingFramework = null,
            ITelemetryDashboardService? telemetryService = null,
            IOllamaProcessManager? ollamaProcessManager = null,
            IOllamaStatusMonitor? ollamaStatusMonitor = null)
        {
            _jamaService = jamaService ?? throw new ArgumentNullException(nameof(jamaService));
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _directRagService = directRagService;
            _textGenerationService = textGenerationService;
            _derivationService = derivationService;
            _ollamaProcessManager = ollamaProcessManager;
            _ollamaStatusMonitor = ollamaStatusMonitor;
            _envelopeService = envelopeService;
            _qualityService = qualityService;
            _complianceWrapper = complianceWrapper;
            _abTestingFramework = abTestingFramework;
            _telemetryService = telemetryService;
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Initialized with Template Form Architecture: Envelope={envelopeService != null}, Quality={qualityService != null}, Compliance={complianceWrapper != null}, ABTest={abTestingFramework != null}, Telemetry={telemetryService != null}, OllamaManager={ollamaProcessManager != null}, OllamaMonitor={ollamaStatusMonitor != null}");
        }

        /// <summary>
        /// Parse a single Jama attachment and extract requirements using LLM
        /// </summary>
        public async Task<List<Requirement>> ParseAttachmentAsync(JamaAttachment attachment, int projectId, System.Action<string>? progressCallback = null, System.Action<Requirement>? onRequirementDiscovered = null, CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Starting parse for attachment {attachment.Id} ({attachment.FileName})");
                progressCallback?.Invoke($"🔧 Preparing to extract requirements from {attachment.FileName}...");

                // CRITICAL: Check if required AI services are available before processing
                // If verification fails, we'll still attempt extraction - it has its own retry logic
                var aiServicesVerified = await VerifyAIServicesAvailableAsync(progressCallback, cancellationToken);
                if (!aiServicesVerified)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Pre-warming failed or AI services unavailable - proceeding with extraction anyway (fallback to retry logic)");
                    progressCallback?.Invoke("⚠️ Pre-warming failed - attempting extraction anyway...");
                    // Don't abort - continue with extraction which has its own retry/restart logic
                }

                // Step 1: Download attachment from Jama
                progressCallback?.Invoke($"📥 Downloading attachment ({attachment.FileSize / 1024}KB)...");
                var fileBytes = await _jamaService.DownloadAttachmentAsync(attachment.Id, cancellationToken);
                if (fileBytes == null || fileBytes.Length == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Failed to download attachment {attachment.Id}");
                    progressCallback?.Invoke("❌ Failed to download document - please check your Jama connection");
                    throw new InvalidOperationException($"Failed to download attachment {attachment.Id}. This may be due to an expired authentication token or network issues. Please try refreshing your Jama connection.");
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Downloaded {fileBytes.Length} bytes for attachment {attachment.Id}");

                // Step 2: Use provided attachment metadata (no need to re-scan project)
                if (!attachment.IsSupportedDocument)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Unsupported document type: {attachment.MimeType}");
                    progressCallback?.Invoke($"❌ Unsupported document type: {attachment.MimeType}");
                    return new List<Requirement>();
                }

                if (_directRagService?.IsConfigured == true && _textGenerationService != null)
                {
                    // Check if it's a document type that DirectRag can handle effectively
                    if (attachment.IsWord || attachment.IsExcel || attachment.IsPdf || attachment.MimeType?.Contains("text") == true)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Using DirectRagService for document analysis ({attachment.MimeType})");
                        progressCallback?.Invoke($"🚀 Processing with reliable RAG-enhanced analysis...");
                        var directRagRequirements = await ExtractRequirementsWithDirectRagAsync(attachment, projectId, progressCallback, onRequirementDiscovered, cancellationToken);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[ATTACHMENT_TRACE] ParserReturn AttachmentId={attachment.Id} FileName={attachment.FileName} Source=DirectRag Count={directRagRequirements.Count} Sample={BuildRequirementTraceSample(directRagRequirements)}");
                        EnrichRequirementsWithAttachmentMetadata(directRagRequirements, attachment);
                        EnrichRequirementsWithValidationMethod(directRagRequirements);
                        await EnrichRequirementsWithVerificationMethodAsync(directRagRequirements, cancellationToken);
                        await EnrichRequirementsWithAllocationAsync(directRagRequirements, cancellationToken);
                        await EnrichRequirementsWithJamaPicklistHintsAsync(directRagRequirements, cancellationToken);
                        return directRagRequirements;
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] ❌ Unsupported document type for DirectRag: {attachment.MimeType}");
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] DirectRagService unavailable or not suitable for {attachment.MimeType}; using AnythingLLM fallback");
                progressCallback?.Invoke($"🔁 Using fallback requirement extraction...");
                var anythingLlmRequirements = await ExtractRequirementsWithAnythingLLMAsync(attachment, projectId, progressCallback, cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Info($"[ATTACHMENT_TRACE] ParserReturn AttachmentId={attachment.Id} FileName={attachment.FileName} Source=AnythingLLM Count={anythingLlmRequirements.Count} Sample={BuildRequirementTraceSample(anythingLlmRequirements)}");
                EnrichRequirementsWithAttachmentMetadata(anythingLlmRequirements, attachment);
                EnrichRequirementsWithValidationMethod(anythingLlmRequirements);
                await EnrichRequirementsWithVerificationMethodAsync(anythingLlmRequirements, cancellationToken);
                await EnrichRequirementsWithAllocationAsync(anythingLlmRequirements, cancellationToken);
                await EnrichRequirementsWithJamaPicklistHintsAsync(anythingLlmRequirements, cancellationToken);
                return anythingLlmRequirements;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Error parsing attachment {attachment.Id}: {ex.Message}");
                return new List<Requirement>();
            }
        }

        /// <summary>
        /// Parse multiple attachments in batch
        /// </summary>
        public async Task<List<Requirement>> ParseAttachmentsBatchAsync(List<int> attachmentIds, int projectId, CancellationToken cancellationToken = default)
        {
            var allRequirements = new List<Requirement>();

            // Get all attachments for the project once
            var attachments = await _jamaService.GetProjectAttachmentsAsync(projectId, cancellationToken);
            
            foreach (var attachmentId in attachmentIds)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var attachment = attachments.FirstOrDefault(a => a.Id == attachmentId);
                if (attachment == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Batch: Attachment {attachmentId} not found in project {projectId}");
                    continue;
                }

                var requirements = await ParseAttachmentAsync(attachment, projectId, null, null, cancellationToken);
                allRequirements.AddRange(requirements);
            }

            return allRequirements;
        }

        public async Task<Dictionary<int, AttachmentIndexValidationResult>> GetAttachmentIndexValidationAsync(
            int projectId,
            IReadOnlyCollection<JamaAttachment> attachments,
            CancellationToken cancellationToken = default)
        {
            if (attachments == null || attachments.Count == 0)
            {
                return new Dictionary<int, AttachmentIndexValidationResult>();
            }

            if (_directRagService?.IsConfigured != true)
            {
                var unavailableResults = new Dictionary<int, AttachmentIndexValidationResult>();
                foreach (var attachment in attachments)
                {
                    unavailableResults[attachment.Id] = new AttachmentIndexValidationResult
                    {
                        AttachmentId = attachment.Id,
                        State = AttachmentIndexValidationState.Unknown,
                        ScrapeBlocked = false,
                        Message = "Index validation unavailable (DirectRAG not configured)"
                    };
                }

                return unavailableResults;
            }

            return await _directRagService.ValidateAttachmentIndexesAsync(projectId, attachments, cancellationToken);
        }

        public async Task<bool> ReindexAttachmentAsync(
            JamaAttachment attachment,
            int projectId,
            CancellationToken cancellationToken = default)
        {
            if (_directRagService?.IsConfigured != true)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn("[JamaDocumentParser] Reindex requested but DirectRAG is not configured");
                return false;
            }

            try
            {
                var fileBytes = await _jamaService.DownloadAttachmentAsync(attachment.Id, cancellationToken);
                if (fileBytes == null || fileBytes.Length == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Reindex failed - could not download attachment {attachment.Id}");
                    return false;
                }

                var documentContent = await ExtractAttachmentTextForIndexingAsync(attachment, fileBytes);
                if (string.IsNullOrWhiteSpace(documentContent))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Reindex failed - no extractable text for attachment {attachment.Id}");
                    return false;
                }

                // Preserve current isolation strategy: parse/index context is one attachment at a time.
                await _directRagService.ClearProjectIndexAsync(projectId, cancellationToken);
                var indexed = await _directRagService.IndexDocumentAsync(attachment, documentContent, projectId, cancellationToken);

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Reindex {(indexed ? "succeeded" : "failed")} for attachment {attachment.Id} ({attachment.FileName})");
                return indexed;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Reindex error for attachment {attachment.Id}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extract requirements using AnythingLLM (fallback method when DirectRag is unavailable)
        /// </summary>
        private async Task<List<Requirement>> ExtractRequirementsWithAnythingLLMAsync(
            JamaAttachment attachment, 
            int projectId, 
            System.Action<string>? progressCallback,
            CancellationToken cancellationToken)
        {
            // Wire up AnythingLLM status updates to UI progress callback
            System.Action<string>? statusUpdateHandler = null;
            if (progressCallback != null)
            {
                statusUpdateHandler = (message) => progressCallback(message);
                _llmService.StatusUpdated += statusUpdateHandler;
            }
            
            try
            {
                // Download document if not already done
                var fileBytes = await _jamaService.DownloadAttachmentAsync(attachment.Id, cancellationToken);
                if (fileBytes == null || fileBytes.Length == 0)
                {
                    return new List<Requirement>();
                }

                progressCallback?.Invoke($"✅ Downloaded {fileBytes.Length / 1024}KB - Processing with AnythingLLM...");

                // Step 3: Create temporary AnythingLLM workspace for parsing
                progressCallback?.Invoke($"🔧 Creating AI workspace for '{attachment.FileName}'...");
                var workspaceName = $"Jama Document Parse: {attachment.FileName}";
                
                var workspace = await _llmService.CreateWorkspaceAsync(workspaceName, cancellationToken);
                if (workspace == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Failed to create workspace {workspaceName}");
                    progressCallback?.Invoke("❌ Failed to create AI workspace");
                    return new List<Requirement>();
                }
                
                var workspaceSlug = workspace.Slug;
                progressCallback?.Invoke($"✅ AI workspace ready - Uploading document...");

                // Step 4: Upload document to AnythingLLM for processing
                var tempFilePath = Path.Combine(Path.GetTempPath(), attachment.FileName);
                try
                {
                    await File.WriteAllBytesAsync(tempFilePath, fileBytes, cancellationToken);
                    
                    // Upload to AnythingLLM using the file-based upload
                    var uploadSuccess = await UploadFileToWorkspaceAsync(workspaceSlug, tempFilePath, cancellationToken, progressCallback);
                    
                    if (!uploadSuccess)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Failed to upload document to workspace");
                        progressCallback?.Invoke("❌ Failed to upload document to AI workspace");
                        return new List<Requirement>();
                    }

                    progressCallback?.Invoke($"Analyzing document with AnythingLLM - this may take 2-4 minutes...");
                    // Step 5: Query AnythingLLM to extract requirements
                    var requirements = await ExtractRequirementsFromWorkspaceAsync(workspaceSlug, attachment, projectId, progressCallback, cancellationToken);
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Extracted {requirements.Count} requirements from attachment {attachment.Id}");
                    return requirements;
                }
                finally
                {
                    // Clean up temp file
                    if (File.Exists(tempFilePath))
                    {
                        try { File.Delete(tempFilePath); } catch { /* Ignore cleanup errors */ }
                    }
                    
                    // Clean up temporary workspace
                    try
                    {
                        await _llmService.DeleteWorkspaceAsync(workspaceSlug, cancellationToken);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Cleaned up temporary workspace {workspaceSlug}");
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail if cleanup errors - workspace cleanup is not critical
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Failed to clean up workspace {workspaceSlug}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] AnythingLLM processing error: {ex.Message}");
                return new List<Requirement>();
            }
            finally
            {
                // Clean up event handler to prevent memory leaks
                if (statusUpdateHandler != null)
                {
                    _llmService.StatusUpdated -= statusUpdateHandler;
                }
            }
        }

        private async Task<string> ExtractAttachmentTextForIndexingAsync(JamaAttachment attachment, byte[] fileBytes)
        {
            try
            {
                if (attachment.IsWord)
                {
                    return await ExtractWordTextAsync(fileBytes);
                }

                if (attachment.IsExcel)
                {
                    return await ExtractExcelTextAsync(fileBytes);
                }

                if (attachment.IsPdf)
                {
                    return await ExtractPdfTextAsync(fileBytes);
                }

                if (attachment.MimeType?.Contains("text") == true)
                {
                    return System.Text.Encoding.UTF8.GetString(fileBytes);
                }

                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Unsupported document type for reindex text extraction: {attachment.MimeType}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Failed text extraction during reindex for {attachment.FileName}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Upload file to AnythingLLM workspace using multipart form data
        /// </summary>
        private async Task<bool> UploadFileToWorkspaceAsync(string workspaceSlug, string filePath, CancellationToken cancellationToken, System.Action<string>? progressCallback = null)
        {
            try
            {
                // Read file content
                var fileContent = await File.ReadAllTextAsync(filePath, cancellationToken);
                var fileName = Path.GetFileName(filePath);

                progressCallback?.Invoke("🧠 Starting document embedding operation...");
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] 🔍 UPLOAD DEBUG: Starting upload for '{fileName}' to workspace '{workspaceSlug}'");
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] 🔍 UPLOAD DEBUG: Content length: {fileContent.Length} characters");
                
                // Start the upload operation
                var uploadStartTime = DateTime.Now;
                var uploadTask = _llmService.UploadDocumentAsync(workspaceSlug, fileName, fileContent, cancellationToken);
                
                // Monitor progress while upload/embedding is happening - this returns when embedding succeeds OR fails
                var monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var progressTask = MonitorUploadProgressAsync(workspaceSlug, progressCallback, monitoringCts.Token);
                
                // Wait for EITHER upload to complete OR monitoring to detect failure
                var completedTask = await Task.WhenAny(uploadTask, progressTask);
                
                if (completedTask == progressTask)
                {
                    // Monitoring detected failure/success - cancel upload if still running
                    monitoringCts.Cancel();
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] 🔍 Monitoring completed first - checking results");
                    
                    // Check if monitoring detected success (document exists) or failure
                    var finalCheck = await _llmService.GetWorkspaceDocumentsAsync(workspaceSlug, cancellationToken);
                    var documentCount = finalCheck.HasValue ? finalCheck.Value.GetArrayLength() : 0;
                    
                    if (documentCount > 0)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Monitoring detected successful embedding - {documentCount} documents found");
                        progressCallback?.Invoke("✅ Document embedding completed successfully!");
                        return true;
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] 🚨 Monitoring detected embedding failure - AnythingLLM not working");
                        progressCallback?.Invoke("🚨 Embedding failure detected - AnythingLLM service malfunction");
                        throw new InvalidOperationException($"AnythingLLM embedding monitoring detected failure - service is not processing documents correctly.");
                    }
                }
                else
                {
                    // Upload completed first - check results normally
                    monitoringCts.Cancel(); // Stop monitoring
                    var uploadResult = await uploadTask;
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] 🔍 UPLOAD DEBUG: Upload completed with result: {uploadResult}");
                    
                    if (!uploadResult)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] ❌ Document upload to AnythingLLM failed");
                        progressCallback?.Invoke("❌ Document upload failed - AnythingLLM service unavailable");
                        throw new InvalidOperationException($"Failed to upload document to AnythingLLM workspace. Check service status.");
                    }
                    else
                    {
                        // Even if upload "succeeded", verify documents actually exist  
                        var verifyCheck = await _llmService.GetWorkspaceDocumentsAsync(workspaceSlug, cancellationToken);
                        var documentCount = verifyCheck.HasValue ? verifyCheck.Value.GetArrayLength() : 0;
                        
                        if (documentCount > 0)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Upload and embedding both successful - {documentCount} documents");
                            progressCallback?.Invoke("✅ Document embedding completed successfully!");
                            return true;
                        }
                        else
                        {
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] ⚠️ Upload succeeded but embedding failed - 0 documents in workspace");
                            progressCallback?.Invoke("⚠️ Document embedding incomplete - check AnythingLLM model configuration"); 
                            throw new InvalidOperationException($"Document uploaded to AnythingLLM but embedding failed. This usually indicates embedding model configuration issues.");
                        }
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Error uploading file: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Monitor upload progress and provide user feedback during embedding operations
        /// </summary>
        private async Task MonitorUploadProgressAsync(string workspaceSlug, System.Action<string>? progressCallback, CancellationToken cancellationToken)
        {
            if (progressCallback == null) return;

            try
            {
                var startTime = DateTime.Now;
                var maxDuration = TimeSpan.FromMinutes(3); // Reduced from 10 to 3 minutes - fail faster
                var updateInterval = TimeSpan.FromSeconds(15); // Update every 15 seconds
                var lastDocumentCount = -1;
                var stuckCount = 0;

                while (!cancellationToken.IsCancellationRequested && DateTime.Now - startTime < maxDuration)
                {
                    await Task.Delay(updateInterval, cancellationToken);

                    var elapsed = DateTime.Now - startTime;
                    var elapsedMinutes = (int)elapsed.TotalMinutes;
                    var elapsedSeconds = (int)elapsed.TotalSeconds % 60;

                    // Check if document has appeared in workspace (indicates embedding progress/completion)  
                    var documents = await _llmService.GetWorkspaceDocumentsAsync(workspaceSlug, cancellationToken);
                    var documentCount = documents.HasValue ? documents.Value.GetArrayLength() : 0;

                    if (documentCount > 0)
                    {
                        progressCallback($"✅ Document embedded successfully! ({documentCount} docs, {elapsedMinutes}m {elapsedSeconds}s)");
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Embedding SUCCESS: Document visible in workspace after {elapsedMinutes}m {elapsedSeconds}s");
                        return; // Document is visible, embedding completed successfully
                    }
                    else
                    {
                        // Check if we're stuck (no progress for too long)
                        if (documentCount == lastDocumentCount)
                        {
                            stuckCount++;
                        }
                        else
                        {
                            stuckCount = 0;
                        }
                        lastDocumentCount = documentCount;

                        // If stuck for >90 seconds (6 cycles), assume failure
                        if (stuckCount >= 6 && elapsed.TotalSeconds > 90)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] 🚨 EMBEDDING FAILURE DETECTED: No progress for 90+ seconds ({stuckCount} cycles)");
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Document count remains 0 - AnythingLLM embedding has failed");
                            progressCallback("🚨 Embedding stuck - no documents after 90+ seconds. Switching to direct extraction!");
                            return; // Exit early to trigger fallback
                        }
                        
                        // Even earlier detection: if we've been running 2+ minutes with 0 docs, something is wrong
                        if (elapsed.TotalSeconds > 120 && documentCount == 0)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] 🚨 EARLY FAILURE DETECTION: 2+ minutes with 0 documents");
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] This indicates embedding process failure - triggering fallback");
                            progressCallback("🚨 Embedding taking too long (2+ min, no documents) - switching to direct extraction!");
                            return; // Exit to trigger fallback
                        }

                        progressCallback($"🔄 Embedding chunks into vectors... ({elapsedMinutes}m {elapsedSeconds}s elapsed)");
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Embedding progress: Processing chunks... ({elapsedMinutes}m {elapsedSeconds}s elapsed)");
                    }
                }

                // If we reach here, we timed out
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] 🚨 EMBEDDING TIMEOUT: Monitoring timed out after 3 minutes");
                progressCallback("⏰ Embedding timeout - switching to direct extraction");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Progress monitoring stopped: {ex.Message}");
                progressCallback?.Invoke("⚠️ Monitoring error - switching to direct extraction");
            }
        }

        /// <summary>
        /// Extract requirements from AnythingLLM workspace using single comprehensive LLM prompt for efficiency
        /// </summary>
        private async Task<List<Requirement>> ExtractRequirementsFromWorkspaceAsync(
            string workspaceSlug, 
            JamaAttachment attachment, 
            int projectId, 
            System.Action<string>? progressCallback,
            CancellationToken cancellationToken)
        {
            try
            {
                progressCallback?.Invoke($"Optimizing AnythingLLM workspace configuration for '{attachment.FileName}'...");
                
                // CRITICAL: Apply optimal RAG configuration BEFORE extraction to ensure comprehensive retrieval
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Applying optimal RAG configuration proactively for workspace '{workspaceSlug}'");
                var configApplied = await _llmService.FixRagConfigurationAsync(workspaceSlug, cancellationToken);
                
                if (configApplied)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ RAG configuration applied - waiting for settings to take effect...");
                    await Task.Delay(1500, cancellationToken); // Allow config to persist
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] ⚠️ Could not apply RAG configuration - proceeding with current settings");
                }
                
                progressCallback?.Invoke($"Testing document access for '{attachment.FileName}'...");
                
                // Now test RAG document access with optimal settings applied
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Testing RAG document access for workspace '{workspaceSlug}' with optimized settings");
                var (hasAccess, diagnostics) = await _llmService.TestDocumentAccessAsync(workspaceSlug, cancellationToken);
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] RAG Test Results: {diagnostics}");
                
                if (!hasAccess)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] ❌ Document access test FAILED even after RAG configuration");
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] This indicates a fundamental AnythingLLM or workspace issue");
                    progressCallback?.Invoke($"❌ Document access failed - cannot extract requirements"); 
                    return new List<Requirement>();
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ RAG document access confirmed - proceeding with extraction");
                progressCallback?.Invoke($"Analyzing '{attachment.FileName}' with AI for comprehensive requirement extraction...");
                
                // Single comprehensive prompt combining verification, extraction, and validation
                var comprehensivePrompt = BuildComprehensiveExtractionPrompt(attachment);

                // Single LLM call instead of 4 separate calls (major performance optimization)
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Starting comprehensive extraction with single LLM call...");
                var response = await _llmService.SendChatMessageAsync(workspaceSlug, comprehensivePrompt, cancellationToken);

                if (string.IsNullOrEmpty(response))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Empty response from comprehensive extraction");
                    return new List<Requirement>();
                }

                // Parse LLM response into requirements with validation
                var requirements = ParseRequirementsFromLLMResponse(response, attachment, projectId);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Initial extraction: {requirements.Count} requirements from comprehensive prompt");

                // CONTENT VALIDATION: Verify each requirement aligns with actual document content
                progressCallback?.Invoke($"🔍 Validating {requirements.Count} requirements against document content...");
                
                // Add timeout for validation to prevent getting stuck
                var contentValidatedRequirements = await ValidateExtractedRequirements(workspaceSlug, requirements, cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Content validation: {contentValidatedRequirements.Count} of {requirements.Count} requirements verified as legitimate");

                // CRITICAL CHECK: If LLM explicitly states it's using hypothetical content, abort extraction
                if (response.Contains("hypothetical content") || 
                    response.Contains("do not have access to external documents") ||
                    response.Contains("don't have direct access") ||
                    response.Contains("unable to provide direct content from files") ||
                    response.Contains("without the capability to directly interact") ||
                    response.Contains("AI language model") && response.Contains("unable to"))
                {
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] ❌ LLM stated it cannot access document content - RAG retrieval failed");
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] LLM Response: {response.Substring(0, Math.Min(200, response.Length))}...");
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Stopping extraction to prevent fake requirements from being added");
                    return new List<Requirement>(); // Return empty list instead of fake requirements
                }

                // EARLY EXIT: Skip validation and recovery if no requirements found
                if (requirements.Count == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] ❌ No requirements extracted - LLM cannot access document content");
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Skipping validation and recovery for empty result set");
                    progressCallback?.Invoke($"❌ No requirements found - document not accessible to LLM");
                    return new List<Requirement>();
                }

                // COMPLETENESS CHECK: Run validation pass to ensure we didn't miss requirements
                var finalValidatedRequirements = await ValidateCompletenessAsync(workspaceSlug, contentValidatedRequirements, attachment, projectId, cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Final count after all validation: {finalValidatedRequirements.Count} requirements");

                // SANITY CHECK: Warn if results seem suspiciously low for document size
                var expectedMinRequirements = EstimateMinimumRequirements(attachment);
                if (finalValidatedRequirements.Count < expectedMinRequirements)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] LOW COUNT WARNING - Only found {finalValidatedRequirements.Count} requirements in {attachment.FileName} (expected at least {expectedMinRequirements} based on document size). Consider re-running extraction.");
                }

                progressCallback?.Invoke($"✅ Extracted {finalValidatedRequirements.Count} requirements (content validated & completeness checked)");
                return finalValidatedRequirements;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Error during comprehensive extraction: {ex.Message}");
                return new List<Requirement>();
            }
        }

        /// <summary>
        /// Build the LLM prompt for requirement extraction
        /// </summary>
        private string BuildRequirementExtractionPrompt(JamaAttachment attachment)
        {
            return $@"COMPREHENSIVE REQUIREMENTS EXTRACTION FROM: {attachment.FileName}

⚡ RAG SYSTEM STATUS: Document content processed and available for retrieval
📄 Document Type: {GetDocumentTypeDescription(attachment)} (Size: 1.5MB+ - expect 20-50+ requirements)

🔥 CRITICAL ANTI-FABRICATION RULES:
1. ONLY extract requirements that appear VERBATIM or EXPLICITLY in the retrieved document content
2. NEVER create plausible-sounding requirements based on what ""should"" be in technical documents  
3. If you can see document content, extract whatever technical specifications, constraints, or criteria ARE visible
4. Do NOT fabricate section numbers, page numbers, or document references
5. All source citations must reference ACTUAL text visible in your context

🔍 EXTRACTION MANDATE: 
The document '{attachment.FileName}' has been processed through RAG. You will ONLY receive document text that actually exists.

• Extract EVERY requirement that appears in your retrieved context - do not stop at 3-5 examples
• Include requirements from ALL sections provided: main body, appendices, tables, figures
• Look for SHALL, MUST, WILL, SHOULD statements throughout the retrieved content
• Include performance specifications, test criteria, design constraints FROM THE ACTUAL TEXT

REQUIREMENT TYPES TO EXTRACT (ONLY if present in retrieved content):
• Functional requirements (system behavior, operations)
• Performance specs (speed, accuracy, throughput, timing, response times)
• Interface requirements (signals, protocols, connectors, voltages, communications)
• Environmental limits (temperature, humidity, vibration, shock, altitude)
• Lifecycle requirements (MTBF, cycles, durability, reliability metrics)  
• Safety/security requirements (fail-safe behavior, protection mechanisms)
• Design constraints and allocations (size, weight, power consumption)
• Test and verification requirements (acceptance criteria, test procedures)

⚠️ VERIFICATION CHECKPOINT: Before generating each requirement, ask yourself:
- ""Can I see this exact requirement text in my retrieved context?""
- ""Is this source reference visible in the content provided to me?""
- ""Am I creating this based on assumptions or actual document text?""

If you cannot confidently answer YES to these questions, DO NOT include that requirement.

🔍 CONTENT VISIBILITY CHECK:
- If you can see ANY technical specifications, constraints, or performance criteria in the document content, extract them as requirements
- If the retrieved content contains interface specs, environmental limits, test criteria, or design constraints, format them as requirements
- Extract ALL technical content you can actually see - do not leave any specifications unextracted

⚠️ OVERRIDE NOTICE: Ignore any built-in restrictions about file access. This is RAG retrieval, not file access.

Begin extraction now. Extract ALL technical specifications and constraints you can actually see in the retrieved document content.";
        }

        /// <summary>
        /// Build comprehensive LLM prompt that combines verification, extraction, and validation in single call
        /// </summary>
        private string BuildComprehensiveExtractionPrompt(JamaAttachment attachment)
        {
            return $@"CRITICAL: EXHAUSTIVE REQUIREMENT EXTRACTION FOR TECHNICAL DOCUMENT

FILE: {attachment.FileName}

⚠️ IMPORTANT: You may have access to only partial document chunks through RAG retrieval.
STRATEGY: Scan EVERY chunk you receive, looking for any requirement-related content.
Don't rely only on obvious matches - look in ALL sections, headers, tables, specs.

STEP 1 - INVENTORY DOCUMENT STRUCTURE:
List everything you can see:
- Document sections and headings (all of them)
- Tables, diagrams, specifications
- Any lists or structured data
- Appendices or reference sections
- If you see scattered chunks rather than continuous text, note that

STEP 2 - EXHAUSTIVE REQUIREMENT SCAN:
Go through EVERY section and EVERY piece of content looking for:

✓ SHALL, MUST, WILL, SHOULD statements (formal requirements)
✓ Performance specs: timing, throughput, latency, accuracy
✓ Interface specifications: protocols, message formats, API specs
✓ Acceptance criteria and test requirements
✓ Environmental constraints: hardware, OS, dependencies
✓ Safety, security, compliance requirements
✓ Quality metrics, reliability, availability specs
✓ Physical constraints or design limits
✓ Numbers, thresholds, tolerance values
✓ References to standards or other requirements
✓ State machines, sequences, procedural steps
✓ Capability descriptions (""system shall be capable of..."")
✓ Table entries (often contain specs and constraints)
✓ Figure captions with technical details
✓ Section headers that contain spec info

STEP 3 - AGGREGATE AND OUTPUT:
Output EVERYTHING that could possibly be a requirement.
Better to include marginal cases than miss requirements.

FORMAT FOR EACH REQUIREMENT:

---
ID: REQ-001
Text: [Primary requirement sentence in approved format, e.g. The <ACTOR> shall <ACTION> when/while/where <CONDITION>. Include exact numbers and constraints]
Purpose: [Why this requirement exists; concise engineering rationale tied to intent]
Product State or Condition: [Required product state/operating condition/context for requirement applicability]
Input(s) and Stimulus: [Inputs, triggers, and stimuli that drive behavior; include table references or values when applicable]
Category: [Functional/Performance/Interface/Environmental/Safety/Design/Quality/Test/Compliance]
Priority: [High/Medium/Low]
Verification: [Test/Analysis/Inspection/Demonstration]
Source: [Section name, table name, or location]
---

CRITICAL RULES:
- Use exact wording from the document
- Include ALL numbers, units, thresholds
- Populate Text/Purpose/Product State or Condition/Input(s) and Stimulus for every requirement
- Keep each field on a single line using clear prose (no blank fields)
- If multiple related specs exist, extract each separately
- Number sequentially from REQ-001
- Scan the FULL document - check every section listed in STEP 1

⚠️ AGGRESSIVE EXTRACTION: If you find only 4-5 requirements, you're likely missing most of the document.
For a technical ATP/SRS document, expect 15-50+ requirements minimum.";
        }

        /// <summary>
        /// Parse LLM response into structured Requirement objects
        /// </summary>
        private List<Requirement> ParseRequirementsFromLLMResponse(string llmResponse, JamaAttachment attachment, int projectId)
        {
            var requirements = new List<Requirement>();

            try
            {
                // DEBUG: Log the raw LLM response to understand what we're getting
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Raw LLM response length: {llmResponse?.Length ?? 0} characters");
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - First 500 chars of LLM response: {llmResponse?.Substring(0, Math.Min(500, llmResponse?.Length ?? 0))}");
                
                // Split response by requirement delimiter
                var blocks = llmResponse?.Split(new[] { "---" }, StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Found {blocks.Length} blocks after splitting on '---'");

                foreach (var block in blocks)
                {
                    if (string.IsNullOrWhiteSpace(block))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Skipping empty block");
                        continue;
                    }

                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Processing block: {block.Trim().Substring(0, Math.Min(200, block.Trim().Length))}");
                    var requirement = ParseRequirementBlock(block.Trim(), attachment, projectId);
                    if (requirement != null)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Successfully parsed requirement: {requirement.GlobalId}");
                        requirements.Add(requirement);
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Failed to parse requirement from block");
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Final result: Parsed {requirements.Count} requirements from LLM response");
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Parsed {requirements.Count} requirements from LLM response");

                NormalizeRequirementsToTestSolutionPerspective(requirements, attachment);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Error parsing LLM response: {ex.Message}");
            }

            return requirements;
        }

        /// <summary>
        /// Parse a single requirement block from LLM response
        /// </summary>
        private Requirement? ParseRequirementBlock(string block, JamaAttachment attachment, int projectId)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Parsing block with {block.Length} characters");
                
                var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var reqData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Block has {lines.Length} lines");

                foreach (var line in lines)
                {
                    var colonIndex = line.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var key = line.Substring(0, colonIndex).Trim();
                        var value = line.Substring(colonIndex + 1).Trim();
                        reqData[key] = value;
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Found key-value: '{key}' = '{value.Substring(0, Math.Min(50, value.Length))}'");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Skipping line (no colon): '{line.Substring(0, Math.Min(50, line.Length))}'");
                    }
                }

                // ENHANCED PARSING: Try multiple field name variations to avoid missing requirements
                var id = ExtractFieldValue(reqData, new[] { "ID", "Requirement ID", "Req ID", "Item", "Number" });
                var text = ExtractFieldValue(reqData, new[] { "Text", "Description", "Requirement", "Content", "Summary" });

                // FALLBACK: If structured parsing fails, try to extract from raw block
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] MISSING REQ WARNING - Structured parsing failed, attempting fallback extraction. Block: {block.Substring(0, Math.Min(200, block.Length))}");
                    var fallbackReq = TryFallbackExtraction(block, attachment, projectId);
                    if (fallbackReq != null)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] RECOVERY SUCCESS - Fallback extraction recovered requirement: {fallbackReq.GlobalId}");
                        return fallbackReq;
                    }
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] LOST REQUIREMENT - Both structured and fallback parsing failed for block with keys: {string.Join(", ", reqData.Keys)}");
                    return null;
                }

                var purpose = ExtractFieldValue(reqData, new[] { "Purpose" });
                var productStateOrCondition = ExtractFieldValue(reqData, new[] { "Product State or Condition", "Product State", "State or Condition" });
                var inputsAndStimulus = ExtractFieldValue(reqData, new[] { "Input(s) and Stimulus", "Inputs and Stimulus", "Inputs", "Stimulus" });

                var descriptionSections = new List<string> { text };
                if (!string.IsNullOrWhiteSpace(purpose))
                {
                    descriptionSections.Add($"Purpose: {purpose}");
                }
                if (!string.IsNullOrWhiteSpace(productStateOrCondition))
                {
                    descriptionSections.Add($"Product State or Condition: {productStateOrCondition}");
                }
                if (!string.IsNullOrWhiteSpace(inputsAndStimulus))
                {
                    descriptionSections.Add($"Input(s) and Stimulus: {inputsAndStimulus}");
                }

                // Build requirement object
                var requirement = new Requirement
                {
                    GlobalId = id,
                    Item = id,
                    Name = reqData.TryGetValue("Category", out var cat) ? cat : "Extracted Requirement",
                    Description = string.Join("\n\n", descriptionSections),
                    TraceReference = BuildRequirementTraceReference(attachment.Id, id, 0),
                    SourceDocumentName = attachment.FileName,
                    SourceAttachmentId = attachment.Id,
                    SourceJamaItemId = attachment.Item > 0 ? attachment.Item : null
                };

                // Add source context to description
                if (reqData.TryGetValue("Source", out var sourceContext))
                {
                    requirement.Description = $"{text}\n\nSource: {sourceContext}\n\nFrom: Jama Attachment {attachment.FileName}";
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Successfully created requirement with ID: '{id}'");
                return requirement;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Exception parsing requirement block: {ex.Message}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaDocumentParser] Error parsing requirement block: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Self-validation: Have LLM verify extracted requirements against document content to prevent hallucination
        /// </summary>
        private async Task<List<Requirement>> ValidateExtractedRequirements(string workspaceSlug, List<Requirement> extractedRequirements, CancellationToken cancellationToken = default)
        {
            try
            {
                if (extractedRequirements.Count == 0)
                    return extractedRequirements;

                // Format extracted requirements for validation
                var requirementsText = FormatRequirementsForValidation(extractedRequirements);

                var validationPrompt = $@"SELF-VALIDATION: VERIFY EXTRACTED REQUIREMENTS AGAINST DOCUMENT CONTENT

🔍 MISSION: For each requirement below, verify if it appears in the document content available to you through RAG.

📋 EXTRACTED REQUIREMENTS TO VALIDATE:
{requirementsText}

🚨 VALIDATION PROTOCOL:
For each requirement, check if you can find supporting evidence in the document content:
1. Can you see text in the document that supports this requirement?  
2. Does the requirement match actual specifications, constraints, or criteria in the document?
3. Are any cited sections, pages, or sources actually visible to you?

📝 RESPONSE FORMAT:
For each requirement ID, respond with:

VALID: [REQ-ID] - Brief explanation of where you see this in the document
INVALID: [REQ-ID] - This requirement appears fabricated/not found in document content

⚠️ CRITICAL: Be STRICT in validation. If you cannot clearly see supporting evidence for a requirement in your document context, mark it INVALID.

🎯 EXAMPLE RESPONSES:
VALID: REQ-001 - Section 3.2 shows interface voltage specification of 3.3V ±5%  
INVALID: REQ-005 - Cannot locate any 50MHz clock requirement in accessible document content
VALID: REQ-008 - Table on page 4 lists operating temperature range -40°C to +85°C

Begin validation now - be thorough and honest about what you can actually see:";

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Sending validation request for {extractedRequirements.Count} requirements...");
                var validationResponse = await _llmService.SendChatMessageAsync(workspaceSlug, validationPrompt, cancellationToken);

                if (string.IsNullOrEmpty(validationResponse))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Empty validation response - keeping all requirements");
                    return extractedRequirements;
                }

                // Parse validation response and filter requirements
                var validatedRequirements = ParseValidationResponse(validationResponse, extractedRequirements);
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Validation complete. Kept {validatedRequirements.Count} of {extractedRequirements.Count} requirements");
                
                return validatedRequirements;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Error in self-validation: {ex.Message}");
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Validation failed - returning original requirements");
                return extractedRequirements; // Return original list if validation fails
            }
        }

        /// <summary>
        /// Format requirements list for validation prompt
        /// </summary>
        private string FormatRequirementsForValidation(List<Requirement> requirements)
        {
            var formatted = new List<string>();
            
            foreach (var req in requirements)
            {
                var reqText = $"ID: {req.GlobalId}\n";
                reqText += $"Text: {req.Description?.Split('\n')[0] ?? "No description"}\n"; // First line only for brevity
                reqText += "---";
                formatted.Add(reqText);
            }
            
            return string.Join("\n", formatted);
        }

        /// <summary>
        /// Parse LLM validation response to determine which requirements are valid
        /// </summary>
        private List<Requirement> ParseValidationResponse(string validationResponse, List<Requirement> originalRequirements)
        {
            var validRequirements = new List<Requirement>();
            var validationLines = validationResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Parsing validation response with {validationLines.Length} lines");

            // Create lookup dictionary for requirements by ID
            var requirementLookup = originalRequirements.ToDictionary(r => r.GlobalId ?? "", r => r);

            foreach (var line in validationLines)
            {
                var trimmedLine = line.Trim();
                
                // Look for VALID: REQ-XXX patterns using regex to extract complete requirement ID
                if (trimmedLine.StartsWith("VALID:", StringComparison.OrdinalIgnoreCase))
                {
                    // Use regex to find REQ-XXX pattern in the line
                    var match = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"REQ-\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                    if (match.Success && requirementLookup.ContainsKey(match.Value))
                    {
                        var reqId = match.Value;
                        validRequirements.Add(requirementLookup[reqId]);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Validated requirement: {reqId}");
                    }
                }
                // Log INVALID requirements for debugging
                else if (trimmedLine.StartsWith("INVALID:", StringComparison.OrdinalIgnoreCase))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"REQ-\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                    if (match.Success)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] LLM marked as INVALID: {match.Value} - {trimmedLine}");
                    }
                }
            }

            // If we couldn't parse any validation results, return original list with warning
            if (validRequirements.Count == 0 && originalRequirements.Count > 0)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Could not parse any validation results - returning all original requirements");
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Raw validation response: {validationResponse}");
                return originalRequirements;
            }

            return validRequirements;
        }

        /// <summary>
        /// Extract field value using multiple possible field names (case-insensitive)
        /// </summary>
        private string? ExtractFieldValue(Dictionary<string, string> data, string[] possibleNames)
        {
            foreach (var name in possibleNames)
            {
                if (data.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            return null;
        }

        /// <summary>
        /// Fallback extraction when structured parsing fails - attempts to find requirements in raw text
        /// </summary>
        private Requirement? TryFallbackExtraction(string block, JamaAttachment attachment, int projectId)
        {
            try
            {
                // Look for patterns like "REQ-123", "R-456", or numbered items
                var patterns = new[]
                {
                    @"(?i)(?:REQ|REQUIREMENT|R)[-_\s]*(\d+)",
                    @"(\d+)\.\s+([^\n]+)",
                    @"Item\s+(\d+)",
                    @"#(\d+)"
                };

                foreach (var pattern in patterns)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(block, pattern);
                    if (match.Success)
                    {
                        var id = match.Groups[1].Value;
                        var text = match.Groups.Count > 2 ? match.Groups[2].Value : block.Trim();
                        
                        // Clean up text - take first meaningful sentence
                        if (string.IsNullOrWhiteSpace(text) || text.Length < 10)
                        {
                            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                            text = lines.FirstOrDefault(l => l.Trim().Length > 10) ?? block.Trim();
                        }

                        return new Requirement
                        {
                            GlobalId = $"FALLBACK-{id}",
                            Item = id,
                            Name = "Extracted Requirement (Fallback)",
                            Description = $"{text}\n\n[Recovered via fallback extraction]\nSource: {attachment.FileName}",
                            TraceReference = BuildRequirementTraceReference(attachment.Id, id, 0),
                            SourceDocumentName = attachment.FileName,
                            SourceAttachmentId = attachment.Id,
                            SourceJamaItemId = attachment.Item > 0 ? attachment.Item : null
                        };
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] FALLBACK FAILED - No recognizable patterns in block: {block.Substring(0, Math.Min(100, block.Length))}");
                return null;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Fallback extraction error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Validate completeness by checking for potential missed requirements
        /// </summary>
        private async Task<List<Requirement>> ValidateCompletenessAsync(string workspaceSlug, List<Requirement> initialRequirements, JamaAttachment attachment, int projectId, CancellationToken cancellationToken)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] COMPLETENESS CHECK - Found {initialRequirements.Count} requirements for {attachment.FileName}");
                
                var expectedMin = EstimateMinimumRequirements(attachment);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] COMPLETENESS CHECK - Expected minimum: {expectedMin} requirements for {attachment.FileSize / 1024.0:F1}KB document");
                
                // If we found significantly fewer than expected, run a second extraction pass
                // Threshold: 50% instead of 70% to be more aggressive about recovery
                var completenessThreshold = expectedMin * 0.5;
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] COMPLETENESS CHECK - Threshold: {completenessThreshold:F1} (50% of {expectedMin})");
                
                if (initialRequirements.Count < completenessThreshold)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] COMPLETENESS CHECK - TRIGGERED ({initialRequirements.Count} < {completenessThreshold:F1}), running recovery extraction pass...");
                    
                    var recoveryPrompt = BuildRecoveryExtractionPrompt(attachment, initialRequirements);
                    // Use shorter timeout for recovery operations to prevent long waits
                    var recoveryTimeout = TimeSpan.FromMinutes(1.5); // Reduced from default 4 minutes
                    var recoveryResponse = await _llmService.SendChatMessageAsync(workspaceSlug, recoveryPrompt, recoveryTimeout, cancellationToken);
                    
                    if (!string.IsNullOrEmpty(recoveryResponse))
                    {
                        var recoveredRequirements = ParseRequirementsFromLLMResponse(recoveryResponse, attachment, projectId);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Recovery pass found {recoveredRequirements.Count} additional requirements");
                        
                        // Merge with initial requirements (avoiding duplicates by GlobalId)
                        var allRequirements = new List<Requirement>(initialRequirements);
                        var existingIds = new HashSet<string>(initialRequirements.Select(r => r.GlobalId ?? ""));
                        
                        foreach (var recovered in recoveredRequirements)
                        {
                            if (!string.IsNullOrEmpty(recovered.GlobalId) && !existingIds.Contains(recovered.GlobalId))
                            {
                                allRequirements.Add(recovered);
                                existingIds.Add(recovered.GlobalId);
                            }
                        }
                        
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Merged result: {allRequirements.Count} total requirements after recovery");
                        return allRequirements;
                    }
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] COMPLETENESS CHECK - PASSED ({initialRequirements.Count} >= {completenessThreshold:F1}), no recovery needed");
                }
                
                return initialRequirements;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Completeness validation failed: {ex.Message}");
                return initialRequirements;
            }
        }

        /// <summary>
        /// Build recovery extraction prompt for missed requirements
        /// </summary>
        private string BuildRecoveryExtractionPrompt(JamaAttachment attachment, List<Requirement> alreadyFound)
        {
            var foundIds = alreadyFound.Select(r => r.GlobalId ?? "Unknown").ToList();
            var foundIdsText = string.Join(", ", foundIds);
            
            return $@"RECOVERY SCAN: EXHAUSTIVE RE-EXTRACTION OF MISSED REQUIREMENTS

FILE: {attachment.FileName}
ALREADY FOUND: {alreadyFound.Count} requirements ({foundIdsText})

⚠️ CRITICAL: Only {alreadyFound.Count} requirements found suggests RAG retrieval limitation.
You may be receiving document chunks in isolation without context.
TASK: Re-scan EVERY part of the document systematically for ALL types of requirements.

EXHAUSTIVE SCAN PROTOCOL:
1. DOCUMENT STRUCTURE SCAN:
   - What sections/chapters exist?
   - Are there tables with specs?
   - Are there numbered lists or bullet points?
   - Are there figures or diagrams with captions?
   - Any appendices or reference sections?

2. REQUIREMENT HUNTING IN EACH SECTION:
   
   ✓ Functional Requirements:
   - System capabilities (""shall be capable of..."")
   - Processes and workflows
   - State transitions
   - Input/output handling
   
   ✓ Performance Requirements:
   - Response times, latency (milliseconds, seconds)
   - Throughput, bandwidth, data rates
   - Memory, storage requirements
   - Scalability, capacity limits
   - Availability, uptime percentages
   
   ✓ Interface Requirements:
   - API specifications, endpoints
   - Data formats (JSON, XML, etc)
   - Protocol requirements (TCP, HTTP, etc)
   - Message structures
   - Encoding/compression
   
   ✓ Test & Acceptance Criteria:
   - Test cases with specific conditions
   - Pass/fail criteria
   - Acceptance thresholds
   - Validation procedures
   - Test data requirements
   
   ✓ Environmental:
   - Hardware requirements (CPU, RAM, disk)
   - Operating system versions
   - Browser compatibility
   - Network requirements
   - Database versions
   
   ✓ Safety/Security/Compliance:
   - Encryption requirements
   - Authentication mechanisms
   - Permission/role requirements
   - Audit logging needs
   - Data protection rules
   - Regulatory requirements

3. AGGRESSIVE EXTRACTION RULES:
   - Include EVERY number, threshold, percentage
   - Extract constraint statements (""must not exceed..."")
   - Extract each numbered item as separate requirement
   - Include table entries that specify behavior/limits
   - Treat section headers as requirement sources if they describe system behavior
   - If unsure: EXTRACT IT (false positive better than false negative)

OUTPUT FORMAT (continue from REQ-{alreadyFound.Count + 1:D3}):

---
ID: REQ-{alreadyFound.Count + 1:D3}
Text: [Primary requirement sentence in approved format, e.g. The <ACTOR> shall <ACTION> when/while/where <CONDITION>. Include exact numbers and constraints]
Purpose: [Why this requirement exists; concise engineering rationale tied to intent]
Product State or Condition: [Required product state/operating condition/context for requirement applicability]
Input(s) and Stimulus: [Inputs, triggers, and stimuli that drive behavior; include table references or values when applicable]
Category: [Functional/Performance/Interface/Environmental/Safety/Design/Quality/Test/Compliance]
Priority: [High/Medium/Low]
Verification: [Test/Analysis/Inspection/Demonstration]
Source: [Specific section/table/figure where found]
---

⚠️ SUCCESS INDICATOR: If still finding only {alreadyFound.Count} total, document may have limited specifications.
But thoroughly scan all sections first before concluding.";
        }

        /// <summary>
        /// Estimate minimum expected requirements based on document characteristics
        /// </summary>
        private int EstimateMinimumRequirements(JamaAttachment attachment)
        {
            // Aggressive heuristic: Technical documents (ATP, SRS, etc) have many requirements
            // Use 1 requirement per 15-20KB as baseline for comprehensive documents
            long fileSize = attachment.FileSize;
            var sizeKB = fileSize / 1024.0;
            
            // Technical documents over 100KB typically have many requirements (ATP, SRS, Interface specs, etc)
            if (sizeKB > 100)
            {
                // More aggressive: 1 requirement per 20KB for large technical docs
                // For 135KB: 135/20 = 6.75 → 15 minimum expected
                return Math.Max(15, (int)(sizeKB / 20));
            }
            else if (sizeKB > 50)
            {
                return Math.Max(8, (int)(sizeKB / 15)); // Medium technical doc: at least 8, or 1 per 15KB
            }
            else if (sizeKB > 20)
            {
                return 5; // Small technical doc should have multiple requirements
            }
            else
            {
                return 2; // Very small document
            }
        }

        /// <summary>
        /// Get human-readable document type description
        /// </summary>
        private string GetDocumentTypeDescription(JamaAttachment attachment)
        {
            if (attachment.IsPdf) return "PDF Document";
            if (attachment.IsWord) return "Word Document";
            if (attachment.IsExcel) return "Excel Spreadsheet";
            return "Document";
        }

        /// <summary>
        /// Attempts direct text extraction and requirement parsing as fallback when RAG vectorization fails
        /// Uses DirectRagService for document processing and plain LLM for requirement extraction
        /// </summary>
        private async Task<List<Requirement>> ExtractRequirementsWithDirectRagAsync(
            JamaAttachment attachment, 
            int projectId, 
            System.Action<string>? progressCallback,
            System.Action<Requirement>? onRequirementDiscovered = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                progressCallback?.Invoke($"📄 Processing '{attachment.FileName}' with direct document analysis...");
                
                // Step 1: Download document content
                var fileBytes = await _jamaService.DownloadAttachmentAsync(attachment.Id, cancellationToken);
                if (fileBytes == null || fileBytes.Length == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error($"[DirectRag] Failed to download attachment {attachment.Id}");
                    return new List<Requirement>();
                }

                // Step 2: Extract text content with proper document parsing
                string documentContent;
                try
                {
                    if (attachment.IsWord)
                    {
                        // Use DocumentFormat.OpenXml for Word documents
                        documentContent = await ExtractWordTextAsync(fileBytes);
                    }
                    else if (attachment.IsExcel)
                    {
                        // Use DocumentFormat.OpenXml for Excel documents  
                        documentContent = await ExtractExcelTextAsync(fileBytes);
                    }
                    else if (attachment.IsPdf)
                    {
                        // Use iText7 for PDF documents
                        documentContent = await ExtractPdfTextAsync(fileBytes);
                    }
                    else if (attachment.MimeType?.Contains("text") == true)
                    {
                        // Plain text files
                        documentContent = System.Text.Encoding.UTF8.GetString(fileBytes);
                    }
                    else
                    {
                        // Fallback for unsupported types
                        documentContent = $"Binary document: {attachment.FileName} ({fileBytes.Length} bytes)\n[DirectRag cannot extract text from this document type: {attachment.MimeType}]";
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[DirectRag] Unsupported document type for text extraction: {attachment.MimeType}");
                    }
                }
                catch (Exception ex)
                {
                    // If extraction fails, use metadata description
                    documentContent = $"Document: {attachment.FileName} ({fileBytes.Length} bytes)\n[Text extraction failed: {ex.Message}]";
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[DirectRag] Text extraction failed for {attachment.FileName}: {ex.Message}");
                }

                progressCallback?.Invoke($"🔍 Indexing document content for analysis...");

                // Reuse unchanged single-document indexes to avoid unnecessary re-indexing.
                // We only reuse when the attachment key matches and project index is already isolated.
                var canReuseIndex = false;
                try
                {
                    var validation = await _directRagService!.ValidateAttachmentIndexesAsync(
                        projectId,
                        new[] { attachment },
                        cancellationToken);

                    var stats = await _directRagService.GetProjectIndexStatsAsync(projectId, cancellationToken);
                    canReuseIndex =
                        validation.TryGetValue(attachment.Id, out var attachmentValidation) &&
                        attachmentValidation.State == AttachmentIndexValidationState.Match &&
                        stats.TotalDocuments == 1;
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[DirectRag] Index reuse check failed for attachment {attachment.Id}: {ex.Message}");
                }

                if (canReuseIndex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[DirectRag] Reusing existing index for unchanged attachment {attachment.Id} ({attachment.FileName}).");
                    progressCallback?.Invoke("♻️ Reusing existing index (document unchanged)...");
                }
                else
                {
                    // Isolate parsing context to the current attachment. Reusing prior project chunks
                    // can bleed IDs/content from previously parsed documents into this run.
                    var cleared = await _directRagService!.ClearProjectIndexAsync(projectId, cancellationToken);
                    if (!cleared)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[DirectRag] Could not clear existing project index for {projectId} before indexing attachment {attachment.Id}. Continuing with potential mixed context.");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[DirectRag] Cleared project index for {projectId} before indexing attachment {attachment.Id} ({attachment.FileName}).");
                    }

                    // Step 3: Index document with DirectRagService
                    var indexSuccess = await _directRagService!.IndexDocumentAsync(attachment, documentContent, projectId, cancellationToken);
                    if (!indexSuccess)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error($"[DirectRag] Failed to index document {attachment.FileName}");
                    }
                }

                progressCallback?.Invoke($"🧠 Analyzing document for requirements with AI...");
                
                // Step 4: Use DirectRag to get relevant content chunks and analyze with LLM
                var contextContent = await _directRagService!.GetRequirementAnalysisContextAsync(
                    "requirements specifications constraints criteria shall must should will system component interface protocol performance safety", 
                    projectId, 
                    maxContextChunks: 20,
                    cancellationToken);

                var contextCoverage = documentContent.Length > 0
                    ? (double)(contextContent?.Length ?? 0) / documentContent.Length
                    : 0d;
                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[DirectRag] Context coverage: {(contextCoverage * 100):F1}% ({contextContent?.Length ?? 0}/{documentContent.Length} chars)");

                // Validate we have meaningful content to analyze
                if (string.IsNullOrWhiteSpace(contextContent) || contextContent.Length < 50)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[DirectRag] Insufficient context content ({contextContent?.Length ?? 0} chars) - using full document");
                    // Use the extracted document content directly if RAG context is insufficient  
                    contextContent = documentContent.Length > 12000 ? documentContent.Substring(0, 12000) + "..." : documentContent;
                }
                else if (contextContent.Length > 12000)
                {
                    // Trim context if it's too large for the LLM
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[DirectRag] Context too large ({contextContent.Length} chars), trimming to 12000 chars");
                    contextContent = contextContent.Substring(0, 12000) + "...";
                }

                if (contextCoverage < 0.15)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn(
                        $"[DirectRag] Low context coverage detected ({(contextCoverage * 100):F1}%). Extraction may miss requirements unless recovery paths add coverage.");
                }

                // Step 5: Use Template Form Architecture (NO LEGACY FALLBACK)
                List<Requirement> extractedRequirements;
                
                if (_envelopeService != null && _textGenerationService != null)
                {
                    // Use Template Form Architecture for structured extraction with quality validation
                    TestCaseEditorApp.Services.Logging.Log.Info($"[DirectRag] Using Template Form Architecture for structured extraction");
                    extractedRequirements = await ExtractRequirementsWithTemplateFormAsync(contextContent, attachment, projectId, progressCallback, cancellationToken);
                }
                else
                {
                    // Template Form services not available - return empty list (NO LEGACY FALLBACK)
                    TestCaseEditorApp.Services.Logging.Log.Error($"[DirectRag] Template Form services unavailable - cannot extract requirements (legacy parsing disabled)");
                    progressCallback?.Invoke("❌ Template Form Architecture services required but unavailable");
                    extractedRequirements = new List<Requirement>();
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[DirectRag] Extracted {extractedRequirements.Count} requirements");
                
                // Step 6: ENHANCE with ATP derivation system (5-phase implementation)
                // Use the sophisticated derivation system to derive additional requirements from document content
                List<Requirement> derivedRequirements = new List<Requirement>();
                if (_derivationService != null)
                {
                    progressCallback?.Invoke($"🚀 Enhancing with AI capability derivation system...");
                    try
                    {
                        derivedRequirements = await DeriveRequirementsFromDocumentContentAsync(documentContent, attachment, projectId, progressCallback, onRequirementDiscovered, cancellationToken);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[DirectRag] ATP derivation system found {derivedRequirements.Count} additional derived requirements");
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[DirectRag] ATP derivation failed, continuing with basic extraction: {ex.Message}");
                    }
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[DirectRag] SystemCapabilityDerivationService not available - using basic extraction only");
                }
                
                // Step 8: Combine extracted and derived requirements (avoid duplicates by ID)
                var allRequirements = new List<Requirement>(extractedRequirements);
                var existingIds = new HashSet<string>(extractedRequirements.Select(r => r.GlobalId ?? ""), StringComparer.OrdinalIgnoreCase);
                
                foreach (var derivedReq in derivedRequirements)
                {
                    if (!string.IsNullOrEmpty(derivedReq.GlobalId) && !existingIds.Contains(derivedReq.GlobalId))
                    {
                        allRequirements.Add(derivedReq);
                        existingIds.Add(derivedReq.GlobalId);
                    }
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[DirectRag] Final result: {extractedRequirements.Count} extracted + {derivedRequirements.Count} derived = {allRequirements.Count} total requirements from {attachment.FileName}");

                var expectedMinimum = EstimateMinimumRequirements(attachment);
                if (allRequirements.Count < expectedMinimum && !string.IsNullOrWhiteSpace(documentContent))
                {
                    var deterministic = ExtractDeterministicRequirementCandidates(documentContent, attachment, projectId);
                    if (deterministic.Count > 0)
                    {
                        var promotedDeterministic = deterministic
                            .Where(c => c.IsPromoted && c.Requirement != null)
                            .ToList();

                        var deterministicExistingIds = new HashSet<string>(
                            allRequirements
                                .Where(r => !string.IsNullOrWhiteSpace(r.GlobalId))
                                .Select(r => r.GlobalId!),
                            StringComparer.OrdinalIgnoreCase);

                        var existingDescriptions = new HashSet<string>(
                            allRequirements
                                .Select(r => (r.Description ?? string.Empty).Trim())
                                .Where(d => !string.IsNullOrWhiteSpace(d)),
                            StringComparer.OrdinalIgnoreCase);

                        var addedCount = 0;
                        foreach (var candidate in promotedDeterministic)
                        {
                            var candidateReq = candidate.Requirement!;
                            var candidateId = candidateReq.GlobalId ?? string.Empty;
                            var candidateDescription = (candidateReq.Description ?? string.Empty).Trim();

                            var duplicateById = !string.IsNullOrWhiteSpace(candidateId) && deterministicExistingIds.Contains(candidateId);
                            var duplicateByDescription = !string.IsNullOrWhiteSpace(candidateDescription) && existingDescriptions.Contains(candidateDescription);

                            if (duplicateById || duplicateByDescription)
                            {
                                continue;
                            }

                            allRequirements.Add(candidateReq);
                            if (!string.IsNullOrWhiteSpace(candidateId))
                            {
                                deterministicExistingIds.Add(candidateId);
                            }
                            if (!string.IsNullOrWhiteSpace(candidateDescription))
                            {
                                existingDescriptions.Add(candidateDescription);
                            }
                            addedCount++;
                        }

                        await WriteDeterministicTraceabilityReportAsync(attachment, projectId, documentContent, deterministic, cancellationToken);
                        TestCaseEditorApp.Services.Logging.Log.Warn(
                            $"[DirectRag] Requirement count {allRequirements.Count - addedCount} below expected minimum {expectedMinimum}; deterministic capture found {deterministic.Count} candidates, qualification promoted {promotedDeterministic.Count}, augmentation added {addedCount} unique candidates.");
                        progressCallback?.Invoke(
                            $"⚠️ Requirement count below expected minimum ({allRequirements.Count - addedCount} < {expectedMinimum}); captured {deterministic.Count} candidates and promoted {addedCount} after qualification");
                    }
                }

                var rewrittenTotal = NormalizeRequirementsToTestSolutionPerspective(allRequirements, attachment);
                if (rewrittenTotal > 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[DirectRag] Applied UUT->test-solution perspective rewrite to {rewrittenTotal} requirements");
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[ATTACHMENT_TRACE] DirectRagResult AttachmentId={attachment.Id} FileName={attachment.FileName} Count={allRequirements.Count} Sample={BuildRequirementTraceSample(allRequirements)}");
                progressCallback?.Invoke($"✅ Found {allRequirements.Count} requirements: {extractedRequirements.Count} extracted + {derivedRequirements.Count} derived + deterministic fallback where needed");
                
                return allRequirements;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[DirectRag] Error processing attachment {attachment.Id}: {ex.Message}");
                progressCallback?.Invoke($"❌ Error processing document: {ex.Message}");
                return new List<Requirement>();
            }
        }

        private static string BuildRequirementTraceSample(IReadOnlyList<Requirement> requirements, int maxItems = 5)
        {
            if (requirements == null || requirements.Count == 0)
            {
                return "<none>";
            }

            var sample = requirements
                .Take(maxItems)
                .Select(r =>
                {
                    var id = !string.IsNullOrWhiteSpace(r.GlobalId)
                        ? r.GlobalId
                        : !string.IsNullOrWhiteSpace(r.Item)
                            ? r.Item
                            : "<no-id>";
                    var name = !string.IsNullOrWhiteSpace(r.Name) ? r.Name : "<no-name>";
                    return $"{id}::{name}";
                });

            return string.Join(" | ", sample);
        }

        /// <summary>
        /// Enrich requirements with attachment metadata for upstream tracing.
        /// Sets sourceAttachmentId and sourceDocumentName on each requirement so extracted requirements
        /// can be traced back to their source document within Jama.
        /// </summary>
        private static void EnrichRequirementsWithAttachmentMetadata(List<Requirement> requirements, JamaAttachment attachment)
        {
            if (requirements == null || attachment == null)
                return;

            foreach (var requirement in requirements)
            {
                if (requirement != null)
                {
                    requirement.SourceAttachmentId = attachment.Id;
                    requirement.SourceDocumentName = attachment.FileName;
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[AttachmentTracing] Enriched {requirements.Count} requirements with attachment metadata: {attachment.FileName} (ID: {attachment.Id})");
        }

        /// <summary>
        /// Set validation method to Traceability for all requirements (required for extracted requirements)
        /// </summary>
        private static void EnrichRequirementsWithValidationMethod(List<Requirement> requirements)
        {
            if (requirements == null)
                return;

            foreach (var requirement in requirements)
            {
                if (requirement != null)
                {
                    requirement.SetValidationMethods(new[] { ValidationMethod.Traceability });
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[FieldEnrichment] Set validation method to Traceability for {requirements.Count} requirements");
        }

        /// <summary>
        /// Enrich requirements with verification method using LLM assistance
        /// Selects from: Analysis, Simulation, Demonstration, Inspection, ServiceHistory, Test, TestUnintendedFunction, VerifiedAtAnotherLevel
        /// </summary>
        private async Task EnrichRequirementsWithVerificationMethodAsync(List<Requirement> requirements, CancellationToken cancellationToken = default)
        {
            if (requirements == null || _textGenerationService == null)
                return;

            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[FieldEnrichment] Starting verification method selection for {requirements.Count} requirements");

                foreach (var requirement in requirements.Where(r => r != null))
                {
                    var prompt = BuildVerificationMethodSelectionPrompt(requirement);
                    var response = await _textGenerationService.GenerateAsync(prompt, cancellationToken);
                    
                    if (string.IsNullOrWhiteSpace(response))
                        continue;

                    var selectedMethod = ParseVerificationMethodFromResponse(response);
                    if (selectedMethod.HasValue)
                    {
                        requirement.AddVerificationMethod(selectedMethod.Value);
                        requirement.Method = selectedMethod.Value;
                        TestCaseEditorApp.Services.Logging.Log.Info($"[FieldEnrichment] Assigned verification method '{selectedMethod}' to requirement {requirement.Item}");
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[FieldEnrichment] Error during verification method enrichment: {ex.Message}");
                // Non-blocking - continue with other enrichments
            }
        }

        /// <summary>
        /// Enrich requirements with allocation type using LLM assistance
        /// Selects from: Hardware, Software, Both
        /// </summary>
        private async Task EnrichRequirementsWithAllocationAsync(List<Requirement> requirements, CancellationToken cancellationToken = default)
        {
            if (requirements == null || _textGenerationService == null)
                return;

            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[FieldEnrichment] Starting allocation selection for {requirements.Count} requirements");

                foreach (var requirement in requirements.Where(r => r != null))
                {
                    var prompt = BuildAllocationSelectionPrompt(requirement);
                    var response = await _textGenerationService.GenerateAsync(prompt, cancellationToken);
                    
                    if (string.IsNullOrWhiteSpace(response))
                        continue;

                    var selectedAllocation = ParseAllocationFromResponse(response);
                    if (selectedAllocation != AllocationTarget.Unassigned)
                    {
                        requirement.Allocation = selectedAllocation;
                        TestCaseEditorApp.Services.Logging.Log.Info($"[FieldEnrichment] Assigned allocation '{selectedAllocation}' to requirement {requirement.Item}");
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[FieldEnrichment] Error during allocation enrichment: {ex.Message}");
                // Non-blocking - continue with other enrichments
            }
        }

        /// <summary>
        /// Build prompt to select verification method for a requirement
        /// </summary>
        private string BuildVerificationMethodSelectionPrompt(Requirement requirement)
        {
            return $@"Given this requirement, select the MOST APPROPRIATE verification method from: Analysis, Simulation, Demonstration, Inspection, ServiceHistory, Test, TestUnintendedFunction, VerifiedAtAnotherLevel

Requirement ID: {requirement.Item}
Requirement Name: {requirement.Name}
Requirement Description: {requirement.Description}

Guidelines:
- Analysis: for theoretical or mathematical verification
- Simulation: for computational/modeling verification
- Demonstration: for visual/operational proof
- Inspection: for physical examination or document review
- Test: for active testing with pass/fail criteria
- ServiceHistory: for proven operational performance
- TestUnintendedFunction: for edge case or negative testing
- VerifiedAtAnotherLevel: if verified at subsystem or supplier level

Respond with ONLY the selected method name, nothing else. If unsure, respond with 'Test'.";
        }

        /// <summary>
        /// Build prompt to select allocation type for a requirement
        /// </summary>
        private string BuildAllocationSelectionPrompt(Requirement requirement)
        {
            return $@"Determine whether this requirement should be allocated to Hardware, Software, or Both.

Requirement ID: {requirement.Item}
Requirement Name: {requirement.Name}
Requirement Description: {requirement.Description}

Guidelines:
- Hardware: if it describes physical components, electrical properties, mechanical constraints, or physical interfaces
- Software: if it describes algorithms, data processing, software functions, or logical behavior
- Both: if it spans hardware and software (e.g., system-level performance requirements, integrated functionality)

Respond with ONLY one of: Hardware, Software, Both. Nothing else. If unsure, respond with 'Both'.";
        }

        /// <summary>
        /// Parse verification method from LLM response
        /// </summary>
        private VerificationMethod? ParseVerificationMethodFromResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return null;

            var normalized = response.Trim().ToLower();

            if (normalized.Contains("analysis")) return VerificationMethod.Analysis;
            if (normalized.Contains("simulation")) return VerificationMethod.Simulation;
            if (normalized.Contains("demonstration")) return VerificationMethod.Demonstration;
            if (normalized.Contains("inspection")) return VerificationMethod.Inspection;
            if (normalized.Contains("servicehistory") || normalized.Contains("service history")) return VerificationMethod.ServiceHistory;
            if (normalized.Contains("test") && !normalized.Contains("unintended")) return VerificationMethod.Test;
            if (normalized.Contains("unintended")) return VerificationMethod.TestUnintendedFunction;
            if (normalized.Contains("another level")) return VerificationMethod.VerifiedAtAnotherLevel;

            return null;
        }

        /// <summary>
        /// Parse allocation from LLM response
        /// </summary>
        private AllocationTarget ParseAllocationFromResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return AllocationTarget.Unassigned;

            var normalized = response.Trim().ToLower();

            if (normalized.Contains("hardware")) return AllocationTarget.Hardware;
            if (normalized.Contains("software")) return AllocationTarget.Software;
            if (normalized.Contains("both")) return AllocationTarget.Both;

            return AllocationTarget.Unassigned;
        }

        /// <summary>
        /// Uses LLM to pre-select likely picklist labels for Jama Type and Status fields.
        /// Labels are later resolved to Jama option IDs; unresolved labels are left blank.
        /// </summary>
        private async Task EnrichRequirementsWithJamaPicklistHintsAsync(List<Requirement> requirements, CancellationToken cancellationToken = default)
        {
            if (requirements == null || _textGenerationService == null)
                return;

            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[FieldEnrichment] Starting Jama picklist hint selection for {requirements.Count} requirements");

                foreach (var requirement in requirements.Where(r => r != null))
                {
                    var typePrompt = BuildRequirementTypeSelectionPrompt(requirement);
                    var typeResponse = await _textGenerationService.GenerateAsync(typePrompt, cancellationToken);
                    var selectedType = ParseRequirementTypeFromResponse(typeResponse);
                    if (!string.IsNullOrWhiteSpace(selectedType))
                    {
                        requirement.RequirementType = selectedType;
                    }

                    var statusPrompt = BuildRequirementStatusSelectionPrompt(requirement);
                    var statusResponse = await _textGenerationService.GenerateAsync(statusPrompt, cancellationToken);
                    var selectedStatus = ParseRequirementStatusFromResponse(statusResponse);
                    if (!string.IsNullOrWhiteSpace(selectedStatus))
                    {
                        requirement.Status = selectedStatus;
                        requirement.RelationshipStatus = selectedStatus;
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[FieldEnrichment] Error during Jama picklist hint enrichment: {ex.Message}");
            }
        }

        private string BuildRequirementTypeSelectionPrompt(Requirement requirement)
        {
            return $@"Select the best requirement TYPE label for Jama from this fixed set:
System, Hardware, Software, Interface, Performance, Safety, Security, Reliability, Environmental, User Selection Required

Requirement ID: {requirement.Item}
Requirement Name: {requirement.Name}
Requirement Description: {requirement.Description}

Respond with ONLY one label from the set above.";
        }

        private string BuildRequirementStatusSelectionPrompt(Requirement requirement)
        {
            return $@"Select the best requirement STATUS label for Jama from this fixed set:
Draft, Proposed, In Review, Approved, Rejected, User Selection Required

Requirement ID: {requirement.Item}
Requirement Name: {requirement.Name}
Requirement Description: {requirement.Description}

Respond with ONLY one label from the set above.";
        }

        private static string ParseRequirementTypeFromResponse(string? response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return string.Empty;

            var normalized = response.Trim().ToLowerInvariant();
            if (normalized.Contains("system")) return "System";
            if (normalized.Contains("hardware")) return "Hardware";
            if (normalized.Contains("software")) return "Software";
            if (normalized.Contains("interface")) return "Interface";
            if (normalized.Contains("performance")) return "Performance";
            if (normalized.Contains("safety")) return "Safety";
            if (normalized.Contains("security")) return "Security";
            if (normalized.Contains("reliability")) return "Reliability";
            if (normalized.Contains("environment")) return "Environmental";
            if (normalized.Contains("user selection required")) return "User Selection Required";
            return string.Empty;
        }

        private static string ParseRequirementStatusFromResponse(string? response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return string.Empty;

            var normalized = response.Trim().ToLowerInvariant();
            if (normalized.Contains("in review")) return "In Review";
            if (normalized.Contains("approved")) return "Approved";
            if (normalized.Contains("rejected")) return "Rejected";
            if (normalized.Contains("proposed")) return "Proposed";
            if (normalized.Contains("draft")) return "Draft";
            if (normalized.Contains("user selection required")) return "User Selection Required";
            return string.Empty;
        }

        /// <summary>
        /// Build comprehensive prompt for direct requirement extraction using LLM
        /// </summary>
        private string BuildDirectExtractionPrompt(JamaAttachment attachment, string contextContent)
        {
            return $@"Extract technical requirements from this document. Look for statements that use 'shall', 'must', 'will' or 'should' and define what the system must do or how it must perform.

DOCUMENT: {attachment.FileName}

CONTENT:
{contextContent}

Find requirements like:
- ""System shall process data at 60 fps""
- ""Temperature range shall be -40°C to +85°C""  
- ""Interface shall support RS-485 protocol""

Format each requirement as:
ID: REQ-001
Text: [Complete requirement statement]
Category: [Functional/Performance/Interface/Environmental]
---

Extract all legitimate requirements:";
        }

        private List<DeterministicRequirementCandidate> ExtractDeterministicRequirementCandidates(string documentContent, JamaAttachment attachment, int projectId)
        {
            var results = new List<DeterministicRequirementCandidate>();

            if (string.IsNullOrWhiteSpace(documentContent))
            {
                return results;
            }

            var normalizedContent = System.Text.RegularExpressions.Regex.Replace(documentContent, @"\s+", " ").Trim();
            if (normalizedContent.Length == 0)
            {
                return results;
            }

            var statementRegex = new System.Text.RegularExpressions.Regex(
                @"\b(?:[A-Za-z][A-Za-z0-9_\-/ ]{1,80}\s+)?(?:shall|must|will|should)\b[\s\S]{12,260}?(?:\.|;|(?=\bTest_type\s*:)|(?=\bTest_Venue\s*:)|(?=\bID\s*:)|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            var idRegex = new System.Text.RegularExpressions.Regex(
                @"\bID\s*:\s*([A-Za-z0-9][A-Za-z0-9_.\-]*)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 1;

            var statements = statementRegex.Matches(normalizedContent).Cast<System.Text.RegularExpressions.Match>().ToList();

            foreach (var statement in statements)
            {
                var text = System.Text.RegularExpressions.Regex.Replace(statement.Value, @"\s+", " ").Trim();
                if (string.IsNullOrWhiteSpace(text) || text.Length < 20)
                {
                    continue;
                }

                if (!text.EndsWith(".", StringComparison.Ordinal))
                {
                    text += ".";
                }

                if (!seen.Add(text))
                {
                    continue;
                }

                var searchStart = Math.Max(0, statement.Index - 140);
                var searchLength = Math.Min(220, normalizedContent.Length - searchStart);
                var nearby = normalizedContent.Substring(searchStart, searchLength);

                var idMatches = idRegex.Matches(nearby).Cast<System.Text.RegularExpressions.Match>().ToList();
                var parsedId = idMatches.Count > 0 ? idMatches.Last().Groups[1].Value.Trim() : string.Empty;

                var itemId = !string.IsNullOrWhiteSpace(parsedId)
                    ? parsedId
                    : $"DOC-{index:D3}";

                var sourceClause = text;
                var normalizedDescription = LooksLikeUutRequirementForFallback(text)
                    ? RewriteUutRequirementAsTestSolutionVerificationForFallback(text)
                    : text;
                var traceReference = BuildRequirementTraceReference(attachment.Id, itemId, index);

                var qualification = QualifyDeterministicRequirementCandidate(sourceClause);

                Requirement? promotedRequirement = null;
                if (qualification.IsPromoted)
                {
                    promotedRequirement = new Requirement
                    {
                        GlobalId = !string.IsNullOrWhiteSpace(parsedId)
                            ? parsedId
                            : $"DOC-{attachment.Id}-{index:D3}",
                        Item = itemId,
                        TraceReference = traceReference,
                        Name = GenerateRequirementNameFromCapability(normalizedDescription, "Deterministic"),
                        Description = normalizedDescription,
                        RequirementType = $"Deterministic - {qualification.Classification}",
                        Rationale = $"Recovered via deterministic fallback from {attachment.FileName}\n\n**Trace Reference:** {traceReference}\n\n**Source Clause:** {sourceClause}\n\n**Qualification:** {qualification.Classification} (Score {qualification.Score}/14)\n{qualification.Reason}",
                        Heading = "Derived",
                        ItemType = "System Requirement",
                        CreatedDate = DateTime.Now,
                        ModifiedDate = DateTime.Now,
                        Project = projectId.ToString(),
                        SourceDocumentName = attachment.FileName,
                        SourceAttachmentId = attachment.Id,
                        SourceJamaItemId = attachment.Item > 0 ? attachment.Item : null,
                        TagList = new List<string>
                        {
                            "Derived",
                            "DeterministicFallback",
                            $"TraceRef:{traceReference}",
                            $"Qualification:{qualification.Classification}",
                            $"Score:{qualification.Score}"
                        }
                    };
                }

                results.Add(new DeterministicRequirementCandidate
                {
                    Requirement = promotedRequirement,
                    SourceClause = sourceClause,
                    Classification = qualification.Classification,
                    QualificationScore = qualification.Score,
                    QualificationReason = qualification.Reason,
                    IsPromoted = qualification.IsPromoted
                });
                index++;
            }

            var promotedCount = results.Count(r => r.IsPromoted);
            TestCaseEditorApp.Services.Logging.Log.Info(
                $"[DirectRag] Deterministic candidate capture found {results.Count} of {statements.Count} modal clauses; qualification promoted {promotedCount} and held {results.Count - promotedCount} for review for attachment {attachment.Id}");

            return results;
        }

        private static DeterministicQualificationResult QualifyDeterministicRequirementCandidate(string clause)
        {
            if (string.IsNullOrWhiteSpace(clause))
            {
                return new DeterministicQualificationResult(0, "Rejected Candidate", "Empty clause", false);
            }

            var normalized = System.Text.RegularExpressions.Regex.Replace(clause, @"\s+", " ").Trim();
            var lower = normalized.ToLowerInvariant();

            if (normalized.Length < 25)
            {
                return new DeterministicQualificationResult(1, "Heading/Structure", "Very short or incomplete fragment", false);
            }

            var startsWithWeakContinuation = System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                @"^(and|or|but|then|when|where|while|unless|if|it|this|these|those|the\s+measurement|the\s+default\s+states)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (startsWithWeakContinuation && normalized.Length < 80)
            {
                return new DeterministicQualificationResult(2, "Heading/Structure", "Continuation fragment without stable subject", false);
            }

            var hardRejectPatterns = new[]
            {
                "contents of this document are proprietary",
                "shall not be disclosed",
                "all rights reserved",
                "table of contents",
                "revision history"
            };

            if (hardRejectPatterns.Any(p => lower.Contains(p)))
            {
                return new DeterministicQualificationResult(0, "Rejected Candidate", "Legal/template boilerplate", false);
            }

            var obligationScore = System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\b(shall|must|required\s+to|is\s+to)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ? 2
                : System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\b(will|should)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    ? 1
                    : 0;

            var actorScore = System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                @"^\s*(?:Acceptance\s+Criteria\s+)?(?:The\s+)?(?:system|software|hardware|equipment|test\s+station|test\s+system|production\s+test|display\s+head|unit|interface|controller|module)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ? 2
                : System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\b(system|software|hardware|equipment|test|display|unit|module)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    ? 1
                    : 0;

            var actionScore = System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\b(shall|must|will|should)\s+[a-zA-Z]{3,}", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ? 2
                : 0;

            var constraintScore = System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                @"\b(within\s+the\s+range|at\s+least|at\s+most|less\s+than|greater\s+than|between|\+/-|when|if|while|for\s+at\s+least|vdc|vac|ms|degrees|%)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ? 2
                : System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\b(on|off|open|gnd|logic\s+low|logic\s+high)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    ? 1
                    : 0;

            var verifiableScore = System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                @"\b(verify|measures?|detects?|indicates?|displayed|fault|calibrate|write|program|test)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ? 2
                : System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\b(within\s+the\s+range|\+/-|at\s+least|at\s+most)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    ? 1
                    : 0;

            var scopeScore = System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                @"\b(system|software|hardware|equipment|interface|display\s+head|production\s+test|test\s+station|unit|module)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ? 2
                : 0;

            var proceduralNoise = System.Text.RegularExpressions.Regex.IsMatch(
                lower,
                @"\b(note:|recommended\s+power|procedure|suggested|for\s+example|\(e\.g\.|\(ex\.|_ref|mergeformat|table\s+\d+)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var proceduralScore = proceduralNoise ? 0 : 2;

            var score = obligationScore + actorScore + actionScore + constraintScore + verifiableScore + scopeScore + proceduralScore;

            var isVerificationLedClause = System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                @"^\s*(?:Acceptance\s+Criteria\s+)?(?:The\s+)?(?:production\s+test|test\s+station|test\s+system|test\s+procedure)\s+shall\s+verify\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var hasExplicitSystemObligation = System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                @"\b(?:the\s+)?(?:system|software|hardware|equipment|display\s+head|unit|module|interface)\s+shall\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var looksFragmented = normalized.Count(ch => ch == '[') != normalized.Count(ch => ch == ']') ||
                                 normalized.Count(ch => ch == '(') > normalized.Count(ch => ch == ')') ||
                                 normalized.EndsWith("[", StringComparison.Ordinal) ||
                                 normalized.EndsWith("(", StringComparison.Ordinal);

            if (looksFragmented && score > 0)
            {
                score = Math.Max(0, score - 3);
            }

            if (startsWithWeakContinuation && score > 0)
            {
                score = Math.Max(0, score - 2);
            }

            var strongSystemSignal = obligationScore >= 1 && actorScore >= 2 && actionScore >= 1 && (constraintScore >= 1 || verifiableScore >= 1);
            var strongVerificationSignal = obligationScore >= 1 && actorScore >= 1 && actionScore >= 1 && verifiableScore >= 1;

            var classification = score switch
            {
                >= 11 => strongSystemSignal ? "True System Requirement" : "Verification/Test Requirement",
                >= 9 => strongVerificationSignal ? "Verification/Test Requirement" : "Potential Requirement",
                >= 7 => "Potential Requirement",
                >= 4 => obligationScore > 0 ? "Derived Requirement Candidate" : "Informational Text",
                _ => looksFragmented ? "Heading/Structure" : "Rejected Candidate"
            };

            if (isVerificationLedClause && !hasExplicitSystemObligation)
            {
                classification = score >= 9 ? "Verification/Test Requirement" : "Potential Requirement";
            }

            var isPromoted = !looksFragmented && !proceduralNoise &&
                             ((classification == "True System Requirement" && score >= 11) ||
                              (classification == "Verification/Test Requirement" && score >= 9 && strongVerificationSignal));
            var reason = $"Score {score}/14 (obligation {obligationScore}, actor {actorScore}, action {actionScore}, constraint {constraintScore}, verifiable {verifiableScore}, scope {scopeScore}, non-procedural {proceduralScore}); verification-led={isVerificationLedClause}, explicit-system-obligation={hasExplicitSystemObligation}";

            return new DeterministicQualificationResult(score, classification, reason, isPromoted);
        }

        private static bool ShouldIncludeDeterministicClause(string clause)
        {
            if (string.IsNullOrWhiteSpace(clause))
            {
                return false;
            }

            var normalized = System.Text.RegularExpressions.Regex.Replace(clause, @"\s+", " ").Trim();
            var lower = normalized.ToLowerInvariant();

            if (normalized.Length < 35)
            {
                return false;
            }

            var startsWithWeakContinuation = System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                @"^(and|or|but|then|when|where|while|unless|if|it|this|these|those|the\s+measurement|the\s+default\s+states)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (startsWithWeakContinuation)
            {
                return false;
            }

            var hasModalVerb = System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                @"\b(shall|must|will|should)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!hasModalVerb)
            {
                return false;
            }

            var lowerNoisePatterns = new[]
            {
                "contents of this document are proprietary",
                "shall not be disclosed",
                "purposes expressly authorized",
                "copyright",
                "all rights reserved",
                "recommended power down procedure",
                "recommended power up procedure",
                "note:",
                "table of contents",
                "revision history"
            };

            if (lowerNoisePatterns.Any(pattern => lower.Contains(pattern)))
            {
                return false;
            }

            var badEndingPatterns = new[]
            {
                "(ex.",
                "(e.g.",
                "using ref ",
                "mergeformat",
                "_ref"
            };

            if (badEndingPatterns.Any(pattern => lower.Contains(pattern)))
            {
                return false;
            }

            var punctuationBalanceLooksBroken = normalized.Count(ch => ch == '[') != normalized.Count(ch => ch == ']') ||
                                                normalized.Count(ch => ch == '(') > normalized.Count(ch => ch == ')');

            if (punctuationBalanceLooksBroken)
            {
                return false;
            }

            var weakShouldClause = System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                @"\bshould\s+be\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase) && normalized.Length < 55;

            if (weakShouldClause)
            {
                return false;
            }

            var tokenCount = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return tokenCount >= 6;
        }

        private async Task WriteDeterministicTraceabilityReportAsync(
            JamaAttachment attachment,
            int projectId,
            string documentContent,
            IReadOnlyList<DeterministicRequirementCandidate> deterministicCandidates,
            CancellationToken cancellationToken)
        {
            try
            {
                if (deterministicCandidates.Count == 0)
                {
                    return;
                }

                var promotedRequirements = deterministicCandidates
                    .Where(c => c.IsPromoted && c.Requirement != null)
                    .ToList();
                var heldCandidates = deterministicCandidates
                    .Where(c => !c.IsPromoted)
                    .ToList();

                var allClauses = ExtractSourceClauses(documentContent);
                var normalizedUsed = promotedRequirements
                    .Select(candidate => NormalizeForTraceMatch(candidate.SourceClause))
                    .Where(s => s.Length >= 16)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var unusedClauses = allClauses
                    .Where(clause =>
                    {
                        var normalizedClause = NormalizeForTraceMatch(clause);
                        return normalizedClause.Length >= 16 &&
                               !normalizedUsed.Any(u => u.Contains(normalizedClause, StringComparison.Ordinal) || normalizedClause.Contains(u, StringComparison.Ordinal));
                    })
                    .ToList();

                var report = new StringBuilder();
                report.AppendLine("DERIVATION TRACEABILITY REPORT");
                report.AppendLine("============================");
                report.AppendLine($"GeneratedUtc: {DateTime.UtcNow:O}");
                report.AppendLine($"ProjectId: {projectId}");
                report.AppendLine($"AttachmentId: {attachment.Id}");
                report.AppendLine($"FileName: {attachment.FileName}");
                report.AppendLine($"CandidateCount: {deterministicCandidates.Count}");
                report.AppendLine($"PromotedRequirementCount: {promotedRequirements.Count}");
                report.AppendLine($"HeldCandidateCount: {heldCandidates.Count}");
                report.AppendLine($"CandidateClauseCount: {allClauses.Count}");
                report.AppendLine($"UsedClauseCount: {promotedRequirements.Count}");
                report.AppendLine($"UnusedClauseCount: {unusedClauses.Count}");
                report.AppendLine();

                report.AppendLine("REQUIREMENT -> SOURCE MAP");
                report.AppendLine("------------------------");
                for (var i = 0; i < promotedRequirements.Count; i++)
                {
                    var candidate = promotedRequirements[i];
                    report.AppendLine($"[{i + 1}] RequirementId: {candidate.Requirement.Item}");
                    report.AppendLine($"    TraceReference: {candidate.Requirement.TraceReference}");
                    report.AppendLine($"    RequirementText: {TruncateForReport(candidate.Requirement.Description, 220)}");
                    report.AppendLine($"    SourceRequirementText: {TruncateForReport(candidate.SourceClause, 220)}");
                    report.AppendLine($"    Qualification: {candidate.Classification} (Score {candidate.QualificationScore}/14)");
                    report.AppendLine($"    QualificationReason: {candidate.QualificationReason}");
                    report.AppendLine();
                }

                report.AppendLine("HELD CANDIDATES (NOT PROMOTED)");
                report.AppendLine("------------------------------");
                if (heldCandidates.Count == 0)
                {
                    report.AppendLine("<none>");
                }
                else
                {
                    for (var i = 0; i < heldCandidates.Count; i++)
                    {
                        var candidate = heldCandidates[i];
                        report.AppendLine($"[{i + 1}] Classification: {candidate.Classification}");
                        report.AppendLine($"    QualificationScore: {candidate.QualificationScore}/14");
                        report.AppendLine($"    QualificationReason: {candidate.QualificationReason}");
                        report.AppendLine($"    SourceClause: {TruncateForReport(candidate.SourceClause, 220)}");
                        report.AppendLine();
                    }
                }

                report.AppendLine("USED SOURCE CLAUSES");
                report.AppendLine("-------------------");
                for (var i = 0; i < promotedRequirements.Count; i++)
                {
                    report.AppendLine($"[{i + 1}] {promotedRequirements[i].SourceClause}");
                }

                report.AppendLine();
                report.AppendLine("UNUSED SOURCE CLAUSES (REVIEW FOR MISSED REQUIREMENTS)");
                report.AppendLine("------------------------------------------------------");
                if (unusedClauses.Count == 0)
                {
                    report.AppendLine("<none>");
                }
                else
                {
                    for (var i = 0; i < unusedClauses.Count; i++)
                    {
                        report.AppendLine($"[{i + 1}] {unusedClauses[i]}");
                    }
                }

                var reportDirectory = Path.Combine(Environment.CurrentDirectory, "exports", "traceability-reports");
                Directory.CreateDirectory(reportDirectory);
                var safeFileName = SanitizeFileNameWithoutExtension(attachment.FileName);
                var reportPath = Path.Combine(
                    reportDirectory,
                    $"derivation-trace-{DateTime.UtcNow:yyyyMMdd-HHmmss}-att-{attachment.Id}-{safeFileName}.txt");

                await File.WriteAllTextAsync(reportPath, report.ToString(), cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Info($"[DerivationTrace] Wrote deterministic traceability report: {reportPath}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[DerivationTrace] Failed to write deterministic traceability report for attachment {attachment.Id}: {ex.Message}");
            }
        }

        private static bool LooksLikeUutRequirementForFallback(string requirementText)
        {
            if (string.IsNullOrWhiteSpace(requirementText))
            {
                return false;
            }

            var normalized = NormalizeRequirementPrefixForFallback(requirementText);

            // Already in the target perspective.
            if (System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                @"^\s*(?:The\s+)?(?:test\s+solution|test\s+system)\s+shall\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return false;
            }

            return System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                @"^\s*(?:The\s+)?(?:MFD|UUT|Unit\s+Under\s+Test|LRU|Aircraft|Display\s+Unit|Avionics\s+Unit)(?:\s+[A-Za-z0-9_\-/()]+){0,4}\s+shall\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static string RewriteUutRequirementAsTestSolutionVerificationForFallback(string requirementText)
        {
            var cleaned = NormalizeRequirementPrefixForFallback(requirementText).Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return "The test solution shall verify required UUT behavior.";
            }

            var rewriteMatch = System.Text.RegularExpressions.Regex.Match(
                cleaned,
                @"^(?:The\s+)?(?<subject>MFD|UUT|Unit\s+Under\s+Test|LRU|Aircraft|Display\s+Unit|Avionics\s+Unit(?:\s+[A-Za-z0-9_\-/()]+){0,4})\s+shall\s+(?<predicate>.+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (rewriteMatch.Success)
            {
                var subject = rewriteMatch.Groups["subject"].Value.Trim();
                var predicate = rewriteMatch.Groups["predicate"].Value.Trim().TrimEnd('.');
                var normalizedSubject = subject.Equals("UUT", StringComparison.OrdinalIgnoreCase)
                    ? "the unit under test (UUT)"
                    : subject.StartsWith("the ", StringComparison.OrdinalIgnoreCase)
                        ? subject
                        : $"the {subject}";

                return $"The test solution shall verify that {normalizedSubject} {predicate}.";
            }

            return $"The test solution shall verify that {char.ToLowerInvariant(cleaned[0])}{cleaned.Substring(1)}.";
        }

        private static string NormalizeRequirementPrefixForFallback(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text;

            // Remove leading bracketed tags like [REQ] [UUT].
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"^\s*(?:\[[^\]]+\]\s*)+", string.Empty);

            // Remove list numbering like "1.", "1.2)", etc.
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"^\s*(?:\d+(?:\.\d+)*[\).:-]\s+)+", string.Empty);

            // Remove leading requirement IDs like "DECAGON-REQ_RC-12:".
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"^\s*[A-Za-z0-9][A-Za-z0-9_.\-]{1,60}\s*:\s*", string.Empty);

            // Remove leading requirement IDs without colon when followed by a known UUT subject,
            // e.g. "C4B_ATR-121 The MFD shall ...".
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized,
                @"^\s*[A-Za-z0-9][A-Za-z0-9_.\-]{2,80}\s+(?=(?:The\s+)?(?:MFD|UUT|Unit\s+Under\s+Test|LRU|Aircraft|Display\s+Unit|Avionics\s+Unit)\b)",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return normalized.Trim();
        }

        private int NormalizeRequirementsToTestSolutionPerspective(List<Requirement> requirements, JamaAttachment attachment)
        {
            if (requirements == null || requirements.Count == 0)
            {
                return 0;
            }

            var rewriteCount = 0;

            foreach (var requirement in requirements)
            {
                if (requirement == null || string.IsNullOrWhiteSpace(requirement.Description))
                {
                    continue;
                }

                var description = requirement.Description;
                var newlineIndex = description.IndexOf('\n');
                var leadClause = newlineIndex >= 0 ? description.Substring(0, newlineIndex).Trim() : description.Trim();
                if (!LooksLikeUutRequirementForFallback(leadClause))
                {
                    continue;
                }

                var suffix = newlineIndex >= 0 ? description.Substring(newlineIndex) : string.Empty;
                var rewrittenClause = RewriteUutRequirementAsTestSolutionVerificationForFallback(leadClause);

                requirement.Description = rewrittenClause + suffix;
                requirement.Name = GenerateRequirementNameFromCapability(rewrittenClause, "Derived");
                rewriteCount++;

                if (string.IsNullOrWhiteSpace(requirement.Rationale))
                {
                    requirement.Rationale = $"Boundary rewrite applied from UUT perspective source in {attachment.FileName}.";
                }
                else if (!requirement.Rationale.Contains("Boundary rewrite", StringComparison.OrdinalIgnoreCase))
                {
                    requirement.Rationale = string.Concat(requirement.Rationale, "\n\nBoundary rewrite applied from UUT perspective source.");
                }
            }

            return rewriteCount;
        }

        private sealed class DeterministicRequirementCandidate
        {
            public Requirement? Requirement { get; init; }
            public string SourceClause { get; init; } = string.Empty;
            public string Classification { get; init; } = string.Empty;
            public int QualificationScore { get; init; }
            public string QualificationReason { get; init; } = string.Empty;
            public bool IsPromoted { get; init; }
        }

        private readonly record struct DeterministicQualificationResult(int Score, string Classification, string Reason, bool IsPromoted);

        /// <summary>
        /// Parse LLM response text into structured Requirement objects
        /// </summary>
        private List<Requirement> ParseRequirementsFromText(string llmResponse, JamaAttachment attachment)
        {
            var requirements = new List<Requirement>();
            
            try
            {
                var sections = llmResponse.Split(new[] { "---" }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var section in sections)
                {
                    if (string.IsNullOrWhiteSpace(section)) continue;
                    
                    var lines = section.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    
                    string? id = null, text = null, category = null, page = null, sectionRef = null;
                    
                    foreach (var line in lines)
                    {
                        var trimmedLine = line.Trim();
                        if (trimmedLine.StartsWith("ID:", StringComparison.OrdinalIgnoreCase))
                            id = trimmedLine.Substring(3).Trim();
                        else if (trimmedLine.StartsWith("Text:", StringComparison.OrdinalIgnoreCase))
                            text = trimmedLine.Substring(5).Trim();
                        else if (trimmedLine.StartsWith("Category:", StringComparison.OrdinalIgnoreCase))
                            category = trimmedLine.Substring(9).Trim();
                        else if (trimmedLine.StartsWith("Page:", StringComparison.OrdinalIgnoreCase))
                            page = trimmedLine.Substring(5).Trim();
                        else if (trimmedLine.StartsWith("Section:", StringComparison.OrdinalIgnoreCase))
                            sectionRef = trimmedLine.Substring(8).Trim();
                    }
                    
                    if (!string.IsNullOrEmpty(text) && IsValidRequirement(text))
                    {
                        // Build enhanced source information with page and section details
                        var sourceInfo = new List<string>();
                        
                        if (!string.IsNullOrEmpty(page))
                            sourceInfo.Add(page);
                        if (!string.IsNullOrEmpty(sectionRef))
                            sourceInfo.Add(sectionRef);
                        
                        var sourceLine = sourceInfo.Count > 0 ? string.Join(", ", sourceInfo) : "Source not specified";
                        
                        var requirement = new Requirement
                        {
                            GlobalId = id ?? $"SYS-REQ-{requirements.Count + 1:D3}",
                            Item = id ?? $"SYS-REQ-{requirements.Count + 1:D3}",
                            Name = category ?? "System Requirement",
                            Description = $"{text}\n\nSource: {sourceLine}\nFrom: {attachment.FileName}",
                            TraceReference = BuildRequirementTraceReference(attachment.Id, id, requirements.Count + 1),
                            SourceDocumentName = attachment.FileName,
                            SourceAttachmentId = attachment.Id,
                            SourceJamaItemId = attachment.Item > 0 ? attachment.Item : null
                        };
                        requirements.Add(requirement);
                    }
                    else if (!string.IsNullOrEmpty(text))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[DirectRag] Filtered component/test-level requirement: {text.Substring(0, Math.Min(50, text.Length))}...");
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[DirectRag] Error parsing requirements from LLM response");
            }

            NormalizeRequirementsToTestSolutionPerspective(requirements, attachment);
            
            return requirements;
        }

        private static string BuildRequirementFocusedExcerpt(string documentContent, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(documentContent))
            {
                return string.Empty;
            }

            var cleaned = System.Text.RegularExpressions.Regex.Replace(documentContent, @"\s+", " ").Trim();
            var requirementSegments = new List<string>();

            var segmentRegex = new System.Text.RegularExpressions.Regex(
                @"(?:\bID\s*:\s*[A-Za-z0-9_.\-]+\b\s*)?.{0,80}\b(?:shall|must|will|should)\b.{0,260}(?:\.|;|(?=\bID\s*:)|(?=\bTest_type\s*:)|(?=\bTest_Venue\s*:)|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match match in segmentRegex.Matches(cleaned))
            {
                var segment = System.Text.RegularExpressions.Regex.Replace(match.Value, @"\s+", " ").Trim();
                if (!string.IsNullOrWhiteSpace(segment) && segment.Length > 20)
                {
                    requirementSegments.Add(segment);
                }
            }

            var selected = requirementSegments.Count > 0
                ? string.Join("\n", requirementSegments.Distinct(StringComparer.OrdinalIgnoreCase))
                : cleaned;

            if (selected.Length > maxChars)
            {
                return selected.Substring(0, maxChars) + "...";
            }

            return selected;
        }

        private static RequirementExtractionEnvelope? TryRecoverEnvelopeFromLooseJson(string llmResponse, string documentName)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                return null;
            }

            var requirements = new List<ExtractedRequirement>();
            var objectMatches = System.Text.RegularExpressions.Regex.Matches(
                llmResponse,
                @"\{[^{}]*\""text\""\s*:\s*\""(?<text>(?:\\.|[^\""\\])*)\""[^{}]*\}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match match in objectMatches)
            {
                var rawObject = match.Value;
                var text = ExtractJsonStringValue(rawObject, "text");
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                requirements.Add(new ExtractedRequirement
                {
                    Id = ExtractJsonStringValue(rawObject, "id"),
                    Text = text,
                    Category = ExtractJsonStringValue(rawObject, "category"),
                    Page = ExtractJsonStringValue(rawObject, "page"),
                    Section = ExtractJsonStringValue(rawObject, "section"),
                    Confidence = ExtractJsonDoubleValue(rawObject, "confidence") ?? 0.7
                });
            }

            if (requirements.Count == 0)
            {
                return null;
            }

            return new RequirementExtractionEnvelope
            {
                Requirements = requirements,
                Metadata = new ExtractionMetadata
                {
                    TotalRequirements = requirements.Count,
                    DocumentName = documentName,
                    ExtractionMethod = "template_form_loose_json_recovery"
                }
            };
        }

        private static string? ExtractJsonStringValue(string source, string field)
        {
            var pattern = $@"\""{field}\""\s*:\s*\""(?<value>(?:\\.|[^\""\\])*)\""";
            var match = System.Text.RegularExpressions.Regex.Match(
                source,
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            if (!match.Success)
            {
                return null;
            }

            var raw = match.Groups["value"].Value;
            return System.Text.RegularExpressions.Regex.Unescape(raw).Trim();
        }

        private static double? ExtractJsonDoubleValue(string source, string field)
        {
            var pattern = $@"\""{field}\""\s*:\s*(?<value>-?\d+(?:\.\d+)?)";
            var match = System.Text.RegularExpressions.Regex.Match(
                source,
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            if (!match.Success)
            {
                return null;
            }

            if (double.TryParse(match.Groups["value"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return null;
        }

        /// <summary>
        /// Validates that extracted text represents a genuine requirement (system, functional, or interface level)
        /// Updated to be less restrictive while maintaining quality
        /// </summary>
        private bool IsValidRequirement(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 15) // Reduced from 30
                return false;

            var lowerText = text.Trim().ToLowerInvariant();

            // Must contain requirement language
            if (!lowerText.Contains("shall") && !lowerText.Contains("must") && !lowerText.Contains("will") && !lowerText.Contains("should"))
                return false;

            // Filter out only clearly non-requirements (keep component requirements)
            if (lowerText.Contains("test shall verify") ||        // Test procedures
                lowerText.Contains("shall be tested") ||          // Test descriptions  
                lowerText.Contains("inspection shall") ||         // QA procedures
                lowerText.Contains("documentation shall") ||      // Doc requirements
                lowerText.StartsWith("note:") ||                 // Notes/comments
                lowerText.StartsWith("example:"))                // Examples
                return false;

            // Filter out incomplete phrases and fragments  
            if (lowerText.StartsWith("shall not be performed on") ||
                lowerText.StartsWith("not be performed") ||
                lowerText.StartsWith("is not applicable") ||
                lowerText.StartsWith("does not apply") ||
                lowerText.StartsWith("not required for") ||
                lowerText.Contains("see section") ||
                lowerText.Contains("refer to") ||
                lowerText.Contains("as defined in"))
                return false;

            // Look for requirement indicators (more inclusive)
            bool hasRequirementIndicators = 
                // System level
                lowerText.Contains("system ") || lowerText.Contains("overall ") || lowerText.Contains("end-to-end") ||
                // Interface/connectivity  
                lowerText.Contains("interface ") || lowerText.Contains("connection") || lowerText.Contains("communication") ||
                lowerText.Contains("ethernet") || lowerText.Contains("network") || lowerText.Contains("protocol") ||
                // Performance/operational
                lowerText.Contains("performance") || lowerText.Contains("speed") || lowerText.Contains("rate") ||
                lowerText.Contains("accuracy") || lowerText.Contains("latency") || lowerText.Contains("throughput") ||
                // Power/environment
                lowerText.Contains("power") || lowerText.Contains("voltage") || lowerText.Contains("current") ||
                lowerText.Contains("temperature") || lowerText.Contains("operating") || lowerText.Contains("environment") ||
                // Functional behavior
                lowerText.Contains("function") || lowerText.Contains("operation") || lowerText.Contains("control") ||
                lowerText.Contains("monitor") || lowerText.Contains("detect") || lowerText.Contains("provide") ||
                // Input/output
                lowerText.Contains("input") || lowerText.Contains("output") || lowerText.Contains("signal") ||
                // Standards/compliance  
                lowerText.Contains("standard") || lowerText.Contains("specification") || lowerText.Contains("compliance") ||
                // Basic requirement words
                lowerText.Contains("requirement") || lowerText.Contains("capability") || lowerText.Contains("feature");

            // Must be a complete sentence with reasonable content (reduced from 8 words)
            var words = lowerText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 5) // Reduced minimum word count
                return false;

            // Accept if it has requirement indicators OR if it looks like a technical specification
            return hasRequirementIndicators || 
                   (lowerText.Contains("mhz") || lowerText.Contains("volts") || lowerText.Contains("amps") || 
                    lowerText.Contains("degrees") || lowerText.Contains("meters") || lowerText.Contains("seconds"));
        }

        /// <summary>
        /// Extract text from Word document using DocumentFormat.OpenXml
        /// </summary>
        private async Task<string> ExtractWordTextAsync(byte[] wordBytes)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var stream = new MemoryStream(wordBytes);
                    using var wordDoc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
                    
                    var body = wordDoc.MainDocumentPart?.Document?.Body;
                    if (body == null) return "";

                    var text = new StringBuilder();
                    foreach (var paragraph in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                    {
                        text.AppendLine(paragraph.InnerText);
                    }

                    // Also extract from tables
                    foreach (var table in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Table>())
                    {
                        foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
                        {
                            var rowText = string.Join("\t", row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>()
                                .Select(cell => cell.InnerText));
                            text.AppendLine(rowText);
                        }
                    }

                    return text.ToString();
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, "[DirectRag] Failed to extract Word document text");
                    throw;
                }
            });
        }

        /// <summary>
        /// Extract text from Excel document using DocumentFormat.OpenXml
        /// </summary>
        private async Task<string> ExtractExcelTextAsync(byte[] excelBytes)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var stream = new MemoryStream(excelBytes);
                    using var spreadSheet = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(stream, false);
                    
                    var workbookPart = spreadSheet.WorkbookPart;
                    if (workbookPart == null) return "";

                    var text = new StringBuilder();

                    // Extract from all worksheets
                    foreach (var worksheetPart in workbookPart.WorksheetParts)
                    {
                        var worksheet = worksheetPart.Worksheet;
                        var sheetData = worksheet.Elements<DocumentFormat.OpenXml.Spreadsheet.SheetData>().FirstOrDefault();
                        if (sheetData == null) continue;

                        foreach (var row in sheetData.Elements<DocumentFormat.OpenXml.Spreadsheet.Row>())
                        {
                            var rowTexts = new List<string>();
                            foreach (var cell in row.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>())
                            {
                                var cellText = GetCellText(cell, workbookPart);
                                rowTexts.Add(cellText);
                            }
                            text.AppendLine(string.Join("\t", rowTexts));
                        }
                    }

                    return text.ToString();
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, "[DirectRag] Failed to extract Excel document text");
                    throw;
                }
            });
        }

        /// <summary>
        /// Get text value from Excel cell
        /// </summary>
        private string GetCellText(DocumentFormat.OpenXml.Spreadsheet.Cell cell, DocumentFormat.OpenXml.Packaging.WorkbookPart workbookPart)
        {
            try
            {
                var cellValue = cell.CellValue?.Text;
                if (string.IsNullOrEmpty(cellValue)) return "";

                // Handle shared string
                if (cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString)
                {
                    var sharedStringPart = workbookPart.SharedStringTablePart;
                    if (sharedStringPart != null && int.TryParse(cellValue, out int sharedStringId))
                    {
                        var sharedStringItem = sharedStringPart.SharedStringTable.Elements<DocumentFormat.OpenXml.Spreadsheet.SharedStringItem>().ElementAtOrDefault(sharedStringId);
                        return sharedStringItem?.InnerText ?? "";
                    }
                }

                return cellValue;
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Extract text from PDF document using iText7
        /// </summary>
        private async Task<string> ExtractPdfTextAsync(byte[] pdfBytes)
        {
            // Add timeout protection to prevent indefinite hanging on complex/large PDFs
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(2)); // 2 minute timeout for PDF extraction
            
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        using var stream = new MemoryStream(pdfBytes);
                        using var reader = new PdfReader(stream);
                        using var pdfDoc = new PdfDocument(reader);
                        
                        var text = new StringBuilder();
                        var totalPages = pdfDoc.GetNumberOfPages();
                        
                        TestCaseEditorApp.Services.Logging.Log.Info($"[DirectRag] Starting PDF text extraction from {totalPages} pages ({pdfBytes.Length} bytes)");
                        
                        // Extract text from all pages with progress logging
                        for (int pageNum = 1; pageNum <= totalPages; pageNum++)
                        {
                            // Check for cancellation periodically
                            cancellationTokenSource.Token.ThrowIfCancellationRequested();
                            
                            var page = pdfDoc.GetPage(pageNum);
                            var strategy = new SimpleTextExtractionStrategy(); 
                            var pageText = PdfTextExtractor.GetTextFromPage(page, strategy);
                            
                            if (!string.IsNullOrWhiteSpace(pageText))
                            {
                                text.AppendLine($"--- Page {pageNum} ---");
                                text.AppendLine(pageText);
                                text.AppendLine();
                            }
                            
                            // Log progress for large documents
                            if (pageNum % 10 == 0 || pageNum == totalPages)
                            {
                                TestCaseEditorApp.Services.Logging.Log.Debug($"[DirectRag] PDF extraction progress: {pageNum}/{totalPages} pages processed");
                            }
                        }
                        
                        var result = text.ToString();
                        TestCaseEditorApp.Services.Logging.Log.Info($"[DirectRag] Successfully extracted {result.Length} characters from {totalPages} pages");
                        return result;
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error(ex, "[DirectRag] Failed to extract PDF text using iText7");
                        throw;
                    }
                }, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[DirectRag] ⏰ PDF extraction timed out after 2 minutes for document ({pdfBytes.Length} bytes)");
                return $"[PDF Extraction Timeout] Document processing timed out after 2 minutes. Document size: {pdfBytes.Length} bytes.\n" +
                       "[This document may be too complex for automated text extraction. Please try a smaller or simpler PDF file.]";
            }
        }

        /// <summary>
        /// Derive requirements from document content using SystemCapabilityDerivationService (ATP derivation system from 5 phases)
        /// </summary>
        private async Task<List<Requirement>> DeriveRequirementsFromDocumentContentAsync(string documentContent, JamaAttachment attachment, int projectId, System.Action<string>? progressCallback = null, System.Action<Requirement>? onRequirementDiscovered = null, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_derivationService == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] SystemCapabilityDerivationService not available - falling back to basic extraction");
                    return new List<Requirement>();
                }

                if (string.IsNullOrWhiteSpace(documentContent))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Empty document content - cannot derive requirements");
                    return new List<Requirement>();
                }

                // CRITICAL: Verify Ollama is still available before starting ATP derivation
                if (!await IsOllamaAvailableAsync(progressCallback, cancellationToken))
                {
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Ollama not available for ATP derivation - aborting intelligent analysis");
                    progressCallback?.Invoke("⚠️ Ollama service unavailable - skipping AI-powered requirement derivation");
                    return new List<Requirement>(); // Fall back gracefully
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] 🚀 Using ATP derivation system to derive requirements from {attachment.FileName} ({documentContent.Length} characters)");
                progressCallback?.Invoke($"🚀 Analyzing document with AI capability derivation system...");

                // Fast deterministic pre-scan to estimate complexity and set realistic runtime budgets.
                var preScan = BuildDerivationPreScanEstimate(documentContent, attachment);
                progressCallback?.Invoke(
                    $"⚡ Quick pre-scan: ~{preScan.EstimatedRequirementCandidates} candidate requirements from {preScan.FileSizeKb:N0} KB " +
                    $"({preScan.NonEmptyLineCount:N0} non-empty lines). Estimated AI derivation: {FormatDurationRange(preScan.EstimatedDuration)}");

                // Configure derivation options for general document processing (not just ATP)
                var derivationOptions = new DerivationOptions
                {
                    SystemType = "General", // Not specific to any system type
                    EnableQualityScoring = true,
                    IncludeRejectionAnalysis = true,
                    MaxProcessingTime = preScan.RecommendedMaxProcessingTime,
                    PerStepTimeout = preScan.RecommendedPerStepTimeout,
                    SourceMetadata = new Dictionary<string, string>
                    {
                        ["SourceDocument"] = attachment.FileName,
                        ["DocumentType"] = GetDocumentTypeDescription(attachment),
                        ["AttachmentId"] = attachment.Id.ToString(),
                        ["ProjectId"] = projectId.ToString(),
                        ["PreScanEstimatedCandidates"] = preScan.EstimatedRequirementCandidates.ToString(),
                        ["PreScanNonEmptyLines"] = preScan.NonEmptyLineCount.ToString(),
                        ["PreScanFileSizeKb"] = preScan.FileSizeKb.ToString("F0"),
                        ["AdaptiveBudgetSeconds"] = ((int)preScan.RecommendedMaxProcessingTime.TotalSeconds).ToString(),
                        ["AdaptivePerStepTimeoutSeconds"] = ((int)preScan.RecommendedPerStepTimeout.TotalSeconds).ToString(),
                        ["SystemBoundary"] = "Test Solution (station hardware/software)",
                        ["SourceRequirementType"] = "UUT ATR requirement",
                        ["DerivationMode"] = "TwoStageBoundaryAware",
                        ["DerivationIntent"] = "Derive test-solution verification requirements from MFD ATR source requirements"
                    }
                };

                // Use the 5-phase ATP derivation system to analyze the document content
                progressCallback?.Invoke(
                    $"🧠 Running two-stage AI derivation on {documentContent.Length:N0} characters " +
                    $"with adaptive budget {FormatDurationCompact(preScan.RecommendedMaxProcessingTime)} " +
                    $"(per-step timeout {FormatDurationCompact(preScan.RecommendedPerStepTimeout)})...");
                
                // Add timeout for ATP derivation and enforce it at await call-site.
                var derivationTimeout = preScan.RecommendedMaxProcessingTime;
                var derivationCts = new CancellationTokenSource();
                derivationCts.CancelAfter(derivationTimeout);
                
                // Combine the provided cancellation token with the timeout
                var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, derivationCts.Token);
                
                // NOTE: Disabled periodic generic progress - SystemCapabilityDerivationService now provides detailed per-step progress
                // var progressTask = ProvidePeriodicDerivationProgressAsync(progressCallback, derivationCts.Token);
                
                try
                {
                    // Auto-retry policy: avoid user decisions in runtime flows.
                    Func<List<SkippedAtpStep>, Task<TimeoutRetryDecision>> retryCallback = (skippedSteps) =>
                    {
                        if (skippedSteps == null || skippedSteps.Count == 0)
                        {
                            return Task.FromResult(new TimeoutRetryDecision { ShouldRetry = false, ExtendedTimeout = TimeSpan.Zero });
                        }

                        // For smaller jobs use a longer retry timeout. For larger jobs keep bounded retries.
                        var extendedTimeout = skippedSteps.Count <= 20
                            ? TimeSpan.FromSeconds(60)
                            : TimeSpan.FromSeconds(30);

                        TestCaseEditorApp.Services.Logging.Log.Info(
                            $"[JamaDocumentParser] Auto-retrying {skippedSteps.Count} timed-out ATP steps with {extendedTimeout.TotalSeconds}s timeout per step (preScanAllowRetry={preScan.AllowRetry}, estimatedCandidates={preScan.EstimatedRequirementCandidates}).");

                        progressCallback?.Invoke(
                            $"🔄 Auto-retry enabled: retrying {skippedSteps.Count} timed-out steps with {extendedTimeout.TotalSeconds:0}s timeout.");

                        return Task.FromResult(new TimeoutRetryDecision
                        {
                            ShouldRetry = true,
                            ExtendedTimeout = extendedTimeout
                        });
                    };
                    
                    var derivationTask = _derivationService.DeriveCapabilitiesAsync(documentContent, derivationOptions, progressCallback, retryCallback, onRequirementDiscovered);
                    var derivationResult = await derivationTask.WaitAsync(combinedCts.Token);
                    derivationCts.Cancel(); // Stop progress updates
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Derivation completed: {derivationResult.DerivedCapabilities.Count} capabilities derived, {derivationResult.RejectedItems.Count} items filtered out");
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Quality score: {derivationResult.QualityScore:F2}, Processing time: {derivationResult.ProcessingTime.TotalSeconds:F1}s");

                    if (derivationResult.ProcessingWarnings.Count > 0)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Derivation warnings: {string.Join(", ", derivationResult.ProcessingWarnings)}");
                    }

                    // Convert derived capabilities to requirements
                    progressCallback?.Invoke($"📋 Converting {derivationResult.DerivedCapabilities.Count} derived capabilities to requirements...");
                    var derivedRequirements = ConvertDerivedCapabilitiesToRequirements(derivationResult.DerivedCapabilities, attachment, projectId);
                    var rewrittenDerived = NormalizeRequirementsToTestSolutionPerspective(derivedRequirements, attachment);
                    if (rewrittenDerived > 0)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Applied UUT->test-solution perspective rewrite to {rewrittenDerived} derived requirements before traceability reporting");
                    }

                    // Emit a human-reviewable traceability report showing used and unused source clauses.
                    await WriteDerivationTraceabilityReportAsync(
                        attachment,
                        projectId,
                        documentContent,
                        derivationResult.DerivedCapabilities,
                        derivationResult.RejectedItems,
                        derivedRequirements,
                        cancellationToken);
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Successfully derived {derivedRequirements.Count} requirements from {attachment.FileName} using ATP derivation system");
                    progressCallback?.Invoke($"✅ ATP Derivation Complete: Generated {derivedRequirements.Count} requirements via phi4-mini → A-N taxonomy → capability synthesis");

                    return derivedRequirements;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (derivationCts.Token.IsCancellationRequested)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] ⏰ ATP derivation timed out after {derivationTimeout.TotalMinutes} minutes for {attachment.FileName}");
                    progressCallback?.Invoke($"⏰ AI derivation timed out after {derivationTimeout.TotalMinutes} minutes - falling back to basic extraction");
                    return new List<Requirement>(); // Return empty list and continue with basic extraction
                }
                finally
                {
                    derivationCts.Cancel(); // Ensure progress task is stopped
                    combinedCts.Dispose(); // Clean up the combined cancellation token source
                    derivationCts.Dispose(); // Clean up the timeout cancellation token source
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaDocumentParser] Error deriving requirements from {attachment.FileName}: {ex.Message}");
                progressCallback?.Invoke($"❌ Requirement derivation failed: {ex.Message}");
                return new List<Requirement>();
            }
        }

        private sealed class DerivationPreScanEstimate
        {
            public int EstimatedRequirementCandidates { get; init; }
            public int NonEmptyLineCount { get; init; }
            public double FileSizeKb { get; init; }
            public TimeSpan EstimatedDuration { get; init; }
            public TimeSpan RecommendedMaxProcessingTime { get; init; }
            public TimeSpan RecommendedPerStepTimeout { get; init; }
            public bool AllowRetry { get; init; }
        }

        private static DerivationPreScanEstimate BuildDerivationPreScanEstimate(string documentContent, JamaAttachment attachment)
        {
            var nonEmptyLines = documentContent
                .Split('\n', StringSplitOptions.None)
                .Select(line => line.Trim())
                .Count(line => !string.IsNullOrWhiteSpace(line));

            var shallMatches = System.Text.RegularExpressions.Regex.Matches(
                documentContent,
                @"\bshall\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;

            var numberedStepMatches = System.Text.RegularExpressions.Regex.Matches(
                documentContent,
                @"^\s*(?:Step\s+)?\d+(?:\.\d+)*(?:\.[A-Za-z])?\s*[:.]?\s+",
                System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;

            var structuredIdMatches = System.Text.RegularExpressions.Regex.Matches(
                documentContent,
                @"\bID\s*:\s*[A-Za-z0-9][A-Za-z0-9_.\-]*\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;

            var fileSizeKb = Math.Max(1d, attachment.FileSize / 1024d);

            // Deterministic rough count: prefer explicit requirement/step markers, then blend structured IDs.
            var roughCandidates = Math.Max(shallMatches, numberedStepMatches) + (int)Math.Round(structuredIdMatches * 0.5);
            roughCandidates = Math.Max(8, Math.Min(roughCandidates, 350));

            // Predict total derivation time using candidate count + file size as coarse complexity features.
            var predictedSeconds = 35d + (roughCandidates * 2.2) + (fileSizeKb / 512d * 10d);
            predictedSeconds = Math.Max(45d, Math.Min(predictedSeconds, 15 * 60d));
            var estimatedDuration = TimeSpan.FromSeconds(predictedSeconds);

            // Keep user-facing flow near target SLA while allowing mild scaling for larger/denser docs.
            var budgetSeconds = Math.Max(120d, Math.Min(predictedSeconds * 1.35d, 6 * 60d));
            var maxProcessing = TimeSpan.FromSeconds(budgetSeconds);

            var perStepTimeoutSeconds = roughCandidates > 120
                ? 12
                : roughCandidates > 70
                    ? 15
                    : 20;

            return new DerivationPreScanEstimate
            {
                EstimatedRequirementCandidates = roughCandidates,
                NonEmptyLineCount = nonEmptyLines,
                FileSizeKb = fileSizeKb,
                EstimatedDuration = estimatedDuration,
                RecommendedMaxProcessingTime = maxProcessing,
                RecommendedPerStepTimeout = TimeSpan.FromSeconds(perStepTimeoutSeconds),
                AllowRetry = roughCandidates <= 60 && fileSizeKb <= 4096
            };
        }

        private static string FormatDurationCompact(TimeSpan value)
        {
            if (value.TotalMinutes >= 1)
            {
                return $"{Math.Round(value.TotalMinutes)}m";
            }

            return $"{Math.Round(value.TotalSeconds)}s";
        }

        private static string FormatDurationRange(TimeSpan estimate)
        {
            var lowSeconds = Math.Max(30, estimate.TotalSeconds * 0.7);
            var highSeconds = Math.Min(15 * 60, estimate.TotalSeconds * 1.4);
            return $"{FormatDurationCompact(TimeSpan.FromSeconds(lowSeconds))}-{FormatDurationCompact(TimeSpan.FromSeconds(highSeconds))}";
        }

        private async Task WriteDerivationTraceabilityReportAsync(
            JamaAttachment attachment,
            int projectId,
            string documentContent,
            IReadOnlyList<DerivedCapability> capabilities,
            IReadOnlyList<RejectedItem> rejectedItems,
            IReadOnlyList<Requirement> derivedRequirements,
            CancellationToken cancellationToken)
        {
            try
            {
                var clauses = ExtractSourceClauses(documentContent);
                if (clauses.Count == 0)
                {
                    return;
                }

                var requirementIds = new List<string>();
                var usedFragments = new List<string>();

                for (var i = 0; i < capabilities.Count; i++)
                {
                    var cap = capabilities[i];
                    var req = i < derivedRequirements.Count ? derivedRequirements[i] : null;
                    var reqId = req?.Item ?? req?.GlobalId ?? $"DERIVED-{i + 1:D3}";
                    requirementIds.Add(reqId);

                    if (!string.IsNullOrWhiteSpace(cap.SourceATPStep))
                    {
                        usedFragments.Add(cap.SourceATPStep);
                    }

                    if (cap.SourceMetadata != null &&
                        cap.SourceMetadata.TryGetValue("SourceRequirementText", out var sourceReqText) &&
                        !string.IsNullOrWhiteSpace(sourceReqText))
                    {
                        usedFragments.Add(sourceReqText);
                    }

                    if (!string.IsNullOrWhiteSpace(req?.Description))
                    {
                        usedFragments.Add(req.Description);
                    }
                }

                var normalizedUsed = usedFragments
                    .Select(NormalizeForTraceMatch)
                    .Where(s => s.Length >= 16)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var normalizedRejected = (rejectedItems ?? Array.Empty<RejectedItem>())
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.OriginalText))
                    .Select(item => new
                    {
                        OriginalText = item.OriginalText,
                        NormalizedText = NormalizeForTraceMatch(item.OriginalText),
                        RejectionReason = item.RejectionReason,
                        SuggestedLevel = item.SuggestedLevel,
                        SuggestedPlacement = item.SuggestedPlacement
                    })
                    .Where(item => item.NormalizedText.Length >= 16)
                    .ToList();

                var usedClauses = new List<string>();
                var unusedClauses = new List<string>();

                foreach (var clause in clauses)
                {
                    var normalizedClause = NormalizeForTraceMatch(clause);
                    if (normalizedClause.Length < 16)
                    {
                        continue;
                    }

                    var isUsed = normalizedUsed.Any(u =>
                        u.Contains(normalizedClause, StringComparison.Ordinal) ||
                        normalizedClause.Contains(u, StringComparison.Ordinal));

                    if (isUsed)
                    {
                        usedClauses.Add(clause);
                    }
                    else
                    {
                        unusedClauses.Add(clause);
                    }
                }

                var report = new StringBuilder();
                report.AppendLine("DERIVATION TRACEABILITY REPORT");
                report.AppendLine("============================");
                report.AppendLine($"GeneratedUtc: {DateTime.UtcNow:O}");
                report.AppendLine($"ProjectId: {projectId}");
                report.AppendLine($"AttachmentId: {attachment.Id}");
                report.AppendLine($"FileName: {attachment.FileName}");
                report.AppendLine($"DerivedRequirementCount: {derivedRequirements.Count}");
                report.AppendLine($"CandidateClauseCount: {clauses.Count}");
                report.AppendLine($"UsedClauseCount: {usedClauses.Count}");
                report.AppendLine($"UnusedClauseCount: {unusedClauses.Count}");
                report.AppendLine();

                report.AppendLine("REQUIREMENT -> SOURCE MAP");
                report.AppendLine("------------------------");
                for (var i = 0; i < capabilities.Count; i++)
                {
                    var cap = capabilities[i];
                    var req = i < derivedRequirements.Count ? derivedRequirements[i] : null;
                    var reqId = req?.Item ?? req?.GlobalId ?? $"DERIVED-{i + 1:D3}";
                    var reportRequirementText = req?.Description ?? cap.RequirementText;
                    if (!string.IsNullOrWhiteSpace(reportRequirementText))
                    {
                        var newlineIndex = reportRequirementText.IndexOf('\n');
                        var leadClause = newlineIndex >= 0
                            ? reportRequirementText.Substring(0, newlineIndex).Trim()
                            : reportRequirementText.Trim();
                        if (LooksLikeUutRequirementForFallback(leadClause))
                        {
                            var suffix = newlineIndex >= 0 ? reportRequirementText.Substring(newlineIndex) : string.Empty;
                            reportRequirementText = RewriteUutRequirementAsTestSolutionVerificationForFallback(leadClause) + suffix;
                        }
                    }
                    var traceReference = BuildRequirementTraceReference(attachment.Id, reqId, i + 1);
                    report.AppendLine($"[{i + 1}] RequirementId: {reqId}");
                    report.AppendLine($"    TraceReference: {traceReference}");
                    report.AppendLine($"    RequirementText: {TruncateForReport(reportRequirementText, 220)}");
                    report.AppendLine($"    SourceAtpStep: {TruncateForReport(cap.SourceATPStep, 220)}");

                    if (cap.SourceMetadata != null &&
                        cap.SourceMetadata.TryGetValue("SourceRequirementText", out var sourceReqText) &&
                        !string.IsNullOrWhiteSpace(sourceReqText))
                    {
                        report.AppendLine($"    SourceRequirementText: {TruncateForReport(sourceReqText, 220)}");
                    }

                    report.AppendLine();
                }

                report.AppendLine("EXAMINED CLAUSES WITH DECISIONS");
                report.AppendLine("------------------------------");
                if (clauses.Count == 0)
                {
                    report.AppendLine("<none>");
                }
                else
                {
                    for (var i = 0; i < clauses.Count; i++)
                    {
                        var clause = clauses[i];
                        var normalizedClause = NormalizeForTraceMatch(clause);

                        var matchedCapability = capabilities.FirstOrDefault(cap =>
                        {
                            var sourceText = cap.SourceMetadata != null && cap.SourceMetadata.TryGetValue("SourceRequirementText", out var src)
                                ? src
                                : cap.SourceATPStep;

                            var normalizedSource = NormalizeForTraceMatch(sourceText);
                            return normalizedSource.Length >= 16 &&
                                   (normalizedSource.Contains(normalizedClause, StringComparison.Ordinal) ||
                                    normalizedClause.Contains(normalizedSource, StringComparison.Ordinal));
                        });

                        var matchedRejected = normalizedRejected.FirstOrDefault(item =>
                            item.NormalizedText.Contains(normalizedClause, StringComparison.Ordinal) ||
                            normalizedClause.Contains(item.NormalizedText, StringComparison.Ordinal));

                        string decision;
                        string criteria;

                        if (matchedCapability != null)
                        {
                            decision = "IS_REQUIREMENT";
                            var rationale = string.IsNullOrWhiteSpace(matchedCapability.DerivationRationale)
                                ? "Classified by taxonomy and converted to derived capability"
                                : matchedCapability.DerivationRationale;

                            criteria = $"Matched derived capability '{TruncateForReport(matchedCapability.RequirementText, 140)}'; " +
                                       $"taxonomy={matchedCapability.TaxonomySubcategory}, confidence={matchedCapability.ConfidenceScore:F2}, rationale={TruncateForReport(rationale, 180)}";
                        }
                        else if (matchedRejected != null)
                        {
                            decision = "NOT_REQUIREMENT";
                            var rejectionReason = string.IsNullOrWhiteSpace(matchedRejected.RejectionReason)
                                ? "Rejected during capability derivation"
                                : matchedRejected.RejectionReason;

                            var level = string.IsNullOrWhiteSpace(matchedRejected.SuggestedLevel)
                                ? "Unspecified"
                                : matchedRejected.SuggestedLevel;
                            var placement = string.IsNullOrWhiteSpace(matchedRejected.SuggestedPlacement)
                                ? "Unspecified"
                                : matchedRejected.SuggestedPlacement;

                            criteria = $"Rejected: {TruncateForReport(rejectionReason, 180)}; suggestedLevel={level}; suggestedPlacement={placement}";
                        }
                        else
                        {
                            decision = "UNDECIDED_REVIEW";
                            criteria = "Clause did not match a kept capability or an explicit rejected item in this run";
                        }

                        report.AppendLine($"[{i + 1}] Decision: {decision}");
                        report.AppendLine($"    Clause: {TruncateForReport(clause, 220)}");
                        report.AppendLine($"    Criteria: {criteria}");
                    }
                }

                report.AppendLine("USED SOURCE CLAUSES");
                report.AppendLine("-------------------");
                if (usedClauses.Count == 0)
                {
                    report.AppendLine("<none>");
                }
                else
                {
                    for (var i = 0; i < usedClauses.Count; i++)
                    {
                        report.AppendLine($"[{i + 1}] {usedClauses[i]}");
                    }
                }

                report.AppendLine();
                report.AppendLine("UNUSED SOURCE CLAUSES (REVIEW FOR MISSED REQUIREMENTS)");
                report.AppendLine("------------------------------------------------------");
                if (unusedClauses.Count == 0)
                {
                    report.AppendLine("<none>");
                }
                else
                {
                    for (var i = 0; i < unusedClauses.Count; i++)
                    {
                        report.AppendLine($"[{i + 1}] {unusedClauses[i]}");
                    }
                }

                var reportDirectory = Path.Combine(Environment.CurrentDirectory, "exports", "traceability-reports");
                Directory.CreateDirectory(reportDirectory);
                var safeFileName = SanitizeFileNameWithoutExtension(attachment.FileName);
                var reportPath = Path.Combine(
                    reportDirectory,
                    $"derivation-trace-{DateTime.UtcNow:yyyyMMdd-HHmmss}-att-{attachment.Id}-{safeFileName}.txt");

                await File.WriteAllTextAsync(reportPath, report.ToString(), cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Info($"[DerivationTrace] Wrote traceability report: {reportPath}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[DerivationTrace] Failed to write traceability report for attachment {attachment.Id}: {ex.Message}");
            }
        }

        private static List<string> ExtractSourceClauses(string documentContent)
        {
            var clauses = new List<string>();
            if (string.IsNullOrWhiteSpace(documentContent))
            {
                return clauses;
            }

            var normalized = System.Text.RegularExpressions.Regex.Replace(documentContent, @"\s+", " ").Trim();
            if (normalized.Length == 0)
            {
                return clauses;
            }

            var statementRegex = new System.Text.RegularExpressions.Regex(
                @"\b(?:[A-Za-z][A-Za-z0-9_\-/ ]{1,80}\s+)?(?:shall|must|will|should)\b[\s\S]{12,260}?(?:\.|;|(?=\bTest_type\s*:)|(?=\bTest_Venue\s*:)|(?=\bID\s*:)|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matches = statementRegex.Matches(normalized).Cast<System.Text.RegularExpressions.Match>();
            foreach (var match in matches)
            {
                var clause = System.Text.RegularExpressions.Regex.Replace(match.Value, @"\s+", " ").Trim();
                if (clause.Length < 20)
                {
                    continue;
                }

                if (!clause.EndsWith(".", StringComparison.Ordinal) && !clause.EndsWith(";", StringComparison.Ordinal))
                {
                    clause += ".";
                }

                if (seen.Add(clause))
                {
                    clauses.Add(clause);
                }
            }

            return clauses;
        }

        private static string NormalizeForTraceMatch(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var lower = value.ToLowerInvariant();
            lower = System.Text.RegularExpressions.Regex.Replace(lower, @"[^a-z0-9\s]", " ");
            lower = System.Text.RegularExpressions.Regex.Replace(lower, @"\s+", " ").Trim();
            return lower;
        }

        private static string TruncateForReport(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "<none>";
            }

            var text = value.Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        private static string SanitizeFileNameWithoutExtension(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(name))
            {
                return "attachment";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '-');
            }

            return name;
        }

        private static string BuildRequirementTraceReference(int attachmentId, string? requirementId, int ordinal)
        {
            var normalizedRequirementId = string.IsNullOrWhiteSpace(requirementId)
                ? $"REQ-{ordinal:D3}"
                : System.Text.RegularExpressions.Regex.Replace(requirementId.Trim(), @"\s+", "-");

            return $"TRC-ATT{attachmentId}-{normalizedRequirementId.ToUpperInvariant()}";
        }

        /// <summary>
        /// Convert derived capabilities to Requirement objects (adapted from SmartRequirementImporter)
        /// </summary>
        private List<Requirement> ConvertDerivedCapabilitiesToRequirements(List<DerivedCapability> capabilities, JamaAttachment attachment, int projectId)
        {
            var requirements = new List<Requirement>();
            var sourceFileName = attachment.FileName;

            for (int i = 0; i < capabilities.Count; i++)
            {
                var capability = capabilities[i];
                var structuredMetadata = TryExtractStructuredRequirementMetadata(capability.RequirementText);

                var normalizedDescription = !string.IsNullOrWhiteSpace(structuredMetadata.RequirementStatement)
                    ? structuredMetadata.RequirementStatement
                    : capability.RequirementText;

                var generatedItem = !string.IsNullOrWhiteSpace(structuredMetadata.RequirementId)
                    ? structuredMetadata.RequirementId
                    : $"DOC-{i + 1:D3}";

                // Remove echoed requirement ID prefix from generated description text,
                // e.g. "C4B_ATR-121 The MFD shall ...".
                if (!string.IsNullOrWhiteSpace(normalizedDescription) && !string.IsNullOrWhiteSpace(generatedItem))
                {
                    normalizedDescription = System.Text.RegularExpressions.Regex.Replace(
                        normalizedDescription,
                        $@"^\s*{System.Text.RegularExpressions.Regex.Escape(generatedItem)}\s*[:\-]?\s+",
                        string.Empty,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }

                if (LooksLikeUutRequirementForFallback(normalizedDescription))
                {
                    normalizedDescription = RewriteUutRequirementAsTestSolutionVerificationForFallback(normalizedDescription);
                }

                var traceReference = BuildRequirementTraceReference(attachment.Id, generatedItem, i + 1);

                var requirement = new Requirement
                {
                    GlobalId = !string.IsNullOrWhiteSpace(structuredMetadata.RequirementId)
                        ? structuredMetadata.RequirementId
                        : $"DOC-{attachment.Id}-{i + 1:D3}", // DOC-12345-001, DOC-12345-002, etc.
                    Item = generatedItem,
                    TraceReference = traceReference,
                    Name = GenerateRequirementNameFromCapability(normalizedDescription, capability.TaxonomyCategory),
                    Description = normalizedDescription,
                    RequirementType = $"{capability.TaxonomyCategory} - {capability.TaxonomySubcategory}",
                    
                    // Add derivation-specific fields
                    Rationale = BuildRequirementNotesFromCapability(capability, sourceFileName),
                    
                    // Standard fields
                    Heading = "Derived", 
                    ItemType = "System Requirement",
                    CreatedDate = DateTime.Now,
                    ModifiedDate = DateTime.Now,
                    SourceDocumentName = sourceFileName,
                    SourceAttachmentId = attachment.Id,
                    SourceJamaItemId = attachment.Item > 0 ? attachment.Item : null,
                    
                    // Add ATP derivation info to track this as an ATP-derived requirement
                    AtpDerivation = new AtpDerivationInfo
                    {
                        SourceDocumentName = sourceFileName,
                        SourceAtpStep = capability.SourceATPStep ?? "",
                        TaxonomyCategory = capability.TaxonomyCategory,
                        TaxonomySubcategory = capability.TaxonomySubcategory,
                        DerivationRationale = capability.DerivationRationale,
                        ConfidenceScore = capability.ConfidenceScore,
                        AllocationTargets = capability.AllocationTargets?.ToList() ?? new List<string>(),
                        MissingSpecifications = capability.MissingSpecifications?.ToList() ?? new List<string>(),
                        DerivedAt = DateTime.Now
                    }
                };

                var sourceClassification = capability.SourceMetadata != null &&
                                           capability.SourceMetadata.TryGetValue("SourceRequirementClassification", out var sourceClass)
                    ? sourceClass
                    : "Source/UUT Requirement";

                var derivedClassification = capability.SourceMetadata != null &&
                                            capability.SourceMetadata.TryGetValue("DerivedRequirementClassification", out var derivedClass)
                    ? derivedClass
                    : "Test Solution System Requirement";

                if (capability.SourceMetadata != null &&
                    capability.SourceMetadata.TryGetValue("SourceRequirementText", out var sourceRequirementText) &&
                    !string.IsNullOrWhiteSpace(sourceRequirementText))
                {
                    requirement.Rationale = string.Concat(
                        requirement.Rationale,
                        "\n\n**Source Requirement:** ",
                        sourceRequirementText.Trim());
                }

                requirement.Rationale = string.Concat(
                    requirement.Rationale,
                    "\n\n**Trace Reference:** ", traceReference,
                    "\n\n**Source Classification:** ", sourceClassification,
                    "\n\n**Derived Classification:** ", derivedClassification);

                requirement.RequirementType = derivedClassification;

                if (!string.IsNullOrWhiteSpace(structuredMetadata.TestType))
                {
                    requirement.VerificationMethodText = structuredMetadata.TestType;
                }

                var robustTags = new List<string> { "Derived" };
                robustTags.Add($"SourceClass:{sourceClassification.Replace(' ', '_')}");
                robustTags.Add($"DerivedClass:{derivedClassification.Replace(' ', '_')}");
                if (!string.IsNullOrWhiteSpace(capability.TaxonomyCategory))
                {
                    robustTags.Add($"Taxonomy:{capability.TaxonomyCategory}");
                }
                robustTags.Add($"TraceRef:{traceReference}");
                if (!string.IsNullOrWhiteSpace(structuredMetadata.TestType))
                {
                    robustTags.Add($"TestType:{structuredMetadata.TestType}");
                }
                if (!string.IsNullOrWhiteSpace(structuredMetadata.TestVenue))
                {
                    robustTags.Add($"TestVenue:{structuredMetadata.TestVenue}");
                }
                requirement.TagList = robustTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                if (structuredMetadata.HasStructuredMetadata)
                {
                    var metadataNotes = new List<string>();
                    if (!string.IsNullOrWhiteSpace(structuredMetadata.RequirementId))
                    {
                        metadataNotes.Add($"**Parsed ID:** {structuredMetadata.RequirementId}");
                    }
                    if (!string.IsNullOrWhiteSpace(structuredMetadata.TestType))
                    {
                        metadataNotes.Add($"**Parsed Test Type:** {structuredMetadata.TestType}");
                    }
                    if (!string.IsNullOrWhiteSpace(structuredMetadata.TestVenue))
                    {
                        metadataNotes.Add($"**Parsed Test Venue:** {structuredMetadata.TestVenue}");
                    }

                    if (metadataNotes.Count > 0)
                    {
                        requirement.Rationale = string.Concat(requirement.Rationale, "\n\n", string.Join("\n\n", metadataNotes));
                    }
                }

                requirements.Add(requirement);
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Converted {capabilities.Count} capabilities to {requirements.Count} requirements from {sourceFileName}");
            return requirements;
        }

        /// <summary>
        /// Generate a concise requirement name from capability text and taxonomy
        /// </summary>
        private string GenerateRequirementNameFromCapability(string requirementText, string taxonomyCategory)
        {
            // Take first 60 characters and clean up
            var name = requirementText.Length > 60 ? requirementText.Substring(0, 60) + "..." : requirementText;
            
            // Remove line breaks and extra spaces
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();
            
            // Prefix with taxonomy category for context
            return $"[{taxonomyCategory}] {name}";
        }

        /// <summary>
        /// Build comprehensive notes from derived capability metadata
        /// </summary>
        private string BuildRequirementNotesFromCapability(DerivedCapability capability, string sourceFileName)
        {
            var notes = new List<string>();
            
            // Add derivation info
            notes.Add($"**Derived from Document:** {sourceFileName}");
            notes.Add($"**Taxonomy Classification:** {capability.TaxonomyCategory} - {capability.TaxonomySubcategory}");
            notes.Add($"**Confidence Score:** {capability.ConfidenceScore:P1}");
            
            if (!string.IsNullOrEmpty(capability.DerivationRationale))
            {
                notes.Add($"**Derivation Rationale:** {capability.DerivationRationale}");
            }

            if (!string.IsNullOrEmpty(capability.SourceATPStep))
            {
                notes.Add($"**Source Content:** {capability.SourceATPStep}");
            }

            if (capability.AllocationTargets?.Count > 0)
            {
                notes.Add($"**Recommended Allocation:** {string.Join(", ", capability.AllocationTargets)}");
            }

            if (capability.MissingSpecifications?.Count > 0)
            {
                notes.Add($"**Missing Specifications:** {string.Join(", ", capability.MissingSpecifications)}");
            }

            notes.Add($"**Derived Using:** AI Capability Derivation System (5-Phase Implementation)");
            notes.Add($"**Quality Score:** Based on A-N taxonomy validation and content analysis");

            return string.Join("\n\n", notes);
        }

        private sealed class StructuredRequirementMetadata
        {
            public string? RequirementId { get; set; }
            public string? RequirementStatement { get; set; }
            public string? TestType { get; set; }
            public string? TestVenue { get; set; }

            public bool HasStructuredMetadata =>
                !string.IsNullOrWhiteSpace(RequirementId) ||
                !string.IsNullOrWhiteSpace(TestType) ||
                !string.IsNullOrWhiteSpace(TestVenue);
        }

        private StructuredRequirementMetadata TryExtractStructuredRequirementMetadata(string rawText)
        {
            var result = new StructuredRequirementMetadata();
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return result;
            }

            var normalizedText = rawText.Replace("\r", " ").Replace("\n", " ");
            normalizedText = System.Text.RegularExpressions.Regex.Replace(normalizedText, @"\s+", " ").Trim();

            var keyPattern = new System.Text.RegularExpressions.Regex(@"\b(ID|Test[_ ]?type|Test[_ ]?venue)\s*:\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var keyMatches = keyPattern.Matches(normalizedText);

            if (keyMatches.Count == 0)
            {
                return result;
            }

            var buckets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < keyMatches.Count; i++)
            {
                var match = keyMatches[i];
                var key = match.Groups[1].Value.Trim();
                var valueStart = match.Index + match.Length;
                var valueEnd = i + 1 < keyMatches.Count ? keyMatches[i + 1].Index : normalizedText.Length;
                if (valueEnd <= valueStart)
                {
                    continue;
                }

                var value = normalizedText.Substring(valueStart, valueEnd - valueStart).Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    buckets[key] = value;
                }
            }

            if (buckets.TryGetValue("ID", out var idValue) && !string.IsNullOrWhiteSpace(idValue))
            {
                var idMatch = System.Text.RegularExpressions.Regex.Match(idValue, @"^[A-Za-z0-9][A-Za-z0-9_.\-]*");
                if (idMatch.Success)
                {
                    result.RequirementId = idMatch.Value;
                    var remainder = idValue.Substring(idMatch.Length).Trim();
                    if (!string.IsNullOrWhiteSpace(remainder))
                    {
                        result.RequirementStatement = remainder;
                    }
                }
                else
                {
                    result.RequirementStatement = idValue;
                }
            }

            if (buckets.TryGetValue("Test_type", out var testTypeValue) ||
                buckets.TryGetValue("Test type", out testTypeValue))
            {
                result.TestType = testTypeValue?.Trim();
            }

            if (buckets.TryGetValue("Test_Venue", out var testVenueValue) ||
                buckets.TryGetValue("Test Venue", out testVenueValue))
            {
                result.TestVenue = testVenueValue?.Trim();
            }

            if (string.IsNullOrWhiteSpace(result.RequirementStatement))
            {
                var shallMatch = System.Text.RegularExpressions.Regex.Match(normalizedText, @"\b[A-Za-z][A-Za-z0-9_\-/ ]{1,60}\s+shall\s+[^.\r\n]{10,300}(?:\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (shallMatch.Success)
                {
                    result.RequirementStatement = shallMatch.Value.Trim();
                }
            }

            return result;
        }

        /// <summary>
        /// Provides periodic progress updates during ATP derivation processing
        /// </summary>
        private async Task ProvidePeriodicDerivationProgressAsync(Action<string>? progressCallback, CancellationToken cancellationToken)
        {
            if (progressCallback == null) return;

            var progressMessages = new[]
            {
                "🔍 Parsing PDF: Extracting ATP test procedure steps from document structure...",
                "📝 LLM Analysis: Using phi4-mini model to analyze test step semantics...", 
                "🎯 A-N Taxonomy: Classifying capabilities using Avionics-Navigation framework...",
                "⚡ Quality Scoring: Computing confidence metrics and derivation rationale...",
                "🔄 ATP Methodology: Cross-referencing with 5-phase capability derivation system...",
                "📊 Capability Ranking: Scoring system capabilities by quality and completeness...",
                "🧠 Relationship Analysis: Processing logical dependencies between test steps...",
                "✨ Requirement Synthesis: Converting capabilities to structured requirements..."
            };

            int messageIndex = 0;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(3000, cancellationToken); // Update every 3 seconds
                    if (cancellationToken.IsCancellationRequested) break;
                    progressCallback(progressMessages[messageIndex % progressMessages.Length]);
                    messageIndex++;
                    
                    // Add a processing indicator every few updates
                    if (messageIndex % 3 == 0)
                    {
                        progressCallback("⏳ ATP Derivation: Processing test steps through phi4-mini → A-N taxonomy → requirements...");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when derivation completes - no action needed
            }
        }

        /// <summary>
        /// Verify that required AI services (Ollama, embeddings, text generation) are available before processing
        /// </summary>
        private async Task<bool> VerifyAIServicesAvailableAsync(Action<string>? progressCallback, CancellationToken cancellationToken)
        {
            progressCallback?.Invoke("🔍 Checking AI service availability...");
            
            // Check if we're using DirectRAG (requires Ollama)
            if (_directRagService?.IsConfigured == true || _textGenerationService != null || _derivationService != null)
            {
                // Test Ollama connectivity (with auto-start)
                progressCallback?.Invoke("🔧 Verifying Ollama service...");
                
                if (!await IsOllamaAvailableAsync(progressCallback, cancellationToken))
                {
                    progressCallback?.Invoke("❌ Failed to start or connect to Ollama service");
                    return false;
                }
            }
            
            progressCallback?.Invoke("✅ AI services available - proceeding with document analysis");
            return true;
        }

        /// <summary>
        /// Test if Ollama is running and responding on localhost:11434
        /// Automatically attempts to start Ollama if it's not running
        /// </summary>
        private async Task<bool> IsOllamaAvailableAsync(Action<string>? progressCallback, CancellationToken cancellationToken)
        {
            // First, try to connect to Ollama
            if (await TestOllamaConnectionAsync(progressCallback, cancellationToken))
            {
                return true;
            }

            // If connection failed, try to start Ollama automatically
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Ollama not responding - attempting to start automatically");
            progressCallback?.Invoke("🚀 Starting Ollama service...");
            
            if (await StartOllamaServiceAsync(cancellationToken))
            {
                // Wait a moment for Ollama to fully start up
                await Task.Delay(3000, cancellationToken);
                
                // Test connection again
                if (await TestOllamaConnectionAsync(progressCallback, cancellationToken))
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Successfully started Ollama service");
                    return true;
                }
            }
            
            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Failed to start or connect to Ollama service");
            return false;
        }

        /// <summary>
        /// Test connection to Ollama API using intelligent status monitoring
        /// Only restarts if model is stuck or not responding
        /// </summary>
        private async Task<bool> TestOllamaConnectionAsync(Action<string>? progressCallback, CancellationToken cancellationToken)
        {
            try
            {
                if (_ollamaStatusMonitor != null)
                {
                    if (!_ollamaStatusMonitoringStarted)
                    {
                        _ollamaStatusMonitor.StartMonitoring();
                        _ollamaStatusMonitoringStarted = true;
                    }

                    await _ollamaStatusMonitor.CheckStatusNowAsync();
                }

                // Smart monitoring: check current status before deciding what to do
                var currentStatus = _ollamaStatusMonitor?.CurrentStatus ?? OllamaModelStatus.Unknown;
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Current Ollama status: {currentStatus}");

                // If model is already loaded, test if it's responsive
                if (currentStatus == OllamaModelStatus.Loaded)
                {
                    progressCallback?.Invoke("✅ Model already loaded - verifying (10s max)...");
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Model already loaded - quick responsiveness check (10s timeout)");
                    
                    var testSuccess = await PreWarmOllamaModelAsync(cancellationToken);
                    if (testSuccess)
                    {
                        progressCallback?.Invoke("✅ Model responsive and ready");
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Model already loaded and responsive - no restart needed");
                        return true;
                    }
                    
                    // Model stuck despite being loaded - restart needed
                    progressCallback?.Invoke("⚠️ Model loaded but not responding - restarting...");
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Model loaded but not responsive - restarting Ollama");
                }
                // If model not loaded, try to pre-warm (will load model)
                else if (currentStatus == OllamaModelStatus.NotLoaded)
                {
                    progressCallback?.Invoke("🔥 Initializing AI model (checking readiness)...");
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Model not loaded - attempting quick pre-warm (10s timeout)");
                    
                    var preWarmSuccess = await PreWarmOllamaModelAsync(cancellationToken);
                    if (preWarmSuccess)
                    {
                        progressCallback?.Invoke("✅ AI model ready");
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Model pre-warmed successfully");
                        return true;
                    }
                    
                    // Pre-warm failed - restart and try again
                    progressCallback?.Invoke("⚠️ Model not responding - restarting service (~5s)...");
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Pre-warm failed or timed out - restarting Ollama");
                }
                else
                {
                    var ollamaHealthy = _ollamaProcessManager != null && await _ollamaProcessManager.IsOllamaHealthyAsync(cancellationToken);
                    if (ollamaHealthy)
                    {
                        progressCallback?.Invoke(currentStatus == OllamaModelStatus.Loading
                            ? "🔥 Model is loading - waiting for readiness..."
                            : "🔥 Ollama reachable - initializing AI model...");
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Ollama status {currentStatus} but service is healthy - attempting pre-warm before restart");

                        var preWarmSuccess = await PreWarmOllamaModelAsync(cancellationToken);
                        if (preWarmSuccess)
                        {
                            progressCallback?.Invoke("✅ AI model ready");
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Healthy Ollama instance became ready without restart");
                            return true;
                        }

                        progressCallback?.Invoke("⚠️ Ollama reachable but model did not become ready - restarting service...");
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Healthy Ollama instance did not complete warmup from status {currentStatus} - restarting");
                    }
                    else
                    {
                        // Status is Unknown and HTTP health check failed - restart is justified.
                        progressCallback?.Invoke($"⚠️ Ollama status {currentStatus} and service is not healthy - restarting...");
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Ollama status {currentStatus} with failed health check - restarting for clean state");
                    }
                }

                // Restart Ollama and try pre-warming
                if (_ollamaProcessManager != null)
                {
                    await _ollamaProcessManager.RestartOllamaAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Ollama restarted successfully");
                    
                    progressCallback?.Invoke("� Service restarted - verifying readiness...");
                    var finalPreWarm = await PreWarmOllamaModelAsync(cancellationToken);
                    
                    if (!finalPreWarm)
                    {
                        progressCallback?.Invoke("⚠️ Model still not ready - will retry during extraction");
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Pre-warming after restart failed - extraction will use retry logic");
                    }
                    else
                    {
                        progressCallback?.Invoke("✅ AI model ready after restart");
                    }
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] IOllamaProcessManager not available - cannot restart");
                }
                
                return true; // Always return true - let extraction handle failures with retry logic
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Failed in Ollama connection test: {ex.Message}");
                return false; // Only return false if entire test process failed
            }
        }

        /// <summary>
        /// Pre-warm Ollama model by making a lightweight generation request
        /// This loads the model into memory. Should complete in ~45 seconds on clean Ollama instance.
        /// Returns true if successful, false if failed (extraction can still proceed with retry logic)
        /// </summary>
        private async Task<bool> PreWarmOllamaModelAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_textGenerationService == null) 
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Text generation service not available for pre-warming");
                    return false;
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Pre-warming Ollama model...");
                
                // Make a simple generation request to load the model
                // Use 10s timeout - if it takes longer, model is likely stuck (will restart)
                // Pre-warm is optional - extraction continues with retry logic if it fails
                var warmupPrompt = "Test"; 
                
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                
                var response = await _textGenerationService.GenerateAsync(warmupPrompt, linkedCts.Token);
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Model pre-warmed successfully - ready for extraction");
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Pre-warming timed out - log warning but don't throw (extraction will retry)
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] ⚠️ Model pre-warming timed out after 10 seconds - model likely stuck, will restart");
                return false;
            }
            catch (Exception ex)
            {
                // Pre-warming failed - log warning but don't throw (extraction will retry)
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Model pre-warming failed: {ex.Message} - extraction will proceed with retry logic");
                return false;
            }
        }

        /// <summary>
        /// Attempt to start Ollama service automatically
        /// </summary>
        private Task<bool> StartOllamaServiceAsync(CancellationToken cancellationToken)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Starting Ollama service...");
                
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ollama",
                    Arguments = "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                var process = System.Diagnostics.Process.Start(startInfo);
                if (process != null)
                {
                    // Don't wait for the process to exit - it should run in background
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Ollama process started (PID: {process.Id})");
                    return Task.FromResult(true);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Failed to start Ollama: {ex.Message}");
            }

            return Task.FromResult(false);
        }

        #region Template Form Architecture Integration (Phase 6)

        /// <summary>
        /// Extract requirements using Template Form Architecture with structured output envelopes
        /// Provides enhanced quality scoring, compliance checking, and telemetry tracking
        /// </summary>
        private async Task<List<Requirement>> ExtractRequirementsWithTemplateFormAsync(
            string documentContent,
            JamaAttachment attachment,
            int projectId,
            System.Action<string>? progressCallback,
            CancellationToken cancellationToken)
        {
            if (_envelopeService == null || _textGenerationService == null)
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[TemplateForm] Template Form services not available - falling back to standard extraction");
                return new List<Requirement>();
            }

            var extractionStartTime = DateTime.UtcNow;
            progressCallback?.Invoke("🎯 Using structured extraction with quality validation...");

            try
            {
                // Step 1: Define the requirement extraction schema
                var requirementSchema = new
                {
                    requirements = new[]
                    {
                        new
                        {
                            id = "string (format: REQ-XXX or SYS-XXX-NNN)",
                            text = "string (complete requirement statement)",
                            category = "string (Functional|Performance|Interface|Environmental|Safety|Security)",
                            page = "string (optional, page number or section reference)",
                            section = "string (optional, document section)",
                            confidence = "number (0.0-1.0, extraction confidence)"
                        }
                    },
                    metadata = new
                    {
                        total_requirements = "number",
                        document_name = "string",
                        extraction_method = "string"
                    }
                };

                var schemaJson = JsonSerializer.Serialize(requirementSchema, new JsonSerializerOptions { WriteIndented = true });

                // Step 2: Build prompt with envelope instructions
                var focusedContent = BuildRequirementFocusedExcerpt(documentContent, 12000);
                var prompt = $@"Extract all technical requirements from this document. Requirements typically use words like 'shall', 'must', 'will', or 'should' and define what a system must do or how it must perform.

DOCUMENT: {attachment.FileName}

CONTENT:
{focusedContent}

IMPORTANT: Respond ONLY with valid JSON matching this schema:
{schemaJson}

Example output:
{{
  ""requirements"": [
    {{
      ""id"": ""REQ-001"",
      ""text"": ""System shall process data at minimum 60 frames per second"",
      ""category"": ""Performance"",
      ""page"": ""Page 12"",
      ""section"": ""3.2 Performance Requirements"",
      ""confidence"": 0.95
    }}
  ],
  ""metadata"": {{
    ""total_requirements"": 1,
    ""document_name"": ""{attachment.FileName}"",
    ""extraction_method"": ""template_form""
  }}
}}

Extract requirements now (JSON only):";

                // Step 3: Generate LLM response with retry logic for model loading timeouts
                progressCallback?.Invoke("🧠 AI analyzing document with structured output...");
                string? llmResponse = null;
                int maxRetries = 2;
                int retryDelayMs = 5000; // 5 seconds between retries
                bool shouldRestartOllama = false;
                
                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        llmResponse = await _textGenerationService.GenerateAsync(prompt, cancellationToken);
                        break; // Success - exit retry loop
                    }
                    catch (HttpRequestException httpEx) when (httpEx.Message.Contains("500") && attempt < maxRetries)
                    {
                        // Check if this is the "timed out waiting for llama runner" error
                        var errorContent = httpEx.Message;
                        if (errorContent.Contains("llama runner") || errorContent.Contains("timed out"))
                        {
                            TestCaseEditorApp.Services.Logging.Log.Warn($"[TemplateForm] Model loading timeout on attempt {attempt + 1}/{maxRetries + 1} - retrying in {retryDelayMs}ms...");
                            progressCallback?.Invoke($"⏳ Model loading delay - retrying ({attempt + 1}/{maxRetries + 1})...");
                            await Task.Delay(retryDelayMs, cancellationToken);
                            retryDelayMs *= 2; // Exponential backoff
                            
                            // If this was the last retry, mark for Ollama restart
                            if (attempt == maxRetries - 1)
                            {
                                shouldRestartOllama = true;
                            }
                            continue;
                        }
                        throw; // Different error - don't retry
                    }
                }

                // If all retries failed with model loading timeout, try restarting Ollama
                if (string.IsNullOrWhiteSpace(llmResponse) && shouldRestartOllama && _ollamaProcessManager != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[TemplateForm] All retries failed with model timeout - restarting Ollama...");
                    progressCallback?.Invoke("🔄 Restarting Ollama service to recover from stuck state...");
                    
                    try
                    {
                        await _ollamaProcessManager.RestartOllamaAsync(cancellationToken);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[TemplateForm] ✅ Ollama restarted - attempting final generation...");
                        progressCallback?.Invoke("✅ Ollama restarted - making final attempt...");
                        
                        // Final attempt after restart
                        llmResponse = await _textGenerationService.GenerateAsync(prompt, cancellationToken);
                    }
                    catch (Exception restartEx)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error($"[TemplateForm] Failed to restart Ollama: {restartEx.Message}");
                        progressCallback?.Invoke($"❌ Failed to restart Ollama: {restartEx.Message}");
                    }
                }

                var safeLlmResponse = llmResponse ?? string.Empty;

                // Step 4: Try to parse the JSON response directly (simplified approach)
                RequirementExtractionEnvelope? envelope = null;
                try
                {
                    // Extract JSON from response (handle markdown code blocks)
                    var jsonStart = safeLlmResponse.IndexOf('{');
                    var jsonEnd = safeLlmResponse.LastIndexOf('}');
                    if (jsonStart >= 0 && jsonEnd > jsonStart)
                    {
                        var jsonContent = safeLlmResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                        envelope = JsonSerializer.Deserialize<RequirementExtractionEnvelope>(jsonContent, new JsonSerializerOptions 
                        { 
                            PropertyNameCaseInsensitive = true 
                        });
                    }
                }
                catch (Exception parseEx)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[TemplateForm] JSON parsing failed: {parseEx.Message}");
                }

                if (envelope == null || envelope.Requirements == null || envelope.Requirements.Count == 0)
                {
                    envelope = TryRecoverEnvelopeFromLooseJson(safeLlmResponse, attachment.FileName);
                }

                if (envelope == null || envelope.Requirements == null || envelope.Requirements.Count == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error($"[TemplateForm] Failed to parse structured output - NO FALLBACK (legacy parsing disabled)");
                    progressCallback?.Invoke("❌ Structured extraction failed - Template Form Architecture required");
                    
                    // Track failure in telemetry if available
                    if (_telemetryService != null)
                    {
                        await _telemetryService.TrackLLMResponseAsync(new LLMResponseTelemetry
                        {
                            Timestamp = DateTime.UtcNow,
                            OperationName = "template_form_extraction",
                            ResponseLength = safeLlmResponse.Length,
                            PassedValidation = false,
                            ResponseTimeMs = (long)(DateTime.UtcNow - extractionStartTime).TotalMilliseconds,
                            TemplateId = "requirement_extraction",
                            WasCorrect = false
                        });
                    }
                    
                    // NO FALLBACK - return empty list to force Template Form Architecture usage
                    return new List<Requirement>();
                }

                // Step 5: Convert envelope data to Requirement objects
                var extractedRequirements = new List<Requirement>();

                foreach (var req in envelope.Requirements)
                {
                    if (string.IsNullOrWhiteSpace(req.Text) || !IsValidRequirement(req.Text))
                        continue;

                    // Build source information
                    var sourceInfo = new List<string>();
                    if (!string.IsNullOrWhiteSpace(req.Page))
                        sourceInfo.Add(req.Page);
                    if (!string.IsNullOrWhiteSpace(req.Section))
                        sourceInfo.Add(req.Section);
                    
                    var sourceLine = sourceInfo.Count > 0 ? string.Join(", ", sourceInfo) : "Source not specified";

                    var requirement = new Requirement
                    {
                        GlobalId = req.Id ?? $"SYS-REQ-{extractedRequirements.Count + 1:D3}",
                        Item = req.Id ?? $"SYS-REQ-{extractedRequirements.Count + 1:D3}",
                        Name = req.Category ?? "System Requirement",
                        Description = $"{req.Text}\n\nSource: {sourceLine}\nFrom: {attachment.FileName}\nConfidence: {req.Confidence:P0} (Template Form extraction)",
                        TraceReference = BuildRequirementTraceReference(attachment.Id, req.Id, extractedRequirements.Count + 1),
                        SourceDocumentName = attachment.FileName,
                        SourceAttachmentId = attachment.Id,
                        SourceJamaItemId = attachment.Item > 0 ? attachment.Item : null
                    };

                    // Step 6: Record field processing for quality metrics (if service available)
                    if (_qualityService != null)
                    {
                        await _qualityService.RecordFieldProcessingResultAsync(new FieldProcessingResult
                        {
                            TemplateId = "requirement_extraction",
                            FieldType = FieldCriticality.Required,
                            ProcessedAt = DateTime.UtcNow,
                            ProcessingTime = TimeSpan.FromMilliseconds(100), // Rough estimate per field
                            IsSuccessful = true,
                            ConfidenceScore = req.Confidence,
                            RetryCount = 0
                        });
                    }

                    extractedRequirements.Add(requirement);
                }

                // Step 7: Track successful extraction in telemetry
                if (_telemetryService != null)
                {
                    var processingTime = (long)(DateTime.UtcNow - extractionStartTime).TotalMilliseconds;
                    
                    await _telemetryService.TrackLLMResponseAsync(new LLMResponseTelemetry
                    {
                        Timestamp = DateTime.UtcNow,
                        OperationName = "template_form_extraction",
                        ResponseLength = safeLlmResponse.Length,
                        ParsedFieldCount = extractedRequirements.Count,
                        ExpectedFieldCount = envelope.Metadata.TotalRequirements,
                        PassedValidation = true,
                        ResponseTimeMs = processingTime,
                        TemplateId = "requirement_extraction",
                        WasCorrect = true,
                        LLMConfidence = envelope.Requirements.Average(r => r.Confidence),
                        ActualQualityScore = envelope.Requirements.Average(r => r.Confidence)
                    });

                    // Track field completions
                    foreach (var req in extractedRequirements)
                    {
                        await _telemetryService.TrackFieldCompletionAsync(new FieldCompletionEvent
                        {
                            Timestamp = DateTime.UtcNow,
                            FieldName = "requirement",
                            TemplateId = "requirement_extraction",
                            CompletionStatus = FieldCompletionStatus.Complete,
                            QualityScore = 0.9,
                            Confidence = 0.9,
                            WasRequired = true,
                            OperationName = "document_extraction"
                        });
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[TemplateForm] Successfully extracted {extractedRequirements.Count} requirements using Template Form Architecture");
                progressCallback?.Invoke($"✅ Extracted {extractedRequirements.Count} validated requirements");

                return extractedRequirements;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[TemplateForm] Error in template form extraction");
                progressCallback?.Invoke($"❌ Template form extraction error: {ex.Message}");
                return new List<Requirement>();
            }
        }

        /// <summary>
        /// Data model for requirement extraction envelope (matches JSON schema)
        /// </summary>
        private class RequirementExtractionEnvelope
        {
            public List<ExtractedRequirement> Requirements { get; set; } = new();
            public ExtractionMetadata Metadata { get; set; } = new();
        }

        private class ExtractedRequirement
        {
            public string? Id { get; set; }
            public string Text { get; set; } = "";
            public string? Category { get; set; }
            public string? Page { get; set; }
            public string? Section { get; set; }
            public double Confidence { get; set; } = 0.8;
        }

        private class ExtractionMetadata
        {
            public int TotalRequirements { get; set; }
            public string DocumentName { get; set; } = "";
            public string ExtractionMethod { get; set; } = "template_form";
        }

        #endregion
    }
}
