using System;
using System.Globalization;
using System.Windows.Data;

namespace TestCaseEditorApp.Converters
{
    /// <summary>
    /// Converter that adds 1 to an integer value.
    /// Used to convert 0-based index to 1-based display index.
    /// </summary>
    public class AddOneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue + 1;
            }
            
            if (value is null)
            {
                return 1;
            }

            // Try to parse as int
            if (int.TryParse(value.ToString(), out var parsedValue))
            {
                return parsedValue + 1;
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue - 1;
            }

            if (int.TryParse(value?.ToString(), out var parsedValue))
            {
                return parsedValue - 1;
            }

            return value;
        }
    }
}