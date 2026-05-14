using System;

namespace TestCaseEditorApp.Services
{
    public sealed class AppUserSettings
    {
        public string JamaBaseUrl { get; set; } = string.Empty;
        public string JamaClientId { get; set; } = string.Empty;
        public string JamaClientSecret { get; set; } = string.Empty;

        public string AnythingLlmBaseUrl { get; set; } = "http://localhost:3001";
        public string AnythingLlmApiKey { get; set; } = string.Empty;

        public string OllamaChatModel { get; set; } = "phi4-mini:latest";
        public string OllamaEmbeddingModel { get; set; } = "nomic-embed-text:latest";

        /// <summary>
        /// Enable requirements analysis snapshot logging for diagnostics.
        /// When true, all snapshot capture calls will execute; when false, they are skipped.
        /// Default is false for production; set to true during development/debugging.
        /// </summary>
        public bool EnableRequirementsAnalysisSnapshot { get; set; } = false;

        public bool HasRequiredConfiguration()
        {
            return !string.IsNullOrWhiteSpace(JamaBaseUrl)
                && !string.IsNullOrWhiteSpace(JamaClientId)
                && !string.IsNullOrWhiteSpace(JamaClientSecret)
                && !string.IsNullOrWhiteSpace(AnythingLlmBaseUrl)
                && !string.IsNullOrWhiteSpace(AnythingLlmApiKey)
                && !string.IsNullOrWhiteSpace(OllamaChatModel)
                && !string.IsNullOrWhiteSpace(OllamaEmbeddingModel);
        }

        public static AppUserSettings Empty()
        {
            return new AppUserSettings();
        }
    }
}
