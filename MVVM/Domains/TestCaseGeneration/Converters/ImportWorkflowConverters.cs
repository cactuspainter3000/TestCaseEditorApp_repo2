using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Converters
{
    /// <summary>
    /// Converter that returns different colors based on whether a value is null/empty.
    /// ConverterParameter format: "ColorIfNotNull|ColorIfNull" (e.g., "#CCCCCC|#AAAAAA")
    /// </summary>
    public class NullToColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isNullOrEmpty = value == null || 
                               (value is string str && string.IsNullOrWhiteSpace(str));

            if (parameter is string paramStr && paramStr.Contains('|'))
            {
                var colors = paramStr.Split('|');
                var colorToUse = isNullOrEmpty ? colors[1] : colors[0];
                
                try
                {
                    return new BrushConverter().ConvertFromString(colorToUse);
                }
                catch
                {
                    // Fallback to default colors
                }
            }

            // Default behavior
            return isNullOrEmpty ? 
                new SolidColorBrush(Colors.Gray) : 
                new SolidColorBrush(Colors.White);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter that inverts a boolean and converts to Visibility (true -> Collapsed, false -> Visible)
    /// </summary>
    public class InvertBoolToVisConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Collapsed;
            }
            return false;
        }
    }

    /// <summary>
    /// Converter for CheckBox styling with orange accent
    /// </summary>
    public class OrangeCheckBoxStyleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // This would typically return a Style resource
            // For now, just return the value as-is
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}