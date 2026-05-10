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

        public string OllamaChatModel { get; set; } = "phi4-mini";
        public string OllamaEmbeddingModel { get; set; } = "nomic-embed-text";

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
