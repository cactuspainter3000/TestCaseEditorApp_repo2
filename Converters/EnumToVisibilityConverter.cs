using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TestCaseEditorApp.Converters
{
    /// <summary>
    /// Converter that shows/hides elements based on enum value comparison
    /// </summary>
    public class EnumToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Visibility.Collapsed;

            // Convert both to strings for comparison
            var enumValue = value.ToString();
            var targetValue = parameter.ToString();

            return string.Equals(enumValue, targetValue, StringComparison.OrdinalIgnoreCase) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("ConvertBack is not supported for EnumToVisibilityConverter");
        }
    }
}