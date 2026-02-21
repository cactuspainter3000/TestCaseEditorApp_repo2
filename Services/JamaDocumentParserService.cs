using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

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
        private const string PARSING_WORKSPACE_PREFIX = "jama-doc-parse";

        public bool IsConfigured => _jamaService.IsConfigured && _llmService != null;

        public JamaDocumentParserService(IJamaConnectService jamaService, IAnythingLLMService llmService)
        {
            _jamaService = jamaService ?? throw new ArgumentNullException(nameof(jamaService));
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
        }

        /// <summary>
        /// Parse a single Jama attachment and extract requirements using LLM
        /// </summary>
        public async Task<List<Requirement>> ParseAttachmentAsync(JamaAttachment attachment, int projectId, System.Action<string>? progressCallback = null, CancellationToken cancellationToken = default)
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
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Starting parse for attachment {attachment.Id} ({attachment.FileName})");
                progressCallback?.Invoke($"🔧 Preparing to extract requirements from {attachment.FileName}...");

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
                progressCallback?.Invoke($"✅ Downloaded {fileBytes.Length / 1024}KB - Processing with LLM...");

                // Step 2: Use provided attachment metadata (no need to re-scan project)
                if (!attachment.IsSupportedDocument)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Unsupported document type: {attachment.MimeType}");
                    progressCallback?.Invoke($"❌ Unsupported document type: {attachment.MimeType}");
                    return new List<Requirement>();
                }

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

                    // Verify document presence and force reprocessing if needed
                    progressCallback?.Invoke("Verifying document processing...");
                    
                    // Wait a moment for initial vectorization
                    await Task.Delay(3000, cancellationToken);
                    
                    var documents = await _llmService.GetWorkspaceDocumentsAsync(workspaceSlug, cancellationToken);
                    if (!documents.HasValue || documents.Value.GetArrayLength() == 0)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] No documents found in workspace after upload - forcing reprocessing");
                        
                        // Run vectorization diagnostics to understand the issue
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Running AnythingLLM vectorization diagnostics...");
                        await _llmService.DiagnoseVectorizationAsync(cancellationToken);
                        
                        progressCallback?.Invoke("Re-processing document for proper vectorization...");
                        
                        var reprocessResult = await _llmService.ForceDocumentReprocessingAsync(workspaceSlug, attachment.FileName, Convert.ToBase64String(fileBytes), cancellationToken);
                        if (!reprocessResult)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Document reprocessing failed - AnythingLLM embedding service appears to be critically broken");
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] This may indicate AnythingLLM crashes during document embedding operations");
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] 🔄 Switching to direct text extraction - bypassing broken embedding service");
                            
                            progressCallback?.Invoke("🚨 AnythingLLM embedding broken - using direct text extraction...");
                            
                            // Try direct text extraction as fallback when vectorization fails
                            var fallbackRequirements = await TryDirectTextExtractionAsync(attachment, fileBytes, progressCallback, cancellationToken);
                            if (fallbackRequirements.Count > 0)
                            {
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Direct text extraction successful - found {fallbackRequirements.Count} requirements (reliable method)");
                                progressCallback?.Invoke($"✅ Direct extraction successful - found {fallbackRequirements.Count} requirements (AnythingLLM bypassed)");
                                return fallbackRequirements;
                            }
                            
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] ❌ Both RAG and direct text extraction failed");
                            progressCallback?.Invoke("❌ All extraction methods failed");
                            return new List<Requirement>();
                        }
                        progressCallback?.Invoke("✅ Document reprocessing successful");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Verified {documents.Value.GetArrayLength()} documents present in workspace");
                    }

                    // Check document content quality
                    var contentOk = await _llmService.CheckDocumentContentAsync(workspaceSlug);
                    if (!contentOk)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] 🔧 Document content extraction failed - trying alternative upload");
                        
                        var alternativeSuccess = await _llmService.TryAlternativeUploadAsync(tempFilePath, workspaceSlug);
                        if (!alternativeSuccess)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] ❌ All upload methods failed - cannot process document");
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] 💡 Manual PDF text extraction may be required");
                            progressCallback?.Invoke("❌ Document text extraction failed - manual extraction may be required");
                            return new List<Requirement>();
                        }
                        
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] ✅ Alternative upload successful - proceeding with extraction");
                        progressCallback?.Invoke("✅ Alternative document processing successful");
                    }

                    progressCallback?.Invoke($"Analyzing document with AI - this may take 2-4 minutes...");
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
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Error parsing attachment {attachment.Id}: {ex.Message}");
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

                var requirements = await ParseAttachmentAsync(attachment, projectId, null, cancellationToken);
                allRequirements.AddRange(requirements);
            }

            return allRequirements;
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
                        TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] 🚨 Monitoring detected embedding failure - forcing fallback");
                        progressCallback?.Invoke("🚨 Embedding failure detected - switching to direct extraction");
                        return false; // Force fallback to direct extraction
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
                        TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] 🔍 Upload failed - triggering fallback");
                        progressCallback?.Invoke("❌ Document upload failed - switching to direct extraction");
                        return false;
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
                            TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] ⚠️ Upload 'succeeded' but 0 documents in workspace - embedding failed");
                            progressCallback?.Invoke("⚠️ Embedding incomplete - switching to direct extraction"); 
                            return false; // Force fallback
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
        /// </summary>
        private async Task<List<Requirement>> TryDirectTextExtractionAsync(
            JamaAttachment attachment, 
            byte[] fileBytes, 
            Action<string>? progressCallback, 
            CancellationToken cancellationToken)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] 🔄 Starting direct text extraction fallback for {attachment.FileName}");
                progressCallback?.Invoke("Extracting text directly from PDF...");
                
                // For now, use a simplified text extraction approach
                // This could be enhanced with proper PDF libraries in the future
                string extractedText = string.Empty;
                
                try
                {
                    // Simple fallback - just convert bytes to text and look for patterns
                    // This is very basic but works as a last resort
                    var base64 = Convert.ToBase64String(fileBytes);
                    extractedText = System.Text.Encoding.UTF8.GetString(fileBytes, 0, Math.Min(fileBytes.Length, 10000));
                    
                    // If that doesn't work, flag for manual extraction
                    if (string.IsNullOrWhiteSpace(extractedText) || extractedText.Length < 50)
                    {
                        progressCallback?.Invoke("⚠️ PDF text extraction limited - manual review may be needed");
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Basic text extraction yielded insufficient content");
                        return CreateManualExtractionPlaceholder(attachment);
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Text extraction failed: {ex.Message}");
                    return CreateManualExtractionPlaceholder(attachment);
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Extracted {extractedText.Length} characters for analysis");
                progressCallback?.Invoke($"Analyzing {extractedText.Length:N0} characters of text...");
                
                // Use simple pattern-based requirement detection as fallback
                var requirements = ParseRequirementsFromText(extractedText, attachment);
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Direct extraction found {requirements.Count} potential requirements");
                return requirements;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaDocumentParser] Error in direct text extraction fallback");
                return CreateManualExtractionPlaceholder(attachment);
            }
        }
        
        /// <summary>
        /// Creates a placeholder requirement indicating manual extraction is needed
        /// </summary>
        private List<Requirement> CreateManualExtractionPlaceholder(JamaAttachment attachment)
        {
            var placeholder = new Requirement
            {
                Item = "MANUAL-EXTRACT-001",
                Description = $"⚠️ MANUAL EXTRACTION REQUIRED: Document '{attachment.FileName}' could not be automatically processed due to AnythingLLM vectorization failure. Please manually review this document for requirements.",
                ItemType = "MANUAL_EXTRACTION_NEEDED"
            };
            
            return new List<Requirement> { placeholder };
        }
        
        /// <summary>
        /// Simple regex-based requirement parsing for fallback when AI processing fails
        /// </summary>
        private List<Requirement> ParseRequirementsFromText(string text, JamaAttachment attachment)
        {
            var requirements = new List<Requirement>();
            var requirementId = 1;
            
            try
            {
                // Look for common requirement patterns
                var patterns = new[]
                {
                    @"(\b(?:shall|must|will|should|require[sd]?)\b[^.!?]*[.!?])",
                    @"(\b(?:The system|The device|The software|The hardware)\s+(?:shall|must|will|should)[^.!?]*[.!?])",
                    @"(\b\d+\.\d+(?:\.\d+)*\s+[^.!?]*(?:shall|must|will|should)[^.!?]*[.!?])",
                };
                
                foreach (var pattern in patterns)
                {
                    var regex = new System.Text.RegularExpressions.Regex(pattern, 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | 
                        System.Text.RegularExpressions.RegexOptions.Multiline);
                    
                    var matches = regex.Matches(text);
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        var requirementText = match.Groups[1].Value.Trim();
                        if (requirementText.Length > 20 && requirementText.Length < 1000) // Reasonable length
                        {
                            var req = new Requirement
                            {
                                Item = $"FALLBACK-{requirementId:D3}",
                                Description = requirementText,
                                ItemType = "FALLBACK_EXTRACTED"
                            };
                            
                            requirements.Add(req);
                            requirementId++;
                            
                            if (requirements.Count >= 50) break; // Limit to prevent overload
                        }
                    }
                }
                
                // If no patterns found, create a manual extraction notice
                if (requirements.Count == 0)
                {
                    return CreateManualExtractionPlaceholder(attachment);
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Pattern-based extraction found {requirements.Count} requirements");
                return requirements;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaDocumentParser] Error parsing requirements from text");
                return CreateManualExtractionPlaceholder(attachment);
            }
        }
    }
}
