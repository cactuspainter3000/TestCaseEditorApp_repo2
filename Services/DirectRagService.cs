using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services.Logging;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Direct RAG service using Ollama embeddings for document search and context enhancement.
    /// Bypasses AnythingLLM dependency by implementing RAG functionality directly with Ollama.
    /// Follows Architectural Guide AI patterns for service implementation.
    /// </summary>
    public class DirectRagService : IDirectRagService
    {
        private readonly IOllamaEmbeddingService _embeddingService;
        private readonly ILogger<DirectRagService> _logger;
        private readonly string _indexStoragePath;
        private readonly ConcurrentDictionary<int, ProjectDocumentIndex> _projectIndexes = new();
        private const int DefaultChunkSize = 350; // Much smaller for 512 token context limit
        private const int ChunkOverlap = 50; // Reduced proportionally
        private const string EmbeddingModel = "mxbai-embed-large:335m-v1-fp16";

        public bool IsConfigured => _embeddingService != null;

        public DirectRagService(IOllamaEmbeddingService embeddingService, ILogger<DirectRagService> logger)
        {
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Store indexes in app data directory
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _indexStoragePath = Path.Combine(appDataPath, "TestCaseEditorApp", "DocumentIndexes");
            Directory.CreateDirectory(_indexStoragePath);
            
            _logger.LogInformation("[DirectRAG] Service initialized with storage path: {StoragePath}", _indexStoragePath);
        }

        public async Task<bool> IndexDocumentAsync(JamaAttachment attachment, string documentContent, int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("[DirectRAG] Starting indexing for document {AttachmentId} in project {ProjectId}", attachment.Id, projectId);
                
                if (string.IsNullOrWhiteSpace(documentContent))
                {
                    _logger.LogWarning("[DirectRAG] Document content is empty for attachment {AttachmentId}", attachment.Id);
                    return false;
                }

                // Get or create project index
                var projectIndex = await GetProjectIndexAsync(projectId);
                
                // Remove existing document if present (re-indexing)
                projectIndex.RemoveDocument(attachment.Id);

                // Split document into chunks
                var chunks = SplitIntoChunks(documentContent, DefaultChunkSize, ChunkOverlap);
                _logger.LogInformation("[DirectRAG] Split document into {ChunkCount} chunks", chunks.Count);

                // Generate embeddings for each chunk
                var documentChunks = new List<DocumentChunk>();
                for (int i = 0; i < chunks.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return false;

                    var chunk = chunks[i];
                    var embedding = await GenerateEmbeddingAsync(chunk, cancellationToken);
                    
                    if (embedding != null)
                    {
                        documentChunks.Add(new DocumentChunk
                        {
                            AttachmentId = attachment.Id,
                            DocumentName = attachment.FileName,
                            DocumentType = attachment.MimeType,
                            ChunkIndex = i,
                            Text = chunk,
                            Embedding = embedding,
                            LastIndexed = DateTime.UtcNow
                        });
                    }
                }

                // Add chunks to project index
                projectIndex.AddDocument(attachment, documentChunks);
                
                // Persist index
                await SaveProjectIndexAsync(projectIndex);
                
                _logger.LogInformation("[DirectRAG] Successfully indexed {ChunkCount} chunks for document {AttachmentId}", 
                    documentChunks.Count, attachment.Id);
                    
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DirectRAG] Failed to index document {AttachmentId}: {Error}", attachment.Id, ex.Message);
                return false;
            }
        }

        public async Task<List<DocumentSearchResult>> SearchDocumentsAsync(string query, int projectId, int maxResults = 5, float similarityThreshold = 0.7f, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("[DirectRAG] Searching for query '{Query}' in project {ProjectId}", query, projectId);
                
                var projectIndex = await GetProjectIndexAsync(projectId);
                if (projectIndex.IsEmpty)
                {
                    _logger.LogInformation("[DirectRAG] No documents indexed for project {ProjectId}", projectId);
                    return new List<DocumentSearchResult>();
                }

                // Generate query embedding
                var queryEmbedding = await GenerateEmbeddingAsync(query, cancellationToken);
                if (queryEmbedding == null)
                {
                    _logger.LogError("[DirectRAG] Failed to generate embedding for query");
                    return new List<DocumentSearchResult>();
                }

                // Find similar chunks
                var results = new List<DocumentSearchResult>();
                foreach (var chunk in projectIndex.GetAllChunks())
                {
                    var similarity = CalculateCosineSimilarity(queryEmbedding, chunk.Embedding);
                    
                    if (similarity >= similarityThreshold)
                    {
                        results.Add(new DocumentSearchResult
                        {
                            DocumentName = chunk.DocumentName,
                            AttachmentId = chunk.AttachmentId,
                            ChunkText = chunk.Text,
                            SimilarityScore = similarity,
                            ChunkIndex = chunk.ChunkIndex,
                            DocumentType = chunk.DocumentType,
                            LastIndexed = chunk.LastIndexed
                        });
                    }
                }

                // Sort by similarity and take top results
                results = results.OrderByDescending(r => r.SimilarityScore).Take(maxResults).ToList();
                
                _logger.LogInformation("[DirectRAG] Found {ResultCount} matching chunks for query", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DirectRAG] Search failed for project {ProjectId}: {Error}", projectId, ex.Message);
                return new List<DocumentSearchResult>();
            }
        }

        public async Task<string> GetRequirementAnalysisContextAsync(string requirementText, int projectId, int maxContextChunks = 3, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("[DirectRAG] Getting analysis context for requirement in project {ProjectId}", projectId);
                
                // Search for relevant context with lower threshold for analysis
                var searchResults = await SearchDocumentsAsync(requirementText, projectId, maxContextChunks, 0.5f, cancellationToken);
                
                if (searchResults.Count == 0)
                {
                    _logger.LogInformation("[DirectRAG] No relevant context found for requirement analysis");
                    return "";
                }

                // Build context string from search results
                var contextBuilder = new StringBuilder();
                contextBuilder.AppendLine("RELEVANT PROJECT DOCUMENTATION:");
                contextBuilder.AppendLine();

                foreach (var result in searchResults)
                {
                    contextBuilder.AppendLine($"From: {result.DocumentName} (Relevance: {result.SimilarityScore:F2})");
                    contextBuilder.AppendLine($"Content: {result.ChunkText}");
                    contextBuilder.AppendLine();
                }

                var context = contextBuilder.ToString();
                _logger.LogInformation("[DirectRAG] Generated {ContextLength} characters of context from {ChunkCount} chunks", 
                    context.Length, searchResults.Count);
                    
                return context;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DirectRAG] Failed to get requirement analysis context: {Error}", ex.Message);
                return "";
            }
        }

        public async Task<bool> ClearProjectIndexAsync(int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("[DirectRAG] Clearing index for project {ProjectId}", projectId);
                
                // Remove from memory
                _projectIndexes.TryRemove(projectId, out _);
                
                // Delete from storage
                var indexFilePath = GetProjectIndexPath(projectId);
                if (File.Exists(indexFilePath))
                {
                    File.Delete(indexFilePath);
                }
                
                _logger.LogInformation("[DirectRAG] Successfully cleared index for project {ProjectId}", projectId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DirectRAG] Failed to clear project index {ProjectId}: {Error}", projectId, ex.Message);
                return false;
            }
        }

        public async Task<DocumentIndexStats> GetProjectIndexStatsAsync(int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                var projectIndex = await GetProjectIndexAsync(projectId);
                
                return new DocumentIndexStats
                {
                    ProjectId = projectId,
                    TotalDocuments = projectIndex.DocumentCount,
                    TotalChunks = projectIndex.ChunkCount,
                    LastIndexUpdate = projectIndex.LastUpdate,
                    IndexedDocuments = projectIndex.GetDocumentNames()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DirectRAG] Failed to get project index stats {ProjectId}: {Error}", projectId, ex.Message);
                return new DocumentIndexStats { ProjectId = projectId };
            }
        }

        // === PRIVATE IMPLEMENTATION ===

        private async Task<ProjectDocumentIndex> GetProjectIndexAsync(int projectId)
        {
            // Check memory cache first
            if (_projectIndexes.TryGetValue(projectId, out var cachedIndex))
            {
                return cachedIndex;
            }

            // Try to load from storage
            var indexFilePath = GetProjectIndexPath(projectId);
            if (File.Exists(indexFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(indexFilePath);
                    var index = JsonSerializer.Deserialize<ProjectDocumentIndex>(json) ?? new ProjectDocumentIndex(projectId);
                    _projectIndexes[projectId] = index;
                    return index;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[DirectRAG] Failed to load index for project {ProjectId}, creating new", projectId);
                }
            }

            // Create new index
            var newIndex = new ProjectDocumentIndex(projectId);
            _projectIndexes[projectId] = newIndex;
            return newIndex;
        }

        private async Task SaveProjectIndexAsync(ProjectDocumentIndex index)
        {
            var indexFilePath = GetProjectIndexPath(index.ProjectId);
            var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(indexFilePath, json);
        }

        private string GetProjectIndexPath(int projectId)
        {
            return Path.Combine(_indexStoragePath, $"project_{projectId}.json");
        }

        private async Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                // Use Ollama embedding service
                var embedding = await _embeddingService.GenerateEmbeddingAsync(text, cancellationToken);
                return embedding;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DirectRAG] Failed to generate embedding for text: {Error}", ex.Message);
                return null;
            }
        }

        private List<string> SplitIntoChunks(string text, int chunkSize, int overlap)
        {
            var chunks = new List<string>();
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            for (int i = 0; i < words.Length; i += chunkSize - overlap)
            {
                var chunkWords = words.Skip(i).Take(chunkSize);
                var chunk = string.Join(" ", chunkWords);
                
                if (!string.IsNullOrWhiteSpace(chunk))
                {
                    chunks.Add(chunk);
                }
                
                // Break if we've processed all words
                if (i + chunkSize >= words.Length)
                    break;
            }

            return chunks;
        }

        private float CalculateCosineSimilarity(float[] vector1, float[] vector2)
        {
            // Use the embedding service's optimized cosine similarity calculation
            return _embeddingService.CalculateCosineSimilarity(vector1, vector2);
        }
    }

    /// <summary>
    /// Internal class for managing document chunks and embeddings for a project
    /// </summary>
    public class ProjectDocumentIndex
    {
        public int ProjectId { get; set; }
        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
        public Dictionary<int, List<DocumentChunk>> DocumentChunks { get; set; } = new();

        public ProjectDocumentIndex() { }
        
        public ProjectDocumentIndex(int projectId)
        {
            ProjectId = projectId;
        }

        public bool IsEmpty => DocumentChunks.Count == 0;
        
        public int DocumentCount => DocumentChunks.Count;
        
        public int ChunkCount => DocumentChunks.Values.Sum(chunks => chunks.Count);

        public void AddDocument(JamaAttachment attachment, List<DocumentChunk> chunks)
        {
            DocumentChunks[attachment.Id] = chunks;
            LastUpdate = DateTime.UtcNow;
        }

        public void RemoveDocument(int attachmentId)
        {
            DocumentChunks.Remove(attachmentId);
            LastUpdate = DateTime.UtcNow;
        }

        public IEnumerable<DocumentChunk> GetAllChunks()
        {
            return DocumentChunks.Values.SelectMany(chunks => chunks);
        }

        public List<string> GetDocumentNames()
        {
            return GetAllChunks().Select(c => c.DocumentName).Distinct().ToList();
        }
    }

    /// <summary>
    /// Represents a document chunk with its embedding for similarity search
    /// </summary>
    public class DocumentChunk
    {
        public int AttachmentId { get; set; }
        public string DocumentName { get; set; } = "";
        public string DocumentType { get; set; } = "";
        public int ChunkIndex { get; set; }
        public string Text { get; set; } = "";
        public float[] Embedding { get; set; } = Array.Empty<float>();
        public DateTime LastIndexed { get; set; }
    }
}