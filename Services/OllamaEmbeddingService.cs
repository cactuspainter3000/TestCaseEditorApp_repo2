// Services/OllamaEmbeddingService.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Ollama embedding generation service using the /api/embed endpoint
    /// </summary>
    public sealed class OllamaEmbeddingService : IOllamaEmbeddingService
    {
        private readonly HttpClient _http;
        private readonly string _embeddingModel;
        private readonly int _embeddingDimensions;

        public OllamaEmbeddingService(string embeddingModel = "mxbai-embed-large", HttpClient? http = null)
        {
            _http = http ?? new HttpClient { BaseAddress = new Uri("http://localhost:11434/") };
            _embeddingModel = embeddingModel;
            _http.Timeout = TimeSpan.FromMinutes(2);

            // Set embedding dimensions based on known models
            _embeddingDimensions = embeddingModel switch
            {
                "mxbai-embed-large" => 1024,
                "mxbai-embed-large:335m-v1-fp16" => 1024,
                "nomic-embed-text" => 768,
                "all-minilm" => 384,
                _ => 1024 // Default assumption
            };

            TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaEmbedding] Initialized with model: {_embeddingModel}, dimensions: {_embeddingDimensions}");
        }

        public string EmbeddingModel => _embeddingModel;
        public int EmbeddingDimensions => _embeddingDimensions;

        public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new float[_embeddingDimensions]; // Return zero vector

            // Truncate text if it's too long (model has 512 token context limit)
            // Very conservative: 1 token ≈ 1.5 characters for safety with special tokens
            const int maxChars = 400; // Very conservative for 512 token limit
            if (text.Length > maxChars)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[OllamaEmbedding] Truncating text from {text.Length} to {maxChars} chars due to model context limit");
                text = text.Substring(0, maxChars);
            }

            var payload = new
            {
                model = _embeddingModel,
                input = text
            };

            var json = JsonSerializer.Serialize(payload);
            
            TestCaseEditorApp.Services.Logging.Log.Debug($"[OllamaEmbedding] Generating embedding for text length: {text.Length}");

            try
            {
                using var response = await _http.PostAsync("api/embed",
                    new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Error($"[OllamaEmbedding] HTTP {(int)response.StatusCode} error. Response: {errorContent}");
                }

                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                // Parse Ollama embedding response format
                if (doc.RootElement.TryGetProperty("embeddings", out var embeddingsElement) &&
                    embeddingsElement.ValueKind == JsonValueKind.Array)
                {
                    var embeddings = embeddingsElement.EnumerateArray().FirstOrDefault();
                    if (embeddings.ValueKind == JsonValueKind.Array)
                    {
                        var embedding = embeddings.EnumerateArray()
                            .Select(e => (float)e.GetDouble())
                            .ToArray();
                        
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[OllamaEmbedding] Generated embedding with {embedding.Length} dimensions");
                        return embedding;
                    }
                }

                // Alternative format - single embedding array
                if (doc.RootElement.TryGetProperty("embedding", out var embeddingElement) &&
                    embeddingElement.ValueKind == JsonValueKind.Array)
                {
                    var embedding = embeddingElement.EnumerateArray()
                        .Select(e => (float)e.GetDouble())
                        .ToArray();
                    
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[OllamaEmbedding] Generated embedding with {embedding.Length} dimensions");
                    return embedding;
                }

                TestCaseEditorApp.Services.Logging.Log.Error("[OllamaEmbedding] Unexpected response format from Ollama embedding API");
                return new float[_embeddingDimensions];
            }
            catch (HttpRequestException ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[OllamaEmbedding] HTTP error during embedding generation");
                return new float[_embeddingDimensions];
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[OllamaEmbedding] Timeout during embedding generation");
                return new float[_embeddingDimensions];
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[OllamaEmbedding] Unexpected error during embedding generation");
                return new float[_embeddingDimensions];
            }
        }

        public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            if (texts == null || texts.Count == 0)
                return Array.Empty<float[]>();

            var results = new List<float[]>();
            const int batchSize = 5; // Process in small batches to avoid overwhelming Ollama

            for (int i = 0; i < texts.Count; i += batchSize)
            {
                var batch = texts.Skip(i).Take(batchSize).ToList();
                var batchTasks = batch.Select(text => GenerateEmbeddingAsync(text, cancellationToken));
                var batchResults = await Task.WhenAll(batchTasks);
                results.AddRange(batchResults);

                // Small delay between batches to be kind to the local Ollama server
                if (i + batchSize < texts.Count)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaEmbedding] Generated {results.Count} embeddings for batch of {texts.Count} texts");
            return results;
        }

        public float CalculateCosineSimilarity(float[] vector1, float[] vector2)
        {
            if (vector1 == null || vector2 == null || vector1.Length != vector2.Length)
                return 0f;

            if (vector1.Length == 0)
                return 0f;

            var dotProduct = 0f;
            var magnitude1 = 0f;
            var magnitude2 = 0f;

            for (int i = 0; i < vector1.Length; i++)
            {
                dotProduct += vector1[i] * vector2[i];
                magnitude1 += vector1[i] * vector1[i];
                magnitude2 += vector2[i] * vector2[i];
            }

            magnitude1 = (float)Math.Sqrt(magnitude1);
            magnitude2 = (float)Math.Sqrt(magnitude2);

            if (magnitude1 == 0f || magnitude2 == 0f)
                return 0f;

            var similarity = dotProduct / (magnitude1 * magnitude2);
            
            // Ensure result is between 0 and 1
            return Math.Max(0f, Math.Min(1f, similarity));
        }
    }
}