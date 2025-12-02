using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TestCaseEditorApp.MVVM.Models;

public static class DefaultsHelper
{
    /// <summary>
    /// Convert DefaultItem to AssumptionPill with applicable methods based on category.
    /// </summary>
    public static AssumptionPill ToAssumptionPill(DefaultItem item)
    {
        var applicableMethods = new List<VerificationMethod>();
        
        // Map categories to verification methods
        // Empty list = applicable to all methods
        switch (item.Category?.ToLowerInvariant())
        {
            case "test":
                // Test-specific pills only show for Test method
                applicableMethods.Add(VerificationMethod.Test);
                break;
            
            case "inspection":
                // Inspection pills for Inspection method
                applicableMethods.Add(VerificationMethod.Inspection);
                break;
            
            case "analysis":
                // Analysis-specific pills
                applicableMethods.Add(VerificationMethod.Analysis);
                break;
                
            case "demonstration":
                // Demonstration-specific pills
                applicableMethods.Add(VerificationMethod.Demonstration);
                break;
            
            // Environment, Equipment, Documentation = show for all methods (empty list)
            default:
                // Leave empty - shows for all methods
                break;
        }
        
        return new AssumptionPill
        {
            Key = item.Key,
            Name = item.Name,
            Category = item.Category,
            Description = item.Description,
            ContentLine = item.ContentLine,
            ApplicableMethods = applicableMethods,
            IsEnabled = item.IsEnabled,
            IsLlmSuggested = item.IsLlmSuggested
        };
    }

    public static DefaultsCatalogDto LoadProjectDefaultsTemplate()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Config", "defaults.catalog.template.json");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Defaults] looking for: {path}  exists={File.Exists(path)}");

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var dto = JsonSerializer.Deserialize<DefaultsCatalogDto>(json, opts);

                // 👇 log what we actually deserialized
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Defaults] template deserialized: items={dto?.Items?.Count ?? 0}, presets={dto?.Presets?.Count ?? 0}");

                if (dto?.Items?.Count > 0)
                    return dto; // 👈 early return (we saw content)
            }
        }
        catch
        {
            // swallow; we'll fall back below
        }

        // 👇 log that we’re using the hardcoded starter
        TestCaseEditorApp.Services.Logging.Log.Debug("[Defaults] using starter fallback (template missing/empty/corrupt).");

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
            TestCaseEditorApp.Services.Logging.Log.Debug($"[VerificationMethods] looking for: {path}  exists={File.Exists(path)}");

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var wrapper = JsonSerializer.Deserialize<VerificationMethodsWrapper>(json, opts);

                if (wrapper?.VerificationMethods != null && wrapper.VerificationMethods.Count > 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[VerificationMethods] loaded {wrapper.VerificationMethods.Count} definitions");
                    return wrapper.VerificationMethods;
                }
            }
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[VerificationMethods] error loading: {ex.Message}");
        }

        // Fallback to empty dictionary
            TestCaseEditorApp.Services.Logging.Log.Debug("[VerificationMethods] using empty fallback");
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
            TestCaseEditorApp.Services.Logging.Log.Debug($"[UserInstructions] looking for: {path}  exists={File.Exists(path)}");

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var wrapper = JsonSerializer.Deserialize<UserInstructionsWrapper>(json, opts);

                if (wrapper?.Instructions != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[UserInstructions] loaded {wrapper.Instructions.Count} instruction sets");
                    return wrapper.Instructions;
                }
            }
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[UserInstructions] error loading: {ex.Message}");
        }

        // Fallback: return empty dictionary
            TestCaseEditorApp.Services.Logging.Log.Debug("[UserInstructions] using empty fallback (file missing or invalid)");
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
            TestCaseEditorApp.Services.Logging.Log.Debug($"[UserInstructions] saved to: {path}");
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[UserInstructions] error saving: {ex.Message}");
        }
    }

    private class UserInstructionsWrapper
    {
        public Dictionary<string, string> Instructions { get; set; } = new();
    }

    /// <summary>
    /// Load user's pill selections per verification method.
    /// Returns a dictionary mapping verification method names to lists of enabled pill keys.
    /// </summary>
    public static Dictionary<string, List<string>> LoadPillSelections()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Config", "pill-selections.json");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[PillSelections] looking for: {path}  exists={File.Exists(path)}");

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var wrapper = JsonSerializer.Deserialize<PillSelectionsWrapper>(json, opts);

                if (wrapper?.Selections != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[PillSelections] loaded {wrapper.Selections.Count} method selections");
                    return wrapper.Selections;
                }
            }
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[PillSelections] error loading: {ex.Message}");
        }

        // Fallback: return empty dictionary
        TestCaseEditorApp.Services.Logging.Log.Debug("[PillSelections] using empty fallback (file missing or invalid)");
        return new Dictionary<string, List<string>>();
    }

    /// <summary>
    /// Save user's pill selections per verification method.
    /// </summary>
    public static void SavePillSelections(Dictionary<string, List<string>> selections)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Config", "pill-selections.json");
            var wrapper = new PillSelectionsWrapper { Selections = selections };
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
            TestCaseEditorApp.Services.Logging.Log.Debug($"[PillSelections] saved {selections.Count} method selections to: {path}");
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[PillSelections] error saving: {ex.Message}");
        }
    }

    private class PillSelectionsWrapper
    {
        public Dictionary<string, List<string>> Selections { get; set; } = new();
    }

}


