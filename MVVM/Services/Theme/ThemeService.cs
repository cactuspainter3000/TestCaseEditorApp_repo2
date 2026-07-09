using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace TestCaseEditorApp.MVVM.Services.Theme
{
    /// <summary>
    /// Service that manages the current theme and notifies views of changes.
    /// </summary>
    public partial class ThemeService : ObservableObject
    {
        [ObservableProperty]
        private ThemeConfig currentTheme;

        private readonly Dictionary<string, ThemeConfig> _availableThemes;

        public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

        public ThemeService()
        {
            _availableThemes = new Dictionary<string, ThemeConfig>
            {
                { "Dark Orange", ThemeConfig.DarkOrange() },
                { "Dark Blue", ThemeConfig.DarkBlue() },
                { "Dark Purple", ThemeConfig.DarkPurple() },
                { "Dark Green", ThemeConfig.DarkGreen() },
            };

            // Default to Dark Orange
            CurrentTheme = _availableThemes["Dark Orange"];
        }

        public IEnumerable<ThemeConfig> GetAvailableThemes()
        {
            return _availableThemes.Values;
        }

        public void SetTheme(string themeName)
        {
            if (_availableThemes.TryGetValue(themeName, out var theme))
            {
                CurrentTheme = theme;
                ThemeChanged?.Invoke(this, new ThemeChangedEventArgs { NewTheme = theme });
            }
        }

        partial void OnCurrentThemeChanged(ThemeConfig value)
        {
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs { NewTheme = value });
        }
    }

    public class ThemeChangedEventArgs : EventArgs
    {
        public ThemeConfig NewTheme { get; set; } = null!;
    }
}
