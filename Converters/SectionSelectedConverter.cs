using System;
using System.Globalization;
using System.Windows.Data;

namespace TestCaseEditorApp.Converters
{
    /// <summary>
    /// Two-way converter that maps a string "selected section key" to a ToggleButton.IsChecked boolean.
    /// ConverterParameter should be the section key (e.g. "TestCase", "TestFlow").
    /// When IsChecked -> true the converter returns the parameter string;
    /// when IsChecked -> false the converter returns null.
    /// </summary>
    public class SectionSelectedConverter : IValueConverter
    {
        // value: SelectedMenuSection (string?)
        // parameter: the key for this ToggleButton (string)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var selectedKey = value as string;
            var param = parameter as string;
            return string.Equals(selectedKey, param, StringComparison.Ordinal);
        }

        // value: ToggleButton.IsChecked (bool)
        // parameter: the key for this ToggleButton (string)
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
            {
                return parameter as string ?? string.Empty;
            }

            // When unchecked, clear selection (no section open) — return empty string rather than null to satisfy nullability
            return string.Empty;
        }
    }
}