using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        public async Task<List<Requirement>> ParseAttachmentAsync(int attachmentId, int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Starting parse for attachment {attachmentId}");

                // Step 1: Download attachment from Jama
                var fileBytes = await _jamaService.DownloadAttachmentAsync(attachmentId, cancellationToken);
                if (fileBytes == null || fileBytes.Length == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Failed to download attachment {attachmentId}");
                    return new List<Requirement>();
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Downloaded {fileBytes.Length} bytes for attachment {attachmentId}");

                // Step 2: Get attachment metadata to determine file type
                var attachments = await _jamaService.GetProjectAttachmentsAsync(projectId, cancellationToken);
                var attachment = attachments.FirstOrDefault(a => a.Id == attachmentId);
                
                if (attachment == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Attachment {attachmentId} not found in project {projectId}");
                    return new List<Requirement>();
                }

                if (!attachment.IsSupportedDocument)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Unsupported document type: {attachment.MimeType}");
                    return new List<Requirement>();
                }

                // Step 3: Create temporary AnythingLLM workspace for parsing
                var workspaceName = $"Jama Document Parse: {attachment.FileName}";
                
                var workspace = await _llmService.CreateWorkspaceAsync(workspaceName, cancellationToken);
                if (workspace == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Failed to create workspace {workspaceName}");
                    return new List<Requirement>();
                }
                
                var workspaceSlug = workspace.Slug;

                // Step 4: Upload document to AnythingLLM for processing
                var tempFilePath = Path.Combine(Path.GetTempPath(), attachment.FileName);
                try
                {
                    await File.WriteAllBytesAsync(tempFilePath, fileBytes, cancellationToken);
                    
                    // Upload to AnythingLLM using the file-based upload
                    var uploadSuccess = await UploadFileToWorkspaceAsync(workspaceSlug, tempFilePath, cancellationToken);
                    
                    if (!uploadSuccess)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Failed to upload document to workspace");
                        return new List<Requirement>();
                    }

                    // Step 5: Query AnythingLLM to extract requirements
                    var requirements = await ExtractRequirementsFromWorkspaceAsync(workspaceSlug, attachment, projectId, cancellationToken);
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Extracted {requirements.Count} requirements from attachment {attachmentId}");
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
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Error parsing attachment {attachmentId}: {ex.Message}");
                return new List<Requirement>();
            }
        }

        /// <summary>
        /// Parse multiple attachments in batch
        /// </summary>
        public async Task<List<Requirement>> ParseAttachmentsBatchAsync(List<int> attachmentIds, int projectId, CancellationToken cancellationToken = default)
        {
            var allRequirements = new List<Requirement>();

            foreach (var attachmentId in attachmentIds)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var requirements = await ParseAttachmentAsync(attachmentId, projectId, cancellationToken);
                allRequirements.AddRange(requirements);
            }

            return allRequirements;
        }

        /// <summary>
        /// Upload file to AnythingLLM workspace using multipart form data
        /// </summary>
        private async Task<bool> UploadFileToWorkspaceAsync(string workspaceSlug, string filePath, CancellationToken cancellationToken)
        {
            try
            {
                // Read file content
                var fileContent = await File.ReadAllTextAsync(filePath, cancellationToken);
                var fileName = Path.GetFileName(filePath);

                // Use AnythingLLM's UploadDocumentAsync method
                return await _llmService.UploadDocumentAsync(workspaceSlug, fileName, fileContent, cancellationToken);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Error uploading file: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extract requirements from AnythingLLM workspace using LLM prompts
        /// </summary>
        private async Task<List<Requirement>> ExtractRequirementsFromWorkspaceAsync(
            string workspaceSlug, 
            JamaAttachment attachment, 
            int projectId, 
            CancellationToken cancellationToken)
        {
            try
            {
                // Craft extraction prompt
                var prompt = BuildRequirementExtractionPrompt(attachment);

                // Query AnythingLLM
                var response = await _llmService.SendChatMessageAsync(workspaceSlug, prompt, cancellationToken);

                if (string.IsNullOrEmpty(response))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Empty response from LLM");
                    return new List<Requirement>();
                }

                // Parse LLM response into requirements
                var requirements = ParseRequirementsFromLLMResponse(response, attachment, projectId);

                return requirements;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[JamaDocumentParser] Error extracting requirements: {ex.Message}");
                return new List<Requirement>();
            }
        }

        /// <summary>
        /// Build the LLM prompt for requirement extraction
        /// </summary>
        private string BuildRequirementExtractionPrompt(JamaAttachment attachment)
        {
            return $@"You are a requirements analyst extracting formal requirements from a technical document.

Document: {attachment.FileName}
Document Type: {GetDocumentTypeDescription(attachment)}

TASK: Extract all requirements, specifications, constraints, and technical details from this document.

For each requirement, provide:
1. **Requirement ID**: A unique identifier (if present in the document, or generate REQ-001, REQ-002, etc.)
2. **Requirement Text**: The full requirement statement
3. **Category**: Type of requirement (functional, performance, interface, safety, environmental, lifecycle, etc.)
4. **Priority**: If stated (High/Medium/Low)
5. **Verification Method**: How this should be verified (Test, Analysis, Inspection, Demonstration)
6. **Source Context**: Where in the document this came from (section, page number if available)

IMPORTANT FOCUS AREAS:
- Functional requirements (what the system must do)
- Performance specifications (speed, accuracy, throughput, timing)
- Interface requirements (signals, protocols, connectors, impedance, voltage levels)
- Environmental constraints (temperature, humidity, altitude, vibration)
- Lifecycle requirements (MTBF, insertion cycles, durability, maintenance)
- Safety and security requirements
- Design constraints and allocations

OUTPUT FORMAT:
Return each requirement as a structured block:

---
ID: [requirement ID]
Text: [full requirement text]
Category: [category]
Priority: [priority if known]
Verification: [method]
Source: [location in document]
---

Extract ALL valuable requirements and specifications. Be thorough and precise.";
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
                var blocks = llmResponse.Split(new[] { "---" }, StringSplitOptions.RemoveEmptyEntries);
                
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

                // Extract required fields
                if (!reqData.TryGetValue("ID", out var id) || string.IsNullOrWhiteSpace(id))
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Rejecting block: Missing or empty ID field. Available keys: {string.Join(", ", reqData.Keys)}");
                    return null;
                }

                if (!reqData.TryGetValue("Text", out var text) || string.IsNullOrWhiteSpace(text))
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] DEBUG - Rejecting block: Missing or empty Text field. Available keys: {string.Join(", ", reqData.Keys)}");
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
        /// Get human-readable document type description
        /// </summary>
        private string GetDocumentTypeDescription(JamaAttachment attachment)
        {
            if (attachment.IsPdf) return "PDF Document";
            if (attachment.IsWord) return "Word Document";
            if (attachment.IsExcel) return "Excel Spreadsheet";
            return "Document";
        }
    }
}
