using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TestCaseEditorApp.MVVM.Models;

public static class DefaultsHelper
{
    public static DefaultsCatalogDto LoadProjectDefaultsTemplate()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Config", "defaults.catalog.template.json");
            System.Diagnostics.Debug.WriteLine($"[Defaults] looking for: {path}  exists={File.Exists(path)}");

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var dto = JsonSerializer.Deserialize<DefaultsCatalogDto>(json, opts);

                // 👇 log what we actually deserialized
                System.Diagnostics.Debug.WriteLine($"[Defaults] template deserialized: items={dto?.Items?.Count ?? 0}, presets={dto?.Presets?.Count ?? 0}");

                if (dto?.Items?.Count > 0)
                    return dto; // 👈 early return (we saw content)
            }
        }
        catch
        {
            // swallow; we'll fall back below
        }

        // 👇 log that we’re using the hardcoded starter
        System.Diagnostics.Debug.WriteLine("[Defaults] using starter fallback (template missing/empty/corrupt).");

        return new DefaultsCatalogDto
        {
            Items = new List<DefaultItem>
        {
            new() { Key="env_ambient25", Name="Ambient 25 °C", Description="Bench tests at 25 °C ±5 °C.", ContentLine="Testing performed at ambient 25 °C ± 5 °C." },
            new() { Key="tools_cal_12mo", Name="Tools Calibrated (12 mo)", Description="Traceable calibration.", ContentLine="All test instruments calibrated within the last 12 months, traceable to NIST." }
        },
            Presets = new List<DefaultPreset>
        {
            new() { Name="Bench (default)", OnKeys = new[] { "env_ambient25", "tools_cal_12mo" } }
        }
        };
    }

    /// <summary>
    /// Load verification method definitions for LLM context.
    /// Returns a dictionary mapping VerificationMethod enum values to their test case writing guidance.
    /// </summary>
    public static Dictionary<string, string> LoadVerificationMethodDefinitions()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Config", "verification-methods.json");
            System.Diagnostics.Debug.WriteLine($"[VerificationMethods] looking for: {path}  exists={File.Exists(path)}");

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var wrapper = JsonSerializer.Deserialize<VerificationMethodsWrapper>(json, opts);

                if (wrapper?.VerificationMethods != null && wrapper.VerificationMethods.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[VerificationMethods] loaded {wrapper.VerificationMethods.Count} definitions");
                    return wrapper.VerificationMethods;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VerificationMethods] error loading: {ex.Message}");
        }

        // Fallback to empty dictionary
        System.Diagnostics.Debug.WriteLine("[VerificationMethods] using empty fallback");
        return new Dictionary<string, string>();
    }

    /// <summary>
    /// Get the verification method definition for a specific method.
    /// Returns null if not found.
    /// </summary>
    public static string? GetVerificationMethodDefinition(VerificationMethod method)
    {
        var definitions = LoadVerificationMethodDefinitions();
        var key = method.ToString();
        return definitions.ContainsKey(key) ? definitions[key] : null;
    }

    private class VerificationMethodsWrapper
    {
        public Dictionary<string, string> VerificationMethods { get; set; } = new();
    }

    /// <summary>
    /// Load user-defined custom instructions for LLM per verification method.
    /// Returns a dictionary mapping verification method names to custom instruction text.
    /// </summary>
    public static Dictionary<string, string> LoadUserInstructions()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Config", "user-instructions.json");
            System.Diagnostics.Debug.WriteLine($"[UserInstructions] looking for: {path}  exists={File.Exists(path)}");

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var wrapper = JsonSerializer.Deserialize<UserInstructionsWrapper>(json, opts);

                if (wrapper?.Instructions != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[UserInstructions] loaded {wrapper.Instructions.Count} instruction sets");
                    return wrapper.Instructions;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserInstructions] error loading: {ex.Message}");
        }

        // Fallback: return empty dictionary
        System.Diagnostics.Debug.WriteLine("[UserInstructions] using empty fallback (file missing or invalid)");
        return new Dictionary<string, string>
        {
            { "Analysis", "" },
            { "Test", "" },
            { "Demonstration", "" },
            { "Inspection", "" },
            { "Similarity", "" }
        };
    }

    /// <summary>
    /// Get custom instructions for a specific verification method.
    /// Returns empty string if not found or not set.
    /// </summary>
    public static string GetUserInstructions(VerificationMethod method)
    {
        var instructions = LoadUserInstructions();
        var key = method.ToString();
        return instructions.ContainsKey(key) ? instructions[key] ?? "" : "";
    }

    /// <summary>
    /// Save user-defined custom instructions for LLM per verification method.
    /// </summary>
    public static void SaveUserInstructions(Dictionary<string, string> instructions)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Config", "user-instructions.json");
            var wrapper = new UserInstructionsWrapper { Instructions = instructions };
            var opts = new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(wrapper, opts);
            
            // Ensure directory exists
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, json);
            System.Diagnostics.Debug.WriteLine($"[UserInstructions] saved to: {path}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserInstructions] error saving: {ex.Message}");
        }
    }

    private class UserInstructionsWrapper
    {
        public Dictionary<string, string> Instructions { get; set; } = new();
    }

}


