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
        public async Task<List<Requirement>> ParseAttachmentAsync(JamaAttachment attachment, int projectId, System.Action<string>? progressCallback = null, CancellationToken cancellationToken = default)
        {
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
                    progressCallback?.Invoke("❌ Failed to download document");
                    return new List<Requirement>();
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
                    var uploadSuccess = await UploadFileToWorkspaceAsync(workspaceSlug, tempFilePath, cancellationToken);
                    
                    if (!uploadSuccess)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] Failed to upload document to workspace");
                        progressCallback?.Invoke("❌ Failed to upload document to AI workspace");
                        return new List<Requirement>();
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

                // Parse LLM response into requirements
                var requirements = ParseRequirementsFromLLMResponse(response, attachment, projectId);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Extracted {requirements.Count} requirements from single comprehensive prompt (vs previous 4-step process)");

                progressCallback?.Invoke($"✅ Extracted {requirements.Count} requirements in single pass");
                return requirements;
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
            return $@"COMPREHENSIVE DOCUMENT ANALYSIS: {attachment.FileName}

🎯 TASK: Perform complete requirement extraction with built-in validation in a single pass.

📋 PROCESS OVERVIEW:
1. First, verify what document content you can actually access
2. Extract ALL requirements comprehensively from accessible content  
3. Self-validate each requirement before including it in final output

⚡ STEP 1 - CONTENT VERIFICATION:
Quickly assess what document sections/content you can access through RAG retrieval.
Note accessible pages, technical topics, specifications, tables, etc.

⚡ STEP 2 - COMPREHENSIVE EXTRACTION:
Extract ALL technical requirements, specifications, and constraints from accessible content:
• Functional requirements (system behavior, operations, features)
• Performance specifications (speed, timing, accuracy, throughput, latency)  
• Interface requirements (protocols, signals, connectors, voltages, communication)
• Environmental constraints (temperature, humidity, vibration, operating conditions)
• Design constraints (size, weight, power, materials, tolerances)
• Safety requirements (protection, fail-safe behavior, hazard mitigation)
• Test/verification requirements (acceptance criteria, procedures, validation)

Look for SHALL, MUST, WILL, SHOULD statements and convert specifications into requirement format.
Be thorough - check ALL sections, tables, figures, appendices you can access.

⚡ STEP 3 - SELF-VALIDATION:
For each requirement, verify it exists in the actual document content before including.
Only include requirements with clear evidence in the accessible text.

📝 OUTPUT FORMAT: For each VALIDATED requirement, use:

---
ID: REQ-001  
Text: [Exact requirement text or converted specification]
Category: [Functional/Performance/Interface/Environmental/Safety/Design/Test]
Priority: [High/Medium/Low or as specified]
Verification: [How this would be verified/tested]
Source: [Page/section reference from accessible content]
---

🎯 GOAL: Extract 15-30+ requirements for technical documents while maintaining accuracy.

Begin comprehensive analysis now - verify content access, extract thoroughly, validate rigorously.";
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
                
                // Look for VALID: REQ-XXX patterns
                if (trimmedLine.StartsWith("VALID:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmedLine.Split(new[] { ' ', '-', ':' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    // Find requirement ID in the line (should be REQ-XXX format)
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i].StartsWith("REQ", StringComparison.OrdinalIgnoreCase) && 
                            requirementLookup.ContainsKey(parts[i]))
                        {
                            var reqId = parts[i];
                            validRequirements.Add(requirementLookup[reqId]);
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaDocumentParser] Validated requirement: {reqId}");
                            break;
                        }
                    }
                }
                // Log INVALID requirements for debugging
                else if (trimmedLine.StartsWith("INVALID:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmedLine.Split(new[] { ' ', '-', ':' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i].StartsWith("REQ", StringComparison.OrdinalIgnoreCase))
                        {
                            TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaDocumentParser] LLM marked as INVALID: {parts[i]} - {trimmedLine}");
                            break;
                        }
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
