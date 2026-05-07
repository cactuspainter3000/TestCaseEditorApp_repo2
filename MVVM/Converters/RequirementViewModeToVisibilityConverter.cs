using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using TestCaseEditorApp.MVVM.Domains.Requirements.Enums;

namespace TestCaseEditorApp.MVVM.Converters
{
    /// <summary>
    /// Converts RequirementViewMode enum values to Visibility for conditional content display.
    /// Used by UnifiedRequirementsMainView to show/hide content based on selected view mode.
    /// </summary>
    public class RequirementViewModeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RequirementViewMode currentMode && parameter is RequirementViewMode targetMode)
            {
                return currentMode == targetMode ? Visibility.Visible : Visibility.Collapsed;
            }

            // Handle string parameter conversion (from XAML static extension)
            if (value is RequirementViewMode currentModeFromValue && parameter is string parameterString)
            {
                if (Enum.TryParse<RequirementViewMode>(parameterString, out var targetModeFromString))
                {
                    return currentModeFromValue == targetModeFromString ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("RequirementViewModeToVisibilityConverter is one-way only");
        }
    }
}