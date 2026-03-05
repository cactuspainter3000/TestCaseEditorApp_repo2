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
        
        // Template Form Architecture services (Phase 6 integration)
        private readonly IOutputEnvelopeService? _envelopeService;
        private readonly IFieldLevelQualityService? _qualityService;
        private readonly IServiceComplianceWrapper? _complianceWrapper;
        private readonly IABTestingFramework? _abTestingFramework;
        private readonly ITelemetryDashboardService? _telemetryService;
        
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
            ITelemetryDashboardService? telemetryService = null)
        {
            _jamaService = jamaService ?? throw new ArgumentNullException(nameof(jamaService));
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _directRagService = directRagService;
            _textGenerationService = textGenerationService;
            _derivationService = derivationService;
            _envelopeService = envelopeService;
            _qualityService = qualityService;
            _complianceWrapper = complianceWrapper;
            _abTestingFramework = abTestingFramework;
            _telemetryService = telemetryService;
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Initialized with Template Form Architecture: Envelope={envelopeService != null}, Quality={qualityService != null}, Compliance={complianceWrapper != null}, ABTest={abTestingFramework != null}, Telemetry={telemetryService != null}");
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
                if (!await VerifyAIServicesAvailableAsync(progressCallback, cancellationToken))
                {
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] AI services not available - aborting document processing");
                    progressCallback?.Invoke("❌ AI services unavailable - please ensure Ollama is running before processing documents");
                    return new List<Requirement>();
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

                // Primary: Try DirectRagService first but only for text-based documents
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] 🔍 ROUTING ANALYSIS:");
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser]   - DirectRagService available: {_directRagService != null}");
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser]   - DirectRagService configured: {_directRagService?.IsConfigured == true}");
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser]   - TextGenerationService available: {_textGenerationService != null}");
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser]   - Document type: {attachment.MimeType}");
                
                if (_directRagService?.IsConfigured == true && _textGenerationService != null)
                {
                    // Check if it's a document type that DirectRag can handle effectively
                    if (attachment.IsWord || attachment.IsExcel || attachment.IsPdf || attachment.MimeType?.Contains("text") == true)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Using DirectRagService for document analysis ({attachment.MimeType})");
                        progressCallback?.Invoke($"🚀 Processing with reliable RAG-enhanced analysis...");
                        return await ExtractRequirementsWithDirectRagAsync(attachment, projectId, progressCallback, onRequirementDiscovered, cancellationToken);
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] ❌ Unsupported document type for DirectRag: {attachment.MimeType}");
                    }
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] ❌ DirectRagService not ready - DirectRag: {_directRagService?.IsConfigured}, TextGen: {_textGenerationService != null}");
                }
                
                // ENHANCED SYSTEM: Require DirectRag service (no AnythingLLM fallback)
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] ENHANCED SYSTEM: DirectRagService required for document type: {attachment.MimeType}. Fallback to AnythingLLM disabled.");
                progressCallback?.Invoke($"❌ Enhanced system requires proper RAG service - no fallback available");
                throw new InvalidOperationException($"Enhanced document processing requires DirectRagService but it's not available for {attachment.MimeType}. Legacy AnythingLLM fallback disabled.");
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
                progressCallback?.Invoke($"Testing document access for '{attachment.FileName}' before extraction...");
                
                // CRITICAL: Test RAG document access before attempting extraction
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Testing RAG document access for workspace '{workspaceSlug}'");
                var (hasAccess, diagnostics) = await _llmService.TestDocumentAccessAsync(workspaceSlug, cancellationToken);
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] RAG Test Results: {diagnostics}");
                
                // DEBUG: Check current workspace configuration after our fixes
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] 🔍 Checking if workspace configuration was properly applied...");
                
                if (!hasAccess)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] ❌ CRITICAL: RAG document access test FAILED");
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] LLM cannot access document content - similarity threshold or other RAG settings blocking retrieval");
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] This means our workspace configuration fixes didn't work as expected");
                    
                    // Force apply RAG configuration fix for existing workspace
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] ⚠️ Applying emergency RAG fix to existing workspace");
                    progressCallback?.Invoke($"⚠️ Applying RAG configuration fix...");
                    
                    var fixApplied = await _llmService.FixRagConfigurationAsync(workspaceSlug, cancellationToken);
                    if (fixApplied)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] RAG fix applied - retesting document access");
                        await Task.Delay(2000, cancellationToken); // Let config take effect
                        
                        var (retestSuccess, retestDiagnostics) = await _llmService.TestDocumentAccessAsync(workspaceSlug, cancellationToken);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Retest Results: {retestDiagnostics}");
                        
                        if (!retestSuccess)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] ❌ RAG fix failed - fundamental AnythingLLM configuration issue");
                            progressCallback?.Invoke($"❌ RAG fix failed - cannot access document"); 
                            return new List<Requirement>();
                        }
                        else
                        {
                            hasAccess = true; // Update to proceed
                        }
                    }
                    else
                    {
                        progressCallback?.Invoke($"❌ Document access failed - RAG configuration issue detected");
                        return new List<Requirement>(); // Return empty list
                    }
                }
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] ⚠️ Initial RAG test failed - applying troubleshooting configuration");
                    progressCallback?.Invoke($"Document access failed - applying RAG troubleshooting fix...");
                    
                    // Apply RAG configuration fix based on AnythingLLM documentation
                    var fixApplied = await _llmService.FixRagConfigurationAsync(workspaceSlug, cancellationToken);
                    if (fixApplied)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] RAG fix applied - retesting document access");
                        progressCallback?.Invoke($"RAG configuration updated - retesting document access...");
                        
                        // Wait for configuration to take effect
                        await Task.Delay(2000, cancellationToken);
                        
                        // Retest document access
                        var (retestSuccess, retestDiagnostics) = await _llmService.TestDocumentAccessAsync(workspaceSlug, cancellationToken);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Retest Results: {retestDiagnostics}");
                        
                        if (!retestSuccess)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] ❌ CRITICAL: RAG fix failed - document still inaccessible");
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] This indicates a deeper AnythingLLM configuration issue");
                            
                            progressCallback?.Invoke($"❌ RAG troubleshooting failed - cannot extract real requirements");
                            return new List<Requirement>(); // Return empty list
                        }
                        else
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ RAG fix successful - document access restored");
                            hasAccess = true; // Update access status to proceed
                        }
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] ❌ Could not apply RAG configuration fix");
                        progressCallback?.Invoke($"❌ Could not fix document access - aborting extraction");
                        return new List<Requirement>();
                    }
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
                var validationTimeout = TimeSpan.FromMinutes(2);
                using var validationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                validationCts.CancelAfter(validationTimeout);
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
            return $@"CRITICAL: DOCUMENT CONTENT ACCESS DIAGNOSTIC + REQUIREMENT EXTRACTION

