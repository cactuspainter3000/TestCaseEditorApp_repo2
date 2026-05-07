using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// OCR (Optical Character Recognition) result containing extracted text and metadata
    /// </summary>
    public class OCRResult
    {
        /// <summary>
        /// Raw extracted text from the image
        /// </summary>
        public string ExtractedText { get; set; } = string.Empty;
        
        /// <summary>
        /// Confidence score from 0.0 to 1.0 (if available)
        /// </summary>
        public double Confidence { get; set; } = 0.0;
        
        /// <summary>
        /// Detected tabular data if image contains structured tables
        /// </summary>
        public List<List<string>> TableData { get; set; } = new List<List<string>>();
        
        /// <summary>
        /// Any errors or warnings during OCR processing
        /// </summary>
        public string? ErrorMessage { get; set; }
        
        /// <summary>
        /// Whether OCR processing was successful
        /// </summary>
        public bool Success => string.IsNullOrEmpty(ErrorMessage);
    }

    /// <summary>
    /// Service interface for Optical Character Recognition (OCR) operations
    /// </summary>
    public interface IOCRService
    {
        /// <summary>
        /// Extract text from an image using OCR
        /// </summary>
        /// <param name="imageData">Image bytes</param>
        /// <param name="fileName">Original filename for context (optional)</param>
        /// <returns>OCR result with extracted text and metadata</returns>
        Task<OCRResult> ExtractTextFromImageAsync(byte[] imageData, string? fileName = null);
        
        /// <summary>
        /// Extract text from an image file using OCR
        /// </summary>
        /// <param name="imagePath">Path to image file</param>
        /// <returns>OCR result with extracted text and metadata</returns>
        Task<OCRResult> ExtractTextFromImageFileAsync(string imagePath);
        
        /// <summary>
        /// Check if OCR is available on this system
        /// </summary>
        /// <returns>True if OCR can be used</returns>
        Task<bool> IsOCRAvailableAsync();
    }
}