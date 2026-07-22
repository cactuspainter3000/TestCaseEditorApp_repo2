using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TestCaseEditorApp.Services
{
    [SupportedOSPlatform("windows")]
    public class UserSettingsService : IUserSettingsService
    {
        private const string RegistryPath = @"SOFTWARE\TestCaseEditorApp\UserSettings";
        private const string LegacyAnythingLlmRegistryPath = @"SOFTWARE\TestCaseEditorApp\AnythingLLM";
        private const string HardcodedJamaProjectId = "686";

        public AppUserSettings LoadSettings()
        {
            var settings = AppUserSettings.Empty();

            try
            {
                Logging.Log.Info("[UserSettings] Loading settings from registry.");
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key != null)
                {
                    settings.JamaBaseUrl = ReadString(key, "JamaBaseUrl");
                    settings.JamaClientId = ReadString(key, "JamaClientId");
                    settings.JamaClientSecret = DecryptString(ReadString(key, "JamaClientSecretEnc"));
                    settings.JamaProjectId = ReadString(key, "JamaProjectId");
                    settings.AnythingLlmBaseUrl = ReadString(key, "AnythingLlmBaseUrl", "http://localhost:3001");
                    settings.AnythingLlmApiKey = DecryptString(ReadString(key, "AnythingLlmApiKeyEnc"));
                    settings.OllamaChatModel = ReadString(key, "OllamaChatModel", "phi4-mini:latest");
                    settings.OllamaEmbeddingModel = ReadString(key, "OllamaEmbeddingModel", "nomic-embed-text:latest");
                    settings.ThemeName = ReadString(key, "ThemeName", "Dark Orange");
                    settings.EnableRequirementsAnalysisSnapshot = ReadBool(key, "EnableRequirementsAnalysisSnapshot", false);
                    settings.EnableAnythingLlmFallback = ReadBool(key, "EnableAnythingLlmFallback", true);

                    Logging.Log.Info($"[UserSettings] Loaded OllamaChatModel: {settings.OllamaChatModel}, OllamaEmbeddingModel: {settings.OllamaEmbeddingModel}");
                }

                if (string.IsNullOrWhiteSpace(settings.AnythingLlmApiKey))
                {
                    var envKey = Environment.GetEnvironmentVariable("ANYTHINGLM_API_KEY");
                    if (!string.IsNullOrWhiteSpace(envKey))
                    {
                        settings.AnythingLlmApiKey = envKey.Trim();
                    }
                    else
                    {
                        using var legacyKey = Registry.CurrentUser.OpenSubKey(LegacyAnythingLlmRegistryPath);
                        settings.AnythingLlmApiKey = ReadString(legacyKey, "ApiKey");
                    }
                }

                settings.JamaBaseUrl = FirstNonEmpty(settings.JamaBaseUrl, Environment.GetEnvironmentVariable("JAMA_BASE_URL"));
                settings.JamaClientId = FirstNonEmpty(settings.JamaClientId, Environment.GetEnvironmentVariable("JAMA_CLIENT_ID"));
                settings.JamaClientSecret = FirstNonEmpty(settings.JamaClientSecret, Environment.GetEnvironmentVariable("JAMA_CLIENT_SECRET"));
                settings.JamaProjectId = HardcodedJamaProjectId;
                settings.AnythingLlmBaseUrl = FirstNonEmpty(settings.AnythingLlmBaseUrl, Environment.GetEnvironmentVariable("ANYTHINGLLM_ENDPOINT"), "http://localhost:3001");
                settings.OllamaChatModel = FirstNonEmpty(settings.OllamaChatModel, Environment.GetEnvironmentVariable("OLLAMA_MODEL"), "phi4-mini:latest");
                settings.OllamaEmbeddingModel = FirstNonEmpty(settings.OllamaEmbeddingModel, Environment.GetEnvironmentVariable("OLLAMA_EMBEDDING_MODEL"), "nomic-embed-text:latest");
                settings.ThemeName = FirstNonEmpty(settings.ThemeName, "Dark Orange");

                var enableFallbackEnv = Environment.GetEnvironmentVariable("ENABLE_ANYTHINGLLM_FALLBACK");
                if (bool.TryParse(enableFallbackEnv, out var enableFallback))
                {
                    settings.EnableAnythingLlmFallback = enableFallback;
                }
            }
            catch (Exception ex)
            {
                Logging.Log.Warn($"[UserSettings] Failed to load settings: {ex.Message}");
            }

            return settings;
        }

        public void SaveSettings(AppUserSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            Logging.Log.Info("[UserSettings] Saving settings to registry.");
            Logging.Log.Info($"[UserSettings] Saving OllamaChatModel: {settings.OllamaChatModel}, OllamaEmbeddingModel: {settings.OllamaEmbeddingModel}");

            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            if (key == null)
            {
                throw new InvalidOperationException("Unable to open user settings registry key.");
            }

            key.SetValue("JamaBaseUrl", (settings.JamaBaseUrl ?? string.Empty).Trim());
            key.SetValue("JamaClientId", (settings.JamaClientId ?? string.Empty).Trim());
            key.SetValue("JamaClientSecretEnc", EncryptString((settings.JamaClientSecret ?? string.Empty).Trim()));
            key.SetValue("JamaProjectId", (settings.JamaProjectId ?? string.Empty).Trim(), RegistryValueKind.String);
            key.SetValue("AnythingLlmBaseUrl", NormalizeUrl(settings.AnythingLlmBaseUrl, "http://localhost:3001"));
            key.SetValue("AnythingLlmApiKeyEnc", EncryptString((settings.AnythingLlmApiKey ?? string.Empty).Trim()));
            key.SetValue("OllamaChatModel", FirstNonEmpty(settings.OllamaChatModel, "phi4-mini:latest"));
            key.SetValue("OllamaEmbeddingModel", FirstNonEmpty(settings.OllamaEmbeddingModel, "nomic-embed-text:latest"));
            key.SetValue("ThemeName", FirstNonEmpty(settings.ThemeName, "Dark Orange"));
            key.SetValue("EnableRequirementsAnalysisSnapshot", settings.EnableRequirementsAnalysisSnapshot ? 1 : 0);
            key.SetValue("EnableAnythingLlmFallback", settings.EnableAnythingLlmFallback ? 1 : 0);

            using var legacyKey = Registry.CurrentUser.CreateSubKey(LegacyAnythingLlmRegistryPath);
            legacyKey?.SetValue("ApiKey", (settings.AnythingLlmApiKey ?? string.Empty).Trim());
        }

        public void ApplySettingsToEnvironment(AppUserSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            Environment.SetEnvironmentVariable("JAMA_BASE_URL", (settings.JamaBaseUrl ?? string.Empty).Trim(), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("JAMA_CLIENT_ID", (settings.JamaClientId ?? string.Empty).Trim(), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("JAMA_CLIENT_SECRET", (settings.JamaClientSecret ?? string.Empty).Trim(), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("JAMA_PROJECT_ID", (settings.JamaProjectId ?? string.Empty).Trim(), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("ANYTHINGLM_API_KEY", (settings.AnythingLlmApiKey ?? string.Empty).Trim(), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("ANYTHINGLLM_ENDPOINT", NormalizeUrl(settings.AnythingLlmBaseUrl, "http://localhost:3001"), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("OLLAMA_MODEL", FirstNonEmpty(settings.OllamaChatModel, "phi4-mini:latest"), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("OLLAMA_EMBEDDING_MODEL", FirstNonEmpty(settings.OllamaEmbeddingModel, "nomic-embed-text:latest"), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("ENABLE_ANYTHINGLLM_FALLBACK", settings.EnableAnythingLlmFallback ? "true" : "false", EnvironmentVariableTarget.Process);
        }

        public bool HasMissingRequiredSettings()
        {
            var settings = LoadSettings();
            return !settings.HasRequiredConfiguration();
        }

        private static string NormalizeUrl(string? value, string fallback)
        {
            var normalized = FirstNonEmpty(value, fallback);
            return normalized.TrimEnd('/');
        }

        private static string ReadString(RegistryKey? key, string name, string fallback = "")
        {
            if (key == null) return fallback;
            var rawValue = key.GetValue(name);
            var value = rawValue as string ?? Convert.ToString(rawValue, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static bool ReadBool(RegistryKey? key, string name, bool fallback = false)
        {
            if (key == null) return fallback;
            var value = key.GetValue(name);
            if (value is int intValue)
            {
                return intValue != 0;
            }
            if (value is long longValue)
            {
                return longValue != 0;
            }
            if (value is string stringValue)
            {
                if (bool.TryParse(stringValue, out var boolValue))
                {
                    return boolValue;
                }

                if (int.TryParse(stringValue, out var parsedInt))
                {
                    return parsedInt != 0;
                }
            }
            return fallback;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;

            var bytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        private static string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return string.Empty;

            try
            {
                var bytes = Convert.FromBase64String(cipherText);
                var unprotectedBytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(unprotectedBytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}