namespace TestCaseEditorApp.Services
{
    public sealed class AppUserSettings
    {
        public string JamaBaseUrl { get; set; } = string.Empty;
        public string JamaClientId { get; set; } = string.Empty;
        public string JamaClientSecret { get; set; } = string.Empty;
        public string JamaProjectId { get; set; } = string.Empty;

        public string AnythingLlmBaseUrl { get; set; } = "http://localhost:3001";
        public string AnythingLlmApiKey { get; set; } = string.Empty;
        public string OllamaChatModel { get; set; } = "phi4-mini:latest";
        public string OllamaEmbeddingModel { get; set; } = "nomic-embed-text:latest";
        public string ThemeName { get; set; } = "Dark Orange";

        public bool EnableRequirementsAnalysisSnapshot { get; set; } = false;

        /// <summary>
        /// Enable the AnythingLLM fallback path in Jama attachment extraction.
        /// When false, DirectRAG/template extraction remains enabled, but the parser will not
        /// fall back to AnythingLLM for unsupported or unsuitable document types.
        /// </summary>
        public bool EnableAnythingLlmFallback { get; set; } = true;

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