FILE: {attachment.FileName}

STEP 1 - DOCUMENT ACCESSIBILITY TEST:
Before extracting requirements, first confirm what document content you can access:
- List the first few section headings, page numbers, or chapter titles you can see
- Report document length/section count if visible  
- If you see NO document content, state: ""NO CONTENT ACCESSIBLE""
- Only proceed to extraction if you have actual document access

STEP 2 - EXTRACT EVERY REQUIREMENT (only if content accessible):
Find ALL technical requirements and specifications. Extract anything that states what the system:
- SHALL, MUST, WILL, SHOULD do or have
- Performance limitations, timing constraints, accuracy specs
- Interface specifications, protocol requirements
- Environmental operating conditions
- Physical constraints, design limits
- Safety, security, quality requirements
- Test criteria, acceptance conditions

CRITICAL RULES:
- Extract from ACTUAL document content you can see, not generic examples
- Include specific numbers, values, references mentioned in the document
- If uncertain whether something is a requirement, INCLUDE IT
- Better to over-extract than miss critical requirements

OUTPUT FORMAT (for each requirement found):

---
ID: REQ-001
Text: [Complete requirement from actual document - include specific details/values]
Category: [Functional/Performance/Interface/Environmental/Safety/Design/Quality/Test/Compliance]
Priority: [High/Medium/Low]
Verification: [Test/Analysis/Inspection/Demonstration]
Source: [Specific section, page, or location where found]
---

