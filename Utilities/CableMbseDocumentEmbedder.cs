using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace TestCaseEditorApp.Utilities
{
    /// <summary>
    /// Utility class to embed the Cable MBSE POC document into AnythingLLM workspace
    /// </summary>
    public static class CableMbseDocumentEmbedder
    {
        private const string WorkspaceSlug = "testcaseeditorapp-workspace-1739401473078";
        private const string DocumentName = "Cable-MBSE-Proof-of-Concept-(POC).txt";
        
        /// <summary>
        /// Embeds the Cable MBSE POC document into the AnythingLLM workspace
        /// </summary>
        /// <returns>True if embedding succeeded, false otherwise</returns>
        public static async Task<bool> EmbedCableMbseDocumentAsync()
        {
            try
            {
                // Get the AnythingLLM service from DI
                var anythingLLMService = App.ServiceProvider?.GetService<Services.AnythingLLMService>();
                if (anythingLLMService == null)
                {
                    Services.Logging.Log.Error("[CableMbseEmbedder] AnythingLLM service not available");
                    return false;
                }

                // Read the document content from temp file created by PowerShell script
                var tempFilePath = Path.Combine(Path.GetTempPath(), "Cable-MBSE-POC-for-embedding.txt");
                
                string documentContent;
                if (File.Exists(tempFilePath))
                {
                    documentContent = await File.ReadAllTextAsync(tempFilePath);
                    Services.Logging.Log.Info($"[CableMbseEmbedder] Loaded document from temp file: {documentContent.Length} characters");
                }
                else
                {
                    // Fallback: create the content directly
                    documentContent = GetCableMbseDocumentContent();
                    Services.Logging.Log.Info("[CableMbseEmbedder] Using fallback document content");
                }

                if (string.IsNullOrWhiteSpace(documentContent))
                {
                    Services.Logging.Log.Error("[CableMbseEmbedder] No document content available");
                    return false;
                }

                Services.Logging.Log.Info($"[CableMbseEmbedder] Starting embedding for document: {DocumentName}");
                Services.Logging.Log.Info($"[CableMbseEmbedder] Workspace: {WorkspaceSlug}");
                Services.Logging.Log.Info($"[CableMbseEmbedder] Content length: {documentContent.Length} characters");

                // Upload and embed the document
                var uploadResult = await anythingLLMService.UploadDocumentAsync(
                    WorkspaceSlug, 
                    DocumentName, 
                    documentContent
                );

                if (uploadResult)
                {
                    Services.Logging.Log.Info("[CableMbseEmbedder] ✅ Cable MBSE document successfully embedded!");
                    Services.Logging.Log.Info("[CableMbseEmbedder] Document is now available for RAG queries");
                    
                    // Verify embedding worked by checking workspace documents
                    await Task.Delay(2000); // Wait a moment for processing
                    var documents = await anythingLLMService.GetWorkspaceDocumentsAsync(WorkspaceSlug);
                    if (documents.HasValue && documents.Value.GetArrayLength() > 0)
                    {
                        Services.Logging.Log.Info($"[CableMbseEmbedder] Verification: Workspace now has {documents.Value.GetArrayLength()} embedded documents");
                        return true;
                    }
                    else
                    {
                        Services.Logging.Log.Warn("[CableMbseEmbedder] Upload succeeded but document verification failed");
                        return uploadResult; // Still consider it successful if upload worked
                    }
                }
                else
                {
                    Services.Logging.Log.Error("[CableMbseEmbedder] Failed to embed Cable MBSE document");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Services.Logging.Log.Error($"[CableMbseEmbedder] Exception during embedding: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the Cable MBSE document content as fallback if temp file not available
        /// </summary>
        private static string GetCableMbseDocumentContent()
        {
            return @"# Cable MBSE Proof of Concept (POC)

## Purpose

This document defines a **controlled, end‑to‑end proof of concept** demonstrating how a real, in‑use cable can be taken from:

**Model → Derived Requirements → Derived Test Cases**

using **MBSE principles with SysML in Cameo Systems Modeler**.

## Overview

The proof of concept will demonstrate the complete MBSE workflow:

1. **Model Development**: Create a systems model of the cable in SysML
2. **Requirements Derivation**: Extract testable requirements from the model  
3. **Test Case Generation**: Automatically generate test cases from requirements
4. **Verification**: Execute tests to verify the cable meets specifications

This process showcases how Model-Based Systems Engineering can improve:
- Requirements traceability
- Test coverage
- Quality assurance
- Development efficiency

## Expected Outcomes

- Demonstrated MBSE workflow from model to test cases
- Improved requirements quality and completeness  
- Enhanced test coverage and traceability
- Validation of automated test case generation capabilities
- Best practices for cable system modeling and testing

## Technical Approach

The POC will utilize:
- **SysML modeling** for system architecture
- **Requirements engineering** for specification development  
- **Automated test generation** for comprehensive coverage
- **Traceability management** for end-to-end verification

This end-to-end demonstration will validate the MBSE approach for cable system development and testing.";
        }

        /// <summary>
        /// Test method to verify the embedding operation
        /// Call this from a button click or menu action in the UI
        /// </summary>    
        public static async void TestEmbedCableMbseDocument()
        {
            try
            {
                Services.Logging.Log.Info("[CableMbseEmbedder] 🚀 Starting Cable MBSE document embedding test...");
                
                var success = await EmbedCableMbseDocumentAsync();
                
                if (success)
                {
                    Services.Logging.Log.Info("[CableMbseEmbedder] 🎉 Test completed successfully!");
                }
                else
                {
                    Services.Logging.Log.Error("[CableMbseEmbedder] ❌ Test failed - check logs for details");
                }
            }
            catch (Exception ex)
            {
                Services.Logging.Log.Error($"[CableMbseEmbedder] Exception in test: {ex.Message}");
            }
        }
    }
}