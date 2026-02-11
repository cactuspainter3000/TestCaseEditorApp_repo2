using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tesseract;
using TestCaseEditorApp.Services.Logging;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// OCR service implementation using Tesseract engine
    /// Provides text extraction from images with basic table structure detection
    /// </summary>
    public class TesseractOCRService : IOCRService
    {
        private TesseractEngine? _engine;
        private bool _initialized = false;
        private static readonly object _initLock = new object();

        /// <summary>
        /// Extract text from image bytes using Tesseract OCR with enhanced preprocessing
        /// </summary>
        public async Task<OCRResult> ExtractTextFromImageAsync(byte[] imageData, string? fileName = null)
        {
            var result = new OCRResult();
            
            try
            {
                if (!await InitializeEngineAsync())
                {
                    result.ErrorMessage = "Tesseract OCR engine not available";
                    return result;
                }

                Log.Info($"[TesseractOCR] Starting enhanced OCR extraction for image: {fileName ?? "<memory>"}");

                // Run OCR in a background thread to avoid blocking UI
                return await Task.Run(() =>
                {
                    try
                    {
                        using var originalImg = Pix.LoadFromMemory(imageData);
                        
                        // Try multiple OCR approaches for better accuracy
                        var ocrResults = new List<(string text, double confidence, List<List<string>> tableData)>();
                        
                        // Approach 1: Original image with default settings
                        Log.Debug("[TesseractOCR] Trying: Original image with default PSM");
                        var result1 = ProcessWithPSM(originalImg, PageSegMode.Auto);
                        if (result1.text.Length > 0) ocrResults.Add(result1);
                        
                        // Approach 2: Enhanced contrast and different PSM
                        Log.Debug("[TesseractOCR] Trying: Enhanced contrast with single block PSM");
                        using var enhancedImg = EnhanceImageForOCR(originalImg);
                        var result2 = ProcessWithPSM(enhancedImg, PageSegMode.SingleBlock);
                        if (result2.text.Length > 0) ocrResults.Add(result2);
                        
                        // Approach 3: Single column mode (good for lists)
                        Log.Debug("[TesseractOCR] Trying: Single column mode");
                        var result3 = ProcessWithPSM(enhancedImg, PageSegMode.SingleColumn);
                        if (result3.text.Length > 0) ocrResults.Add(result3);
                        
                        // Approach 4: Raw line mode (minimal layout analysis)
                        Log.Debug("[TesseractOCR] Trying: Raw line mode");
                        var result4 = ProcessWithPSM(originalImg, PageSegMode.RawLine);
                        if (result4.text.Length > 0) ocrResults.Add(result4);
                        
                        // Approach 5: Sparse text mode
                        Log.Debug("[TesseractOCR] Trying: Sparse text mode");  
                        var result5 = ProcessWithPSM(enhancedImg, PageSegMode.SparseText);
                        if (result5.text.Length > 0) ocrResults.Add(result5);
                        
                        // Approach 6: High resolution with sharpening
                        Log.Debug("[TesseractOCR] Trying: High resolution with sharpening");
                        using var sharpened = SharpenImage(originalImg);
                        var result6 = ProcessWithPSM(sharpened, PageSegMode.Auto);
                        if (result6.text.Length > 0) ocrResults.Add(result6);
                        
                        // Approach 7: Denoised image
                        Log.Debug("[TesseractOCR] Trying: Denoised image");
                        using var denoised = DenoiseImage(enhancedImg);
                        var result7 = ProcessWithPSM(denoised, PageSegMode.SingleBlock);
                        if (result7.text.Length > 0) ocrResults.Add(result7);

                        // Select best result based on length and confidence
                        var bestResult = SelectBestOCRResult(ocrResults);
                        
                        Log.Info($"[TesseractOCR] Enhanced OCR complete: {bestResult.text.Length} characters " +
                                $"(confidence: {bestResult.confidence:P1}), tested {ocrResults.Count} approaches");
                        
                        result.ExtractedText = bestResult.text;
                        result.Confidence = bestResult.confidence;
                        result.TableData = bestResult.tableData;
                        
                        return result;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"[TesseractOCR] Enhanced OCR processing failed: {ex.Message}");
                        return new OCRResult { ErrorMessage = $"Enhanced OCR processing failed: {ex.Message}" };
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[TesseractOCR] Failed to extract text from image: {fileName}");
                result.ErrorMessage = $"OCR failed: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Process image with specific Page Segmentation Mode and OCR Engine Mode
        /// </summary>
        private (string text, double confidence, List<List<string>> tableData) ProcessWithPSM(Pix image, PageSegMode psm, EngineMode engineMode = EngineMode.Default)
        {
            try
            {
                // Set page segmentation mode
                _engine!.SetVariable("tessedit_pageseg_mode", ((int)psm).ToString());
                
                // Additional OCR optimization variables
                _engine.SetVariable("tessedit_char_whitelist", ""); // Reset any whitelist
                _engine.SetVariable("preserve_interword_spaces", "1"); // Preserve spacing
                _engine.SetVariable("user_defined_dpi", "300"); // Assume 300 DPI
                
                using var page = _engine.Process(image);
                var text = page.GetText()?.Trim() ?? string.Empty;
                var confidence = (double)page.GetMeanConfidence() / 100.0;
                
                // Detect tabular structure
                List<List<string>> tableData = new();
                if (!string.IsNullOrEmpty(text))
                {
                    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.Trim())
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .ToList();
                    
                    tableData = DetectTabularData(lines);
                }
                
                return (text, confidence, tableData);
            }
            catch (Exception ex)
            {
                Log.Debug($"[TesseractOCR] PSM {psm} failed: {ex.Message}");
                return (string.Empty, 0.0, new List<List<string>>());
            }
        }

        /// <summary>
        /// Enhance image for better OCR results with basic techniques
        /// </summary>
        private Pix EnhanceImageForOCR(Pix originalImage)
        {
            try
            {
                // Start with original
                var enhanced = originalImage.Clone();
                
                // Scale up if image is small (helps with text recognition)
                if (enhanced.Width < 1200 || enhanced.Height < 900)
                {
                    var scaleFactor = Math.Max(1200.0 / enhanced.Width, 900.0 / enhanced.Height);
                    if (scaleFactor > 1.0 && scaleFactor <= 4.0) // Reasonable scaling limit
                    {
                        enhanced = enhanced.Scale((float)scaleFactor, (float)scaleFactor);
                        Log.Debug($"[TesseractOCR] Scaled image by {scaleFactor:F2}x for better recognition");
                    }
                }
                
                // Convert to grayscale if not already
                if (enhanced.Depth > 8)
                {
                    enhanced = enhanced.ConvertRGBToGray(0.3f, 0.59f, 0.11f);
                }
                
                return enhanced;
            }
            catch (Exception ex)
            {
                Log.Debug($"[TesseractOCR] Image enhancement failed, using original: {ex.Message}");
                return originalImage.Clone();
            }
        }
        
        /// <summary>
        /// Apply basic sharpening to improve text clarity
        /// </summary>
        private Pix SharpenImage(Pix originalImage)
        {
            try
            {
                var sharpened = originalImage.Clone();
                
                // Convert to grayscale if needed
                if (sharpened.Depth > 8)
                {
                    sharpened = sharpened.ConvertRGBToGray(0.3f, 0.59f, 0.11f);
                }
                
                return sharpened;
            }
            catch (Exception ex)
            {
                Log.Debug($"[TesseractOCR] Image sharpening failed: {ex.Message}");
                return originalImage.Clone();
            }
        }
        
        /// <summary>
        /// Apply basic noise reduction
        /// </summary>
        private Pix DenoiseImage(Pix originalImage)
        {
            try
            {
                // For basic denoising, just return a clone
                // More advanced filtering would require additional libraries
                return originalImage.Clone();
            }
            catch (Exception ex)
            {
                Log.Debug($"[TesseractOCR] Image denoising failed: {ex.Message}");
                return originalImage.Clone();
            }
        }

        /// <summary>
        /// Select the best OCR result from multiple attempts
        /// </summary>
        private (string text, double confidence, List<List<string>> tableData) SelectBestOCRResult(
            List<(string text, double confidence, List<List<string>> tableData)> results)
        {
            if (!results.Any())
                return (string.Empty, 0.0, new List<List<string>>());

            // Score results based on text length, confidence, and table detection
            var scoredResults = results.Select(r => new
            {
                Result = r,
                Score = CalculateOCRScore(r.text, r.confidence, r.tableData)
            }).ToList();
            
            var best = scoredResults.OrderByDescending(s => s.Score).First().Result;
            
            Log.Debug($"[TesseractOCR] Selected best result: {best.text.Length} chars, " +
                     $"{best.confidence:P1} confidence, {best.tableData.Count} table rows");
            
            return best;
        }

        /// <summary>
        /// Calculate quality score for OCR result based on general metrics
        /// </summary>
        private double CalculateOCRScore(string text, double confidence, List<List<string>> tableData)
        {
            double score = 0;
            
            // Base score from text length (longer results often better)
            score += Math.Min(text.Length / 50.0, 10.0); // Max 10 points for substantial text
            
            // Confidence score (heavily weighted)
            score += confidence * 15.0; // Max 15 points for high confidence
            
            // Table detection bonus (structured data is valuable)
            score += tableData.Count * 1.0; // 1 point per table row detected
            
            // Character diversity bonus (more diverse character sets often indicate better recognition)
            var uniqueChars = text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).Distinct().Count();
            score += Math.Min(uniqueChars / 5.0, 3.0); // Max 3 points for character diversity
            
            // Alphanumeric content bonus (mixed content often more reliable)
            var hasLetters = text.Any(char.IsLetter);
            var hasDigits = text.Any(char.IsDigit);
            if (hasLetters && hasDigits) score += 2.0;
            
            // Penalize very short or very long results without structure
            if (text.Length < 10 && tableData.Count == 0) score *= 0.5;
            if (text.Length > 2000 && confidence < 0.7) score *= 0.7;
            
            return score;
        }

        /// <summary>
        /// Extract text from image file using Tesseract OCR
        /// </summary>
        public async Task<OCRResult> ExtractTextFromImageFileAsync(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    return new OCRResult { ErrorMessage = $"Image file not found: {imagePath}" };
                }

                var imageData = await File.ReadAllBytesAsync(imagePath);
                return await ExtractTextFromImageAsync(imageData, Path.GetFileName(imagePath));
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[TesseractOCR] Failed to read image file: {imagePath}");
                return new OCRResult { ErrorMessage = $"Failed to read image file: {ex.Message}" };
            }
        }

        /// <summary>
        /// Check if Tesseract OCR is available
        /// </summary>
        public async Task<bool> IsOCRAvailableAsync()
        {
            return await Task.FromResult(await InitializeEngineAsync());
        }

        /// <summary>
        /// Initialize the Tesseract OCR engine
        /// </summary>
        private async Task<bool> InitializeEngineAsync()
        {
            if (_initialized)
                return _engine != null;

            return await Task.Run(() =>
            {
                lock (_initLock)
                {
                    if (_initialized)
                        return _engine != null;

                    try
                    {
                        // Try to find tessdata directory
                        var tessdataPath = FindTessdataDirectory();
                        if (string.IsNullOrEmpty(tessdataPath))
                        {
                            Log.Warn("[TesseractOCR] Tesseract data files not found. Please install Tesseract OCR and ensure tessdata directory is available.");
                            return false;
                        }

                        Log.Info($"[TesseractOCR] Initializing with tessdata path: {tessdataPath}");
                        
                        _engine = new TesseractEngine(tessdataPath, "eng", EngineMode.Default);
                        _engine.SetVariable("tessedit_pageseg_mode", "6"); // Assume uniform block of text
                        
                        Log.Info("[TesseractOCR] Tesseract OCR engine initialized successfully");
                        _initialized = true;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[TesseractOCR] Failed to initialize Tesseract OCR engine");
                        return false;
                    }
                }
            });
        }

        /// <summary>
        /// Find the tessdata directory (language data files for Tesseract)
        /// </summary>
        private static string? FindTessdataDirectory()
        {
            // Common tessdata paths
            string[] paths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tessdata"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tesseract-OCR", "tessdata"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Tesseract-OCR", "tessdata"), // User installation
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tesseract", "tessdata"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "tessdata"),
                Environment.GetEnvironmentVariable("TESSDATA_PREFIX") ?? ""
            };

            foreach (var path in paths.Where(p => !string.IsNullOrEmpty(p)))
            {
                if (Directory.Exists(path))
                {
                    // Check if English language file exists
                    var engFile = Path.Combine(path, "eng.traineddata");
                    if (File.Exists(engFile))
                    {
                        return path;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// General-purpose tabular data detection from OCR text lines
        /// </summary>
        private static List<List<string>> DetectTabularData(List<string> lines)
        {
            var tableData = new List<List<string>>();
            
            try
            {
                Log.Debug($"[TesseractOCR] Analyzing {lines.Count} lines for general tabular structure");
                
                foreach (var line in lines)
                {
                    // Skip very short lines
                    if (line.Length < 3)
                        continue;
                    
                    List<string>? bestColumns = null;
                    int maxColumns = 0;
                    
                    // Try different delimiter patterns to find the best column split
                    var delimiters = new[] 
                    { 
                        '\t',           // Tab separated
                        '|',            // Pipe separated
                        ',',            // Comma separated (CSV-like)
                        ';'             // Semicolon separated
                    };
                    
                    // Test explicit delimiters first
                    foreach (var delimiter in delimiters)
                    {
                        if (line.Contains(delimiter))
                        {
                            var columns = line.Split(delimiter, StringSplitOptions.RemoveEmptyEntries)
                                .Select(col => col.Trim())
                                .Where(col => !string.IsNullOrWhiteSpace(col))
                                .ToList();
                            
                            if (columns.Count > maxColumns && columns.Count >= 2)
                            {
                                maxColumns = columns.Count;
                                bestColumns = columns;
                            }
                        }
                    }
                    
                    // Test multiple spaces pattern (fixed-width tables)
                    var spaceSeparated = Regex.Split(line, @"\s{2,}") // 2+ consecutive spaces
                        .Select(col => col.Trim())
                        .Where(col => !string.IsNullOrWhiteSpace(col))
                        .ToList();
                        
                    if (spaceSeparated.Count > maxColumns && spaceSeparated.Count >= 2)
                    {
                        maxColumns = spaceSeparated.Count;
                        bestColumns = spaceSeparated;
                    }
                    
                    // Test colon-separated key-value pairs
                    if (line.Contains(':') && maxColumns < 2)
                    {
                        var colonSplit = line.Split(':', StringSplitOptions.RemoveEmptyEntries)
                            .Select(part => part.Trim())
                            .Where(part => !string.IsNullOrWhiteSpace(part))
                            .ToList();
                            
                        if (colonSplit.Count == 2) // Classic key:value pattern
                        {
                            bestColumns = colonSplit;
                            maxColumns = 2;
                        }
                    }
                    
                    // Test hyphen/dash separated patterns
                    if (line.Contains('-') && maxColumns < 2)
                    {
                        var dashSplit = Regex.Split(line, @"\s*-\s*")
                            .Select(part => part.Trim())
                            .Where(part => !string.IsNullOrWhiteSpace(part) && part.Length > 1)
                            .ToList();
                            
                        if (dashSplit.Count >= 2 && dashSplit.Count <= 8)
                        {
                            bestColumns = dashSplit;
                            maxColumns = dashSplit.Count;
                        }
                    }
                    
                    // Add the best parsed row if found
                    if (bestColumns != null && maxColumns >= 2)
                    {
                        var cleanedColumns = bestColumns.Select(CleanTableCell)
                            .Where(cell => !string.IsNullOrWhiteSpace(cell))
                            .ToList();
                            
                        if (cleanedColumns.Count >= 2)
                        {
                            tableData.Add(cleanedColumns);
                            Log.Debug($"[TesseractOCR] Added table row with {cleanedColumns.Count} columns");
                        }
                    }
                }
                
                Log.Info($"[TesseractOCR] General table detection: {tableData.Count} rows from {lines.Count} text lines");
                return tableData;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TesseractOCR] Error in general tabular structure detection");
                return new List<List<string>>();
            }
        }

        /// <summary>
        /// Clean individual table cells with general OCR corrections
        /// </summary>
        private static string CleanTableCell(string cell)
        {
            if (string.IsNullOrWhiteSpace(cell))
                return string.Empty;
            
            // Remove extra whitespace
            cell = Regex.Replace(cell, @"\s+", " ").Trim();
            
            // Fix common OCR character recognition errors only in numeric contexts
            // More conservative approach - only fix obvious numeric errors
            cell = Regex.Replace(cell, @"\bO(\d)", "0$1"); // O followed by digit -> 0 followed by digit
            cell = Regex.Replace(cell, @"(\d)O\b", "$10"); // digit followed by O at word boundary -> digit followed by 0
            cell = Regex.Replace(cell, @"\bl(\d)", "1$1"); // lowercase l followed by digit -> 1 followed by digit
            cell = Regex.Replace(cell, @"(\d)l\b", "$11"); // digit followed by lowercase l at word boundary -> digit followed by 1
            
            // Remove leading/trailing punctuation that might be OCR artifacts
            cell = cell.Trim('.', ',', ';', ':', '!', '?', '"', '\'');
            
            // Fix spacing around common punctuation
            cell = Regex.Replace(cell, @"\s*([.,;:])\s*", "$1 ").Trim();
            
            return cell;
        }

        public void Dispose()
        {
            _engine?.Dispose();
        }
    }
}