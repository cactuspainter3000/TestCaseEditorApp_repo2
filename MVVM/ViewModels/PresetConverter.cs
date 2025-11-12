using System;
using System.Collections.Generic;
using System.Linq;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Convert DefaultPreset (old shape) to Preset + Chip instances for the UI.
    /// Honors EnabledKeys to set initial Chip.IsEnabled state.
    /// </summary>
    public static class PresetConverter
    {
        public static IEnumerable<Preset> ConvertDefaults(IEnumerable<DefaultPreset>? defaults)
        {
            if (defaults == null) yield break;

            foreach (var d in defaults)
            {
                var preset = new Preset(d.Name)
                {
                    Description = null
                };

                var onKeys = d.OnKeys ?? Array.Empty<string>();
                var enabled = new HashSet<string>(d.EnabledKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

                foreach (var key in onKeys)
                {
                    var chip = new Chip(key)
                    {
                        IsEnabled = enabled.Contains(key)
                    };
                    preset.Items.Add(chip);
                }

                yield return preset;
            }
        }
    }
}