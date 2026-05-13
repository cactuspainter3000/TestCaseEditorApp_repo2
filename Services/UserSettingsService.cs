using System;
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

        public AppUserSettings LoadSettings()
        {
            var settings = AppUserSettings.Empty();

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key != null)
                {
                    settings.JamaBaseUrl = ReadString(key, "JamaBaseUrl");
                    settings.JamaClientId = ReadString(key, "JamaClientId");
                    settings.JamaClientSecret = DecryptString(ReadString(key, "JamaClientSecretEnc"));
                    settings.AnythingLlmBaseUrl = ReadString(key, "AnythingLlmBaseUrl", "http://localhost:3001");
                    settings.AnythingLlmApiKey = DecryptString(ReadString(key, "AnythingLlmApiKeyEnc"));
                    settings.OllamaChatModel = ReadString(key, "OllamaChatModel", "phi4-mini");
                    settings.OllamaEmbeddingModel = ReadString(key, "OllamaEmbeddingModel", "nomic-embed-text");
                    settings.EnableRequirementsAnalysisSnapshot = ReadBool(key, "EnableRequirementsAnalysisSnapshot", false);
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
                settings.AnythingLlmBaseUrl = FirstNonEmpty(settings.AnythingLlmBaseUrl, Environment.GetEnvironmentVariable("ANYTHINGLLM_ENDPOINT"), "http://localhost:3001");
                settings.OllamaChatModel = FirstNonEmpty(settings.OllamaChatModel, Environment.GetEnvironmentVariable("OLLAMA_MODEL"), "phi4-mini");
                settings.OllamaEmbeddingModel = FirstNonEmpty(settings.OllamaEmbeddingModel, Environment.GetEnvironmentVariable("OLLAMA_EMBEDDING_MODEL"), "nomic-embed-text");
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

            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            if (key == null)
            {
                throw new InvalidOperationException("Unable to open user settings registry key.");
            }

            key.SetValue("JamaBaseUrl", (settings.JamaBaseUrl ?? string.Empty).Trim());
            key.SetValue("JamaClientId", (settings.JamaClientId ?? string.Empty).Trim());
            key.SetValue("JamaClientSecretEnc", EncryptString((settings.JamaClientSecret ?? string.Empty).Trim()));
            key.SetValue("AnythingLlmBaseUrl", NormalizeUrl(settings.AnythingLlmBaseUrl, "http://localhost:3001"));
            key.SetValue("AnythingLlmApiKeyEnc", EncryptString((settings.AnythingLlmApiKey ?? string.Empty).Trim()));
            key.SetValue("OllamaChatModel", FirstNonEmpty(settings.OllamaChatModel, "phi4-mini"));
            key.SetValue("OllamaEmbeddingModel", FirstNonEmpty(settings.OllamaEmbeddingModel, "nomic-embed-text"));
            key.SetValue("EnableRequirementsAnalysisSnapshot", settings.EnableRequirementsAnalysisSnapshot ? 1 : 0);

            using var legacyKey = Registry.CurrentUser.CreateSubKey(LegacyAnythingLlmRegistryPath);
            legacyKey?.SetValue("ApiKey", (settings.AnythingLlmApiKey ?? string.Empty).Trim());
        }

        public void ApplySettingsToEnvironment(AppUserSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            Environment.SetEnvironmentVariable("JAMA_BASE_URL", (settings.JamaBaseUrl ?? string.Empty).Trim(), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("JAMA_CLIENT_ID", (settings.JamaClientId ?? string.Empty).Trim(), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("JAMA_CLIENT_SECRET", (settings.JamaClientSecret ?? string.Empty).Trim(), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("ANYTHINGLM_API_KEY", (settings.AnythingLlmApiKey ?? string.Empty).Trim(), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("ANYTHINGLLM_ENDPOINT", NormalizeUrl(settings.AnythingLlmBaseUrl, "http://localhost:3001"), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("OLLAMA_MODEL", FirstNonEmpty(settings.OllamaChatModel, "phi4-mini"), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("OLLAMA_EMBEDDING_MODEL", FirstNonEmpty(settings.OllamaEmbeddingModel, "nomic-embed-text"), EnvironmentVariableTarget.Process);
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
            var value = key.GetValue(name) as string;
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