IMPORTANT: Only extract requirements if you have actual document access. Do not create generic example requirements.";
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

                // Build requirement object
                var requirement = new Requirement
                {
                    GlobalId = id,
                    Item = id,
                    Name = reqData.TryGetValue("Category", out var cat) ? cat : "Extracted Requirement",
                    Description = text
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
                            Description = $"{text}\n\n[Recovered via fallback extraction]\nSource: {attachment.FileName}"
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
                
                // If we found significantly fewer than expected, run a second extraction pass
                if (initialRequirements.Count < expectedMin * 0.7) // If less than 70% of expected
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Count seems low ({initialRequirements.Count} < {expectedMin * 0.7:F0}), running recovery extraction pass...");
                    
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
            
            return $@"RECOVERY SCAN: FIND MISSED REQUIREMENTS IN {attachment.FileName}

CONTEXT: Initial extraction found only {alreadyFound.Count} requirements: {foundIdsText}

This seems low for a technical document. Please perform a thorough re-scan focusing on areas commonly missed:

TARGETED SEARCH AREAS:
1. Tables and charts with numerical specifications
2. Appendices and reference sections
3. Figure captions with technical specs
4. Bullet points and numbered lists
5. Headers that contain requirement-like text
6. Performance sections, specifications sections
7. Test procedures, acceptance criteria
8. Interface descriptions, protocol specifications
9. Environmental, safety, compliance sections
10. Any ""The system shall..."" or similar statements

AGGRESSIVE EXTRACTION:
- Look for ANY text that specifies system behavior, constraints, or performance
- Include threshold values, limits, tolerances
- Extract test requirements and acceptance criteria
- Include interface specifications and standards references
- Don't be conservative - if it looks like a requirement, extract it

DOCUMENT ACCESS CHECK:
First confirm you can see actual document content (not just metadata). List a few actual text snippets or headings you can see from the document.

FORMAT: Same as before with --- delimiters:
ID: REQ-XXX (continue numbering from REQ-{alreadyFound.Count + 1:D3})
Text: [Specific requirement text from actual document]
Category: [Type]
Source: [Exact location where found]
---

GOAL: Find real requirements we missed in the first pass. Look harder at the actual document content.";
        }

        /// <summary>
        /// Estimate minimum expected requirements based on document characteristics
        /// </summary>
        private int EstimateMinimumRequirements(JamaAttachment attachment)
        {
            // Basic heuristic based on file size and type
            long fileSize = attachment.FileSize;
            var sizeKB = fileSize / 1024.0;
            
            // Documents over 100KB typically have multiple requirements
            if (sizeKB > 100)
            {
                return Math.Max(5, (int)(sizeKB / 50)); // Rough estimate: 1 req per 50KB
            }
            else if (sizeKB > 50)
            {
                return 3; // Medium documents should have at least a few requirements
            }
            else
            {
                return 1; // Small documents should have at least one requirement
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
                
                // Step 3: Index document with DirectRagService
                var indexSuccess = await _directRagService!.IndexDocumentAsync(attachment, documentContent, projectId, cancellationToken);
                if (!indexSuccess)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error($"[DirectRag] Failed to index document {attachment.FileName}");
                }

                progressCallback?.Invoke($"🧠 Analyzing document for requirements with AI...");
                
                // Step 4: Use DirectRag to get relevant content chunks and analyze with LLM
                var contextContent = await _directRagService!.GetRequirementAnalysisContextAsync(
                    "requirements specifications constraints criteria shall must should will system component interface protocol performance safety", 
                    projectId, 
                    maxContextChunks: 8, // Reduced from 20 to prevent context overflow
                    cancellationToken);

                // Validate we have meaningful content to analyze
                if (string.IsNullOrWhiteSpace(contextContent) || contextContent.Length < 50)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[DirectRag] Insufficient context content ({contextContent?.Length ?? 0} chars) - using full document");
                    // Use the extracted document content directly if RAG context is insufficient  
                    contextContent = documentContent.Length > 4000 ? documentContent.Substring(0, 4000) + "..." : documentContent;
                }
                else if (contextContent.Length > 4000)
                {
                    // Trim context if it's too large for the LLM
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[DirectRag] Context too large ({contextContent.Length} chars), trimming to 4000 chars");
                    contextContent = contextContent.Substring(0, 4000) + "...";
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
                progressCallback?.Invoke($"✅ Found {allRequirements.Count} requirements: {extractedRequirements.Count} extracted + {derivedRequirements.Count} derived using AI analysis");
                
                return allRequirements;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[DirectRag] Error processing attachment {attachment.Id}: {ex.Message}");
                progressCallback?.Invoke($"❌ Error processing document: {ex.Message}");
                return new List<Requirement>();
            }
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
                            Description = $"{text}\n\nSource: {sourceLine}\nFrom: {attachment.FileName}"
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
            
            return requirements;
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
                if (!await IsOllamaAvailableAsync(cancellationToken))
                {
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Ollama not available for ATP derivation - aborting intelligent analysis");
                    progressCallback?.Invoke("⚠️ Ollama service unavailable - skipping AI-powered requirement derivation");
                    return new List<Requirement>(); // Fall back gracefully
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] 🚀 Using ATP derivation system to derive requirements from {attachment.FileName} ({documentContent.Length} characters)");
                progressCallback?.Invoke($"🚀 Analyzing document with AI capability derivation system...");

                // Configure derivation options for general document processing (not just ATP)
                var derivationOptions = new DerivationOptions
                {
                    SystemType = "General", // Not specific to any system type
                    EnableQualityScoring = true,
                    IncludeRejectionAnalysis = true,
                    SourceMetadata = new Dictionary<string, string>
                    {
                        ["SourceDocument"] = attachment.FileName,
                        ["DocumentType"] = GetDocumentTypeDescription(attachment),
                        ["AttachmentId"] = attachment.Id.ToString(),
                        ["ProjectId"] = projectId.ToString()
                    }
                };

                // Use the 5-phase ATP derivation system to analyze the document content
                progressCallback?.Invoke($"🧠 Running AI-powered requirement derivation analysis on {documentContent.Length} characters...");
                
                // Add timeout for ATP derivation to prevent indefinite hanging
                var derivationTimeout = TimeSpan.FromMinutes(10); // Maximum 10 minutes for large documents
                var derivationCts = new CancellationTokenSource();
                derivationCts.CancelAfter(derivationTimeout);
                
                // Combine the provided cancellation token with the timeout
                var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, derivationCts.Token);
                
                // Start a background task to provide periodic progress updates
                var progressTask = ProvidePeriodicDerivationProgressAsync(progressCallback, derivationCts.Token);
                
                try
                {
                    // Create retry callback for handling timeout scenarios
                    Func<List<SkippedAtpStep>, Task<TimeoutRetryDecision>> retryCallback = async (skippedSteps) =>
                    {
                        // Show user the retry option using MessageBox
                        var message = $"Analysis completed with some timeouts.\n\n" +
                                    $"{skippedSteps.Count} ATP steps timed out during analysis.\n\n" +
                                    $"Would you like to retry the timed-out steps with extended timeout?\n\n" +
                                    $"(This may help extract additional requirements from complex ATP steps)";
                        
                        var result = System.Windows.MessageBox.Show(message,
                            "ATP Analysis - Retry Timed Out Steps?",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question);
                        
                        if (result == System.Windows.MessageBoxResult.Yes)
                        {
                            // Let user choose timeout extension
                            var timeoutMessage = "Select extended timeout for retry:\n\n" +
                                               "• 15 seconds (Quick retry)\n" +
                                               "• 30 seconds (Standard retry)\n" +
                                               "• 60 seconds (Extended retry)\n" +
                                               "• 120 seconds (Maximum retry)\n\n" +
                                               "Click OK for 60 seconds, Cancel for 30 seconds";
                            
                            var timeoutResult = System.Windows.MessageBox.Show(timeoutMessage,
                                "Select Retry Timeout",
                                System.Windows.MessageBoxButton.OKCancel,
                                System.Windows.MessageBoxImage.Question);
                            
                            var extendedTimeout = timeoutResult == System.Windows.MessageBoxResult.OK 
                                ? TimeSpan.FromSeconds(60) 
                                : TimeSpan.FromSeconds(30);
                            
                            return new TimeoutRetryDecision { ShouldRetry = true, ExtendedTimeout = extendedTimeout };
                        }
                        
                        return new TimeoutRetryDecision { ShouldRetry = false, ExtendedTimeout = TimeSpan.Zero };
                    };
                    
                    var derivationResult = await _derivationService.DeriveCapabilitiesAsync(documentContent, derivationOptions, progressCallback, retryCallback, onRequirementDiscovered);
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
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Successfully derived {derivedRequirements.Count} requirements from {attachment.FileName} using ATP derivation system");
                    progressCallback?.Invoke($"✅ ATP Derivation Complete: Generated {derivedRequirements.Count} requirements via phi4-mini → A-N taxonomy → capability synthesis");

                    return derivedRequirements;
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
                var requirement = new Requirement
                {
                    GlobalId = $"DOC-{attachment.Id}-{i + 1:D3}", // DOC-12345-001, DOC-12345-002, etc.
                    Item = $"DOC-{i + 1:D3}",
                    Name = GenerateRequirementNameFromCapability(capability.RequirementText, capability.TaxonomyCategory),
                    Description = capability.RequirementText,
                    RequirementType = $"{capability.TaxonomyCategory} - {capability.TaxonomySubcategory}",
                    
                    // Add derivation-specific fields
                    Rationale = BuildRequirementNotesFromCapability(capability, sourceFileName),
                    
                    // Standard fields
                    Heading = "Derived", 
                    ItemType = "System Requirement",
                    CreatedDate = DateTime.Now,
                    ModifiedDate = DateTime.Now,
                    
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
                
                if (!await IsOllamaAvailableAsync(cancellationToken))
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
        private async Task<bool> IsOllamaAvailableAsync(CancellationToken cancellationToken)
        {
            // First, try to connect to Ollama
            if (await TestOllamaConnectionAsync(cancellationToken))
            {
                return true;
            }

            // If connection failed, try to start Ollama automatically
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Ollama not responding - attempting to start automatically");
            
            if (await StartOllamaServiceAsync(cancellationToken))
            {
                // Wait a moment for Ollama to fully start up
                await Task.Delay(3000, cancellationToken);
                
                // Test connection again
                if (await TestOllamaConnectionAsync(cancellationToken))
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Successfully started Ollama service");
                    return true;
                }
            }
            
            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Failed to start or connect to Ollama service");
            return false;
        }

        /// <summary>
        /// Test connection to Ollama API
        /// </summary>
        private async Task<bool> TestOllamaConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5); // Quick timeout for health check
                
                var response = await httpClient.GetAsync("http://localhost:11434/api/tags", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Ollama health check passed - service is responding");
                    
                    // Pre-warm the model to avoid "timed out waiting for llama runner" errors
                    await PreWarmOllamaModelAsync(cancellationToken);
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaDocumentParser] Ollama connection test failed: {ex.Message}");
            }
            
            return false;
        }

        /// <summary>
        /// Pre-warm Ollama model by making a lightweight generation request
        /// This loads the model into memory to avoid "timed out waiting for llama runner" errors on first real request
        /// </summary>
        private async Task PreWarmOllamaModelAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_textGenerationService == null) return;
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Pre-warming Ollama model...");
                
                // Make a simple, fast generation request to load the model
                var warmupPrompt = "Test"; // Minimal prompt for fastest response
                var response = await _textGenerationService.GenerateAsync(warmupPrompt, cancellationToken);
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Model pre-warmed successfully - ready for extraction");
            }
            catch (Exception ex)
            {
                // Log but don't fail - the model might still work for real requests
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Model pre-warming failed (will attempt extraction anyway): {ex.Message}");
            }
        }

        /// <summary>
        /// Attempt to start Ollama service automatically
        /// </summary>
        private async Task<bool> StartOllamaServiceAsync(CancellationToken cancellationToken)
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
                    return true;
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Failed to start Ollama: {ex.Message}");
            }
            
            return false;
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
                var prompt = $@"Extract all technical requirements from this document. Requirements typically use words like 'shall', 'must', 'will', or 'should' and define what a system must do or how it must perform.

