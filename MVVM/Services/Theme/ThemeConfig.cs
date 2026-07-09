using System.Windows.Media;

namespace TestCaseEditorApp.MVVM.Services.Theme
{
    /// <summary>
    /// Centralized theme configuration for consistent color management across the app.
    /// Users can select themes and all views automatically reflect the chosen theme.
    /// </summary>
    public class ThemeConfig
    {
        public string Name { get; set; } = "Dark Orange";
        
        // Primary Colors
        public Color AccentPrimary { get; set; }
        public Color AccentSecondary { get; set; }
        
        // Background Colors
        public Color BackgroundDark { get; set; }      // Main background (#1E1E1E)
        public Color BackgroundMedium { get; set; }    // Secondary background (#2D2D2D)
        public Color BackgroundLight { get; set; }     // Tertiary background (#3A3A3A)
        
        // Border & Divider Colors
        public Color BorderSubtle { get; set; }        // Subtle borders (#555555)
        public Color BorderStrong { get; set; }        // Strong borders (#777777)
        
        // Text Colors
        public Color TextPrimary { get; set; }         // Main text (#DDDDDD)
        public Color TextSecondary { get; set; }       // Secondary text (#BBBBBB)
        public Color TextMuted { get; set; }           // Muted text (#888888)
        
        // Status Colors
        public Color SuccessColor { get; set; }        // Success (#00AA00)
        public Color WarningColor { get; set; }        // Warning (#FF8800)
        public Color ErrorColor { get; set; }          // Error (#DD0000)
        public Color InfoColor { get; set; }           // Info (#0088DD)
        
        public static ThemeConfig DarkOrange()
        {
            return new ThemeConfig
            {
                Name = "Dark Orange",
                AccentPrimary = Color.FromRgb(255, 140, 0),          // Orange
                AccentSecondary = Color.FromRgb(255, 165, 0),        // Lighter orange
                BackgroundDark = Color.FromRgb(30, 30, 30),          // #1E1E1E
                BackgroundMedium = Color.FromRgb(45, 45, 45),        // #2D2D2D
                BackgroundLight = Color.FromRgb(58, 58, 58),         // #3A3A3A
                BorderSubtle = Color.FromRgb(85, 85, 85),            // #555555
                BorderStrong = Color.FromRgb(119, 119, 119),         // #777777
                TextPrimary = Color.FromRgb(221, 221, 221),          // #DDDDDD
                TextSecondary = Color.FromRgb(187, 187, 187),        // #BBBBBB
                TextMuted = Color.FromRgb(136, 136, 136),            // #888888
                SuccessColor = Color.FromRgb(0, 170, 0),             // #00AA00
                WarningColor = Color.FromRgb(255, 136, 0),           // #FF8800
                ErrorColor = Color.FromRgb(221, 0, 0),               // #DD0000
                InfoColor = Color.FromRgb(0, 136, 221),              // #0088DD
            };
        }

        public static ThemeConfig DarkBlue()
        {
            return new ThemeConfig
            {
                Name = "Dark Blue",
                AccentPrimary = Color.FromRgb(0, 136, 221),          // Blue
                AccentSecondary = Color.FromRgb(0, 170, 255),        // Lighter blue
                BackgroundDark = Color.FromRgb(30, 30, 30),
                BackgroundMedium = Color.FromRgb(45, 45, 45),
                BackgroundLight = Color.FromRgb(58, 58, 58),
                BorderSubtle = Color.FromRgb(85, 85, 85),
                BorderStrong = Color.FromRgb(119, 119, 119),
                TextPrimary = Color.FromRgb(221, 221, 221),
                TextSecondary = Color.FromRgb(187, 187, 187),
                TextMuted = Color.FromRgb(136, 136, 136),
                SuccessColor = Color.FromRgb(0, 170, 0),
                WarningColor = Color.FromRgb(255, 136, 0),
                ErrorColor = Color.FromRgb(221, 0, 0),
                InfoColor = Color.FromRgb(0, 136, 221),
            };
        }

        public static ThemeConfig DarkPurple()
        {
            return new ThemeConfig
            {
                Name = "Dark Purple",
                AccentPrimary = Color.FromRgb(170, 0, 255),          // Purple
                AccentSecondary = Color.FromRgb(200, 100, 255),      // Lighter purple
                BackgroundDark = Color.FromRgb(30, 30, 30),
                BackgroundMedium = Color.FromRgb(45, 45, 45),
                BackgroundLight = Color.FromRgb(58, 58, 58),
                BorderSubtle = Color.FromRgb(85, 85, 85),
                BorderStrong = Color.FromRgb(119, 119, 119),
                TextPrimary = Color.FromRgb(221, 221, 221),
                TextSecondary = Color.FromRgb(187, 187, 187),
                TextMuted = Color.FromRgb(136, 136, 136),
                SuccessColor = Color.FromRgb(0, 170, 0),
                WarningColor = Color.FromRgb(255, 136, 0),
                ErrorColor = Color.FromRgb(221, 0, 0),
                InfoColor = Color.FromRgb(0, 136, 221),
            };
        }

        public static ThemeConfig DarkGreen()
        {
            return new ThemeConfig
            {
                Name = "Dark Green",
                AccentPrimary = Color.FromRgb(0, 170, 85),           // Green
                AccentSecondary = Color.FromRgb(0, 200, 120),        // Lighter green
                BackgroundDark = Color.FromRgb(30, 30, 30),
                BackgroundMedium = Color.FromRgb(45, 45, 45),
                BackgroundLight = Color.FromRgb(58, 58, 58),
                BorderSubtle = Color.FromRgb(85, 85, 85),
                BorderStrong = Color.FromRgb(119, 119, 119),
                TextPrimary = Color.FromRgb(221, 221, 221),
                TextSecondary = Color.FromRgb(187, 187, 187),
                TextMuted = Color.FromRgb(136, 136, 136),
                SuccessColor = Color.FromRgb(0, 170, 0),
                WarningColor = Color.FromRgb(255, 136, 0),
                ErrorColor = Color.FromRgb(221, 0, 0),
                InfoColor = Color.FromRgb(0, 136, 221),
            };
        }
    }
}
