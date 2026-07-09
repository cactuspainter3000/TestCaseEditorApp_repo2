using System;
using System.Globalization;
using System.Windows.Data;

namespace TestCaseEditorApp.Converters
{
    /// <summary>
    /// Converts an enum value to a boolean for use with ToggleButton IsChecked binding.
    /// Usage: IsChecked="{Binding SelectedTab, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=MainTab}"
    /// </summary>
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string parameterString = parameter.ToString() ?? "";
            
            // Try to convert both to string for comparison
            string valueString = value.ToString() ?? "";
            
            return valueString.Equals(parameterString, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not bool boolValue || parameter == null)
                return Binding.DoNothing;

            if (!boolValue)
                return Binding.DoNothing;

            // If the value is true, return the enum value specified in the parameter
            if (Enum.TryParse(targetType, parameter.ToString(), true, out var result))
                return result;

            return Binding.DoNothing;
        }
    }
}