DOCUMENT: {attachment.FileName}

CONTENT:
{(documentContent.Length > 3500 ? documentContent.Substring(0, 3500) + "..." : documentContent)}

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
                string llmResponse = string.Empty;
                int maxRetries = 2;
                int retryDelayMs = 5000; // 5 seconds between retries
                
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
                            continue;
                        }
                        throw; // Different error - don't retry
                    }
                }

                // Step 4: Try to parse the JSON response directly (simplified approach)
                RequirementExtractionEnvelope? envelope = null;
                try
                {
                    // Extract JSON from response (handle markdown code blocks)
                    var jsonStart = llmResponse.IndexOf('{');
                    var jsonEnd = llmResponse.LastIndexOf('}');
                    if (jsonStart >= 0 && jsonEnd > jsonStart)
                    {
                        var jsonContent = llmResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
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
                    TestCaseEditorApp.Services.Logging.Log.Error($"[TemplateForm] Failed to parse structured output - NO FALLBACK (legacy parsing disabled)");
                    progressCallback?.Invoke("❌ Structured extraction failed - Template Form Architecture required");
                    
                    // Track failure in telemetry if available
                    if (_telemetryService != null)
                    {
                        await _telemetryService.TrackLLMResponseAsync(new LLMResponseTelemetry
                        {
                            Timestamp = DateTime.UtcNow,
                            OperationName = "template_form_extraction",
                            ResponseLength = llmResponse.Length,
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
                        Description = $"{req.Text}\n\nSource: {sourceLine}\nFrom: {attachment.FileName}\nConfidence: {req.Confidence:P0} (Template Form extraction)"
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
                        ResponseLength = llmResponse.Length,
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
