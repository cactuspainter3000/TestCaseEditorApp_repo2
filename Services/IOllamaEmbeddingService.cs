// Services/IOllamaEmbeddingService.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Interface for Ollama embedding generation service
    /// </summary>
    public interface IOllamaEmbeddingService
    {
        /// <summary>
        /// Generate embedding vector for a single text
        /// </summary>
        /// <param name="text">Text to embed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Embedding vector as float array</returns>
        Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generate embedding vectors for multiple texts in batch
        /// </summary>
        /// <param name="texts">Texts to embed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of embedding vectors</returns>
        Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);

        /// <summary>
        /// Calculate cosine similarity between two embedding vectors
        /// </summary>
        /// <param name="vector1">First embedding vector</param>
        /// <param name="vector2">Second embedding vector</param>
        /// <returns>Cosine similarity score (0-1, higher is more similar)</returns>
        float CalculateCosineSimilarity(float[] vector1, float[] vector2);

        /// <summary>
        /// Get the embedding model being used
        /// </summary>
        string EmbeddingModel { get; }

        /// <summary>
        /// Get the dimensions of the embedding vectors produced by this model
        /// </summary>
        int EmbeddingDimensions { get; }
    }
}