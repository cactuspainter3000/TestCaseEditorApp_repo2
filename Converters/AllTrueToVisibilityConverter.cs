using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TestCaseEditorApp.Converters
{
    // Returns Visible if every input value is boolean true; otherwise Collapsed.
    // Use with MultiBinding when you want "A && B && C" => Visible.
    public class AllTrueToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length == 0) return Visibility.Collapsed;

            foreach (var v in values)
            {
                if (v is bool b)
                {
                    if (!b) return Visibility.Collapsed;
                }
                else
                {
                    // treat null / non-bool as false
                    return Visibility.Collapsed;
                }
            }

            return Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}