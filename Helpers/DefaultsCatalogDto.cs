using System;
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

}

