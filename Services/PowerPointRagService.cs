using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services.Logging;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// PowerPoint RAG service implementation using existing DirectRAG and LLM infrastructure.
    /// Provides simple interface for users to upload PowerPoints and query their content.
    /// Follows Architectural Guide AI patterns for service implementation.
    /// </summary>
    public class PowerPointRagService : IPowerPointRagService
    {
        private readonly IDirectRagService _directRagService;
        private readonly ITextGenerationService _llmService;
        private readonly ILogger<PowerPointRagService> _logger;
        private readonly string _workspaceStoragePath;
        
        // Use a special project ID for PowerPoint-specific workspaces
        private const int POWERPOINT_PROJECT_BASE_ID = 900000; // Start high to avoid conflicts with real Jama projects

        public bool IsConfigured => _directRagService?.IsConfigured == true && _llmService != null;

        public PowerPointRagService(IDirectRagService directRagService, ITextGenerationService llmService, ILogger<PowerPointRagService> logger)
        {
            _directRagService = directRagService ?? throw new ArgumentNullException(nameof(directRagService));
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Store workspace metadata in app data directory
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _workspaceStoragePath = Path.Combine(appDataPath, "TestCaseEditorApp", "PowerPointWorkspaces");
            Directory.CreateDirectory(_workspaceStoragePath);
            
            _logger.LogInformation("[PowerPointRAG] Service initialized with storage path: {StoragePath}", _workspaceStoragePath);
        }

        public async Task<bool> IndexPowerPointAsync(string filePath, string? documentName = null, string workspaceId = "default", CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.LogError("[PowerPointRAG] File not found: {FilePath}", filePath);
                    return false;
                }

                var fileName = Path.GetFileName(filePath);
                var displayName = documentName ?? Path.GetFileNameWithoutExtension(fileName);
                
                _logger.LogInformation("[PowerPointRAG] Indexing PowerPoint file: {FilePath} as {DisplayName}", filePath, displayName);
                
                var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
                return await IndexPowerPointFromBytesAsync(fileBytes, displayName, workspaceId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PowerPointRAG] Failed to index PowerPoint file: {FilePath}", filePath);
                return false;
            }
        }

        public async Task<bool> IndexPowerPointFromBytesAsync(byte[] powerPointBytes, string documentName, string workspaceId = "default", CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("[PowerPointRAG] Starting indexing for {DocumentName} in workspace {WorkspaceId} ({FileSize} bytes)", 
                    documentName, workspaceId, powerPointBytes.Length);

                // Extract text from PowerPoint
                var extractedText = await ExtractPowerPointTextAsync(powerPointBytes);
                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    _logger.LogWarning("[PowerPointRAG] No text could be extracted from {DocumentName}", documentName);
                    return false;
                }

                // Create a mock JamaAttachment for compatibility with DirectRagService
                var mockAttachment = new JamaAttachment
                {
                    Id = documentName.GetHashCode(), // Use hash as unique ID
                    FileName = $"{documentName}.pptx",
                    Name = documentName,
                    MimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                    FileSize = powerPointBytes.Length,
                    Item = 0, // Not associated with a Jama item
                    CreatedDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };

                // Get workspace-specific project ID
                var projectId = GetProjectIdForWorkspace(workspaceId);
                
                // Index using DirectRAG service
                var indexed = await _directRagService.IndexDocumentAsync(mockAttachment, extractedText, projectId, cancellationToken);
                
                if (indexed)
                {
                    // Save workspace metadata
                    await SaveWorkspaceMetadataAsync(workspaceId, documentName, powerPointBytes.Length, GetSlideCount(extractedText));
                    _logger.LogInformation("[PowerPointRAG] Successfully indexed {DocumentName} in workspace {WorkspaceId}", documentName, workspaceId);
                }
                
                return indexed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PowerPointRAG] Failed to index PowerPoint document: {DocumentName}", documentName);
                return false;
            }
        }

        public async Task<List<PowerPointSearchResult>> QueryPresentationsAsync(string query, string workspaceId = "default", int maxResults = 5, float similarityThreshold = 0.7f, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("[PowerPointRAG] Executing query in workspace {WorkspaceId}: {Query}", workspaceId, query);
                
                var projectId = GetProjectIdForWorkspace(workspaceId);
                var searchResults = await _directRagService.SearchDocumentsAsync(query, projectId, maxResults, similarityThreshold, cancellationToken);
                
                var powerPointResults = searchResults.Select(result => new PowerPointSearchResult
                {
                    DocumentName = result.DocumentName,
                    SlideContent = result.ChunkText,
                    SlideNumber = ExtractSlideNumber(result.ChunkText),
                    SimilarityScore = result.SimilarityScore,
                    WorkspaceId = workspaceId,
                    IndexedDate = result.LastIndexed
                }).ToList();
                
                _logger.LogInformation("[PowerPointRAG] Query returned {ResultCount} results", powerPointResults.Count);
                return powerPointResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PowerPointRAG] Failed to query presentations in workspace {WorkspaceId}", workspaceId);
                return new List<PowerPointSearchResult>();
            }
        }

        public async Task<string> GenerateAnswerAsync(string question, string workspaceId = "default", int maxContextChunks = 5, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("[PowerPointRAG] Generating answer for question in workspace {WorkspaceId}", workspaceId);
                
                // Get relevant context from presentations
                var searchResults = await QueryPresentationsAsync(question, workspaceId, maxContextChunks, 0.6f, cancellationToken);
                
                if (!searchResults.Any())
                {
                    return "I couldn't find any relevant information in the indexed PowerPoint presentations to answer your question. " +
                           "Please make sure you have uploaded presentations that contain the information you're looking for.";
                }

                // Build context from search results
                var contextBuilder = new StringBuilder();
                contextBuilder.AppendLine("Based on the following information from your PowerPoint presentations:");
                contextBuilder.AppendLine();

                foreach (var result in searchResults)
                {
                    contextBuilder.AppendLine($"**From {result.DocumentName} (Slide {result.SlideNumber}):**");
                    contextBuilder.AppendLine(result.SlideContent);
                    contextBuilder.AppendLine();
                }

                // Create prompt for LLM
                var systemPrompt = @"You are a helpful assistant that analyzes PowerPoint presentation content. 
Your task is to provide accurate, concise answers based ONLY on the provided presentation content.

Guidelines:
- Answer in a clear, professional manner
- Use information only from the provided slides
- If you cannot answer based on the provided content, say so
- Cite which presentation/slide information comes from when possible
- Keep answers focused and relevant to the question";

                var userPrompt = $@"Question: {question}

{contextBuilder}

Please provide a comprehensive answer to the question based on this presentation content.";

                // Generate answer using LLM
                var answer = await _llmService.GenerateWithSystemAsync(systemPrompt, userPrompt, cancellationToken);
                
                _logger.LogInformation("[PowerPointRAG] Generated answer of {AnswerLength} characters", answer.Length);
                return answer;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PowerPointRAG] Failed to generate answer for question in workspace {WorkspaceId}", workspaceId);
                return "I encountered an error while trying to generate an answer. Please try again or check the system logs for details.";
            }
        }

        public async Task<List<IndexedPresentation>> GetIndexedPresentationsAsync(string workspaceId = "default", CancellationToken cancellationToken = default)
        {
            try
            {
                var projectId = GetProjectIdForWorkspace(workspaceId);
                var stats = await _directRagService.GetProjectIndexStatsAsync(projectId, cancellationToken);
                
                var workspaceMetadata = await LoadWorkspaceMetadataAsync(workspaceId);
                
                var presentations = stats.IndexedDocuments.Select(docName => 
                {
                    var metadata = workspaceMetadata.FirstOrDefault(m => m.DocumentName == docName);
                    return new IndexedPresentation
                    {
                        DocumentName = docName,
                        SlideCount = metadata?.SlideCount ?? 0,
                        WorkspaceId = workspaceId,
                        IndexedDate = stats.LastIndexUpdate,
                        FileSizeBytes = metadata?.FileSizeBytes ?? 0
                    };
                }).ToList();
                
                return presentations;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PowerPointRAG] Failed to get indexed presentations for workspace {WorkspaceId}", workspaceId);
                return new List<IndexedPresentation>();
            }
        }

        public async Task<bool> RemovePresentationAsync(string documentName, string workspaceId = "default", CancellationToken cancellationToken = default)
        {
            try
            {
                // Note: DirectRagService doesn't have a method to remove individual documents
                // This would require extending the DirectRagService interface
                _logger.LogWarning("[PowerPointRAG] Individual document removal not yet implemented - consider clearing entire workspace");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PowerPointRAG] Failed to remove presentation {DocumentName} from workspace {WorkspaceId}", documentName, workspaceId);
                return false;
            }
        }

        public async Task<bool> ClearWorkspaceAsync(string workspaceId = "default", CancellationToken cancellationToken = default)
        {
            try
            {
                var projectId = GetProjectIdForWorkspace(workspaceId);
                var cleared = await _directRagService.ClearProjectIndexAsync(projectId, cancellationToken);
                
                if (cleared)
                {
                    // Clear workspace metadata
                    await ClearWorkspaceMetadataAsync(workspaceId);
                }
                
                return cleared;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PowerPointRAG] Failed to clear workspace {WorkspaceId}", workspaceId);
                return false;
            }
        }

        /// <summary>
        /// Extract text from PowerPoint using DocumentFormat.OpenXml
        /// </summary>
        private async Task<string> ExtractPowerPointTextAsync(byte[] powerPointBytes)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var stream = new MemoryStream(powerPointBytes);
                    using var presentationDoc = PresentationDocument.Open(stream, false);
                    
                    if (presentationDoc.PresentationPart == null)
                    {
                        Log.Warn("[PowerPointRAG] PowerPoint document has no presentation part");
                        return "";
                    }

                    var text = new StringBuilder();
                    var slidesPart = presentationDoc.PresentationPart.Presentation.SlideIdList;
                    
                    if (slidesPart == null)
                    {
                        Log.Warn("[PowerPointRAG] PowerPoint document has no slides");
                        return "";
                    }

                    int slideNumber = 1;
                    Log.Info($"[PowerPointRAG] Extracting text from PowerPoint with {slidesPart.Count()} slides");

                    foreach (var slideId in slidesPart.OfType<SlideId>())
                    {
                        try
                        {
                            var slidePart = (SlidePart)presentationDoc.PresentationPart.GetPartById(slideId.RelationshipId.Value);
                            
                            text.AppendLine($"--- Slide {slideNumber} ---");
                            
                            // Extract text from all text runs in the slide
                            foreach (var textElement in slidePart.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>())
                            {
                                if (!string.IsNullOrWhiteSpace(textElement.Text))
                                {
                                    text.AppendLine(textElement.Text.Trim());
                                }
                            }
                            
                            text.AppendLine(); // Add spacing between slides
                            slideNumber++;
                        }
                        catch (Exception slideEx)
                        {
                            Log.Warn($"[PowerPointRAG] Failed to extract text from slide {slideNumber}: {slideEx.Message}");
                            text.AppendLine($"[Slide {slideNumber} - extraction failed]");
                            slideNumber++;
                        }
                    }

                    return text.ToString();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[PowerPointRAG] Failed to extract PowerPoint text");
                    throw;
                }
            });
        }

        #region Helper Methods

        /// <summary>
        /// Generate a unique project ID for each workspace
        /// </summary>
        private int GetProjectIdForWorkspace(string workspaceId)
        {
            return POWERPOINT_PROJECT_BASE_ID + Math.Abs(workspaceId.GetHashCode()) % 10000;
        }

        /// <summary>
        /// Extract slide number from content that contains "--- Slide X ---" markers
        /// </summary>
        private int ExtractSlideNumber(string content)
        {
            try
            {
                var lines = content.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("--- Slide") && line.Contains("---"))
                    {
                        var parts = line.Split(' ');
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (parts[i] == "Slide" && i + 1 < parts.Length)
                            {
                                if (int.TryParse(parts[i + 1], out int slideNum))
                                {
                                    return slideNum;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
            return 1; // Default to slide 1
        }

        /// <summary>
        /// Count slides from extracted text
        /// </summary>
        private int GetSlideCount(string extractedText)
        {
            return extractedText.Split(new[] { "--- Slide" }, StringSplitOptions.None).Length - 1;
        }

        /// <summary>
        /// Save workspace metadata for tracking indexed presentations
        /// </summary>
        private async Task SaveWorkspaceMetadataAsync(string workspaceId, string documentName, long fileSizeBytes, int slideCount)
        {
            try
            {
                var metadataPath = Path.Combine(_workspaceStoragePath, $"{workspaceId}.json");
                var metadata = await LoadWorkspaceMetadataAsync(workspaceId);
                
                // Remove existing entry for this document
                metadata.RemoveAll(m => m.DocumentName == documentName);
                
                // Add new entry
                metadata.Add(new WorkspaceMetadata
                {
                    DocumentName = documentName,
                    FileSizeBytes = fileSizeBytes,
                    SlideCount = slideCount,
                    IndexedDate = DateTime.Now
                });
                
                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(metadataPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PowerPointRAG] Failed to save workspace metadata");
            }
        }

        /// <summary>
        /// Load workspace metadata
        /// </summary>
        private async Task<List<WorkspaceMetadata>> LoadWorkspaceMetadataAsync(string workspaceId)
        {
            try
            {
                var metadataPath = Path.Combine(_workspaceStoragePath, $"{workspaceId}.json");
                if (!File.Exists(metadataPath))
                {
                    return new List<WorkspaceMetadata>();
                }
                
                var json = await File.ReadAllTextAsync(metadataPath);
                return JsonSerializer.Deserialize<List<WorkspaceMetadata>>(json) ?? new List<WorkspaceMetadata>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PowerPointRAG] Failed to load workspace metadata");
                return new List<WorkspaceMetadata>();
            }
        }

        /// <summary>
        /// Clear workspace metadata
        /// </summary>
        private async Task ClearWorkspaceMetadataAsync(string workspaceId)
        {
            try
            {
                var metadataPath = Path.Combine(_workspaceStoragePath, $"{workspaceId}.json");
                if (File.Exists(metadataPath))
                {
                    File.Delete(metadataPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PowerPointRAG] Failed to clear workspace metadata");
            }
        }

        #endregion

        /// <summary>
        /// Internal class for storing workspace metadata
        /// </summary>
        private class WorkspaceMetadata
        {
            public string DocumentName { get; set; } = "";
            public long FileSizeBytes { get; set; }
            public int SlideCount { get; set; }
            public DateTime IndexedDate { get; set; }
        }
    }
}