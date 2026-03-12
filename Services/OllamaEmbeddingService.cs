// Services/OllamaEmbeddingService.cs
using System;
using System.Collections.Concurrent;
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
        private static readonly ConcurrentDictionary<string, int> _modelMaxChars = new();
        private int _calibratedMaxChars = 0;

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

            // Use calibrated value if available, otherwise fall back to conservative default
            int maxChars = _calibratedMaxChars > 0 ? _calibratedMaxChars : 750;
            
            if (text.Length > maxChars)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[OllamaEmbedding] Truncating text from {text.Length} to {maxChars} chars (calibrated: {_calibratedMaxChars > 0})");
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

        public async Task<int> CalibrateMaxInputSizeAsync(string sampleText = null, CancellationToken cancellationToken = default)
        {
            // Check cache first
            if (_modelMaxChars.TryGetValue(_embeddingModel, out int cached))
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaEmbedding] Using cached calibration for {_embeddingModel}: {cached} chars");
                _calibratedMaxChars = cached;
                return cached;
            }

            if (!string.IsNullOrWhiteSpace(sampleText))
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaEmbedding] 🔬 Calibrating with real document samples for {_embeddingModel}...");
                return await CalibrateWithRealSamplesAsync(sampleText, cancellationToken);
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaEmbedding] 🔬 Calibrating maximum input size for {_embeddingModel} (synthetic test)...");
            
            // Binary search to find actual limit
            int low = 100, high = 2000;
            int maxWorking = 100;
            int attempts = 0;
            
            while (low <= high && attempts < 15) // Limit attempts to prevent infinite loops
            {
                attempts++;
                int mid = (low + high) / 2;
                // Use realistic technical text for calibration (part numbers, compound terms, special chars)
                string testText = new string('x', mid / 4) + "946-1UE3-001 " + new string('A', mid / 4) + 
                                 "Input/Output " + new string('B', mid / 4) + "IEEE-1149.1 " + new string('C', mid / 4);
                
                try
                {
                    // Temporarily bypass truncation for calibration
                    var payload = new
                    {
                        model = _embeddingModel,
                        input = testText
                    };

                    var json = JsonSerializer.Serialize(payload);
                    using var response = await _http.PostAsync("api/embed",
                        new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        maxWorking = mid;
                        low = mid + 1;  // Try larger
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Calibration] {mid} chars: ✅ SUCCESS (attempt {attempts})");
                    }
                    else
                    {
                        high = mid - 1;  // Try smaller
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Calibration] {mid} chars: ❌ FAILED - HTTP {(int)response.StatusCode} (attempt {attempts})");
                    }
                }
                catch (HttpRequestException)
                {
                    high = mid - 1;  // Try smaller
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Calibration] {mid} chars: ❌ FAILED - Exception (attempt {attempts})");
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[Calibration] Unexpected error at {mid} chars: {ex.Message}");
                    high = mid - 1;
                }

                // Small delay between attempts to be kind to Ollama
                await Task.Delay(50, cancellationToken);
            }
            
            // Use 70% of discovered limit for safety margin (accounts for technical text tokenization variations)
            int safeLimit = (int)(maxWorking * 0.7);
            _modelMaxChars[_embeddingModel] = safeLimit;
            _calibratedMaxChars = safeLimit;
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaEmbedding] ✅ Calibration complete for {_embeddingModel}: {safeLimit} chars safe limit (discovered max: {maxWorking} chars, {attempts} attempts)");
            return safeLimit;
        }

        /// <summary>
        /// Calibrate using real document samples for accurate tokenization measurement
        /// </summary>
        private async Task<int> CalibrateWithRealSamplesAsync(string documentText, CancellationToken cancellationToken)
        {
            // Extract 3 samples from different parts of the document
            var samples = new List<string>();
            int docLength = documentText.Length;
            int sampleSize = Math.Min(2000, docLength);

            // Sample from start
            samples.Add(documentText.Substring(0, Math.Min(sampleSize, docLength)));
            
            // Sample from middle (if doc is long enough)
            if (docLength > sampleSize * 2)
            {
                int midStart = (docLength - sampleSize) / 2;
                samples.Add(documentText.Substring(midStart, sampleSize));
            }
            
            // Sample from end (if doc is long enough)
            if (docLength > sampleSize)
            {
                int endStart = Math.Max(0, docLength - sampleSize);
                samples.Add(documentText.Substring(endStart));
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[OllamaEmbedding] Testing {samples.Count} document samples for calibration");

            // Test each sample and find the most conservative limit
            int safestLimit = int.MaxValue;
            int totalAttempts = 0;

            for (int sampleIdx = 0; sampleIdx < samples.Count; sampleIdx++)
            {
                var sample = samples[sampleIdx];
                int low = 100, high = Math.Min(2000, sample.Length);
                int maxWorking = 100;
                int attempts = 0;

                TestCaseEditorApp.Services.Logging.Log.Debug($"[Calibration] Testing sample {sampleIdx + 1}/{samples.Count} ({sample.Length} chars available)");

                while (low <= high && attempts < 12) // Fewer attempts per sample
                {
                    attempts++;
                    totalAttempts++;
                    int mid = (low + high) / 2;

                    // Use actual document text up to mid characters
                    string testText = sample.Substring(0, Math.Min(mid, sample.Length));

                    try
                    {
                        var payload = new { model = _embeddingModel, input = testText };
                        var json = JsonSerializer.Serialize(payload);
                        using var response = await _http.PostAsync("api/embed",
                            new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);

                        if (response.IsSuccessStatusCode)
                        {
                            maxWorking = mid;
                            low = mid + 1;
                            TestCaseEditorApp.Services.Logging.Log.Debug($"[Calibration] Sample {sampleIdx + 1}, {mid} chars: ✅ SUCCESS");
                        }
                        else
                        {
                            high = mid - 1;
                            TestCaseEditorApp.Services.Logging.Log.Debug($"[Calibration] Sample {sampleIdx + 1}, {mid} chars: ❌ HTTP {(int)response.StatusCode}");
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                    {
                        high = mid - 1;
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Calibration] Sample {sampleIdx + 1}, {mid} chars: ❌ {ex.GetType().Name}");
                    }

                    await Task.Delay(50, cancellationToken);
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[Calibration] Sample {sampleIdx + 1} result: {maxWorking} chars maximum");
                safestLimit = Math.Min(safestLimit, maxWorking);
            }

            // Use 50% safety margin (conservative for document variance)
            int finalLimit = (int)(safestLimit * 0.5);
            _modelMaxChars[_embeddingModel] = finalLimit;
            _calibratedMaxChars = finalLimit;

            TestCaseEditorApp.Services.Logging.Log.Info(
                $"[OllamaEmbedding] ✅ Sample-based calibration complete for {_embeddingModel}: {finalLimit} chars safe limit " +
                $"(safest sample: {safestLimit} chars, {samples.Count} samples, {totalAttempts} total attempts)");

            return finalLimit;
        }
    }
}