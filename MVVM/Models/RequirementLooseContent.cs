using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Unstructured content associated with a requirement.
    /// </summary>
    public partial class RequirementLooseContent : ObservableObject
    {
        [ObservableProperty]
        private List<string> paragraphs = new();

        [ObservableProperty]
        private List<LooseTable> tables = new();

        [ObservableProperty]
        private string? cleanedDescription;

        [ObservableProperty]
        private List<ExtractedImageText> extractedImageTexts = new();
    }

    /// <summary>
    /// Text extracted from images via OCR
    /// </summary>
    public class ExtractedImageText
    {
        /// <summary>
        /// Source image filename or URL
        /// </summary>
        public string ImageSource { get; set; } = string.Empty;

        /// <summary>
        /// Raw text extracted from the image
        /// </summary>
        public string ExtractedText { get; set; } = string.Empty;

        /// <summary>
        /// OCR confidence score (0.0 to 1.0)
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Structured table data if detected in the image
        /// </summary>
        public List<LooseTable> DetectedTables { get; set; } = new List<LooseTable>();
    }
}
