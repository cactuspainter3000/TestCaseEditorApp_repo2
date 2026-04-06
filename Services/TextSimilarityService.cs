using System;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Implementation of text similarity service using Levenshtein distance
    /// Calculates percentage of changes between original and modified text
    /// </summary>
    public class TextSimilarityService : ITextSimilarityService
    {
        public double CalculateSimilarityPercentage(string originalText, string modifiedText)
        {
            if (string.IsNullOrEmpty(originalText) && string.IsNullOrEmpty(modifiedText))
                return 100.0; // Both empty = identical

            if (string.IsNullOrEmpty(originalText) || string.IsNullOrEmpty(modifiedText))
                return 0.0; // One empty, one not = completely different

            // Normalize whitespace for comparison
            var original = NormalizeText(originalText);
            var modified = NormalizeText(modifiedText);

            if (original == modified)
                return 100.0; // Identical after normalization

            var maxLength = Math.Max(original.Length, modified.Length);
            var distance = CalculateLevenshteinDistance(original, modified);
            
            // Convert distance to similarity percentage
            var similarity = ((double)(maxLength - distance) / maxLength) * 100.0;
            return Math.Max(0.0, similarity);
        }

        public bool ExceedsChangeThreshold(string originalText, string modifiedText, double threshold = 15.0)
        {
            var similarity = CalculateSimilarityPercentage(originalText, modifiedText);
            var changePercentage = 100.0 - similarity;
            
            return changePercentage > threshold;
        }

        /// <summary>
        /// Normalize text for comparison by trimming and standardizing whitespace
        /// </summary>
        private static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Trim and normalize multiple spaces/newlines to single spaces
            return System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
        }

        /// <summary>
        /// Calculate Levenshtein distance between two strings
        /// </summary>
        private static int CalculateLevenshteinDistance(string source, string target)
        {
            if (source.Length == 0) return target.Length;
            if (target.Length == 0) return source.Length;

            var matrix = new int[source.Length + 1, target.Length + 1];

            // Initialize first column and row
            for (int i = 0; i <= source.Length; i++)
                matrix[i, 0] = i;
            for (int j = 0; j <= target.Length; j++)
                matrix[0, j] = j;

            // Fill in the rest of the matrix
            for (int i = 1; i <= source.Length; i++)
            {
                for (int j = 1; j <= target.Length; j++)
                {
                    var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[source.Length, target.Length];
        }
    }
}