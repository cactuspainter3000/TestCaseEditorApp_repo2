using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TestCaseEditorApp.Converters
{
    [ValueConversion(typeof(bool), typeof(double))]
    public class BoolToAngleConverter : IValueConverter
    {
        // Convert bool -> numeric angle (true -> 180, false -> 0)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = false;

            // This pattern matches a boxed bool and also a boxed Nullable<bool> with a value.
            if (value is bool b)
            {
                flag = b;
            }
            else if (value is string s && bool.TryParse(s, out var parsed))
            {
                flag = parsed;
            }
            else
            {
                // If value is null or not a bool/string, default to false.
                flag = false;
            }

            double angle = flag ? 180.0 : 0.0;

            if (targetType == null || targetType == typeof(object) || targetType == typeof(double))
                return angle;

            try
            {
                return System.Convert.ChangeType(angle, targetType, culture);
            }
            catch
            {
                return DependencyProperty.UnsetValue;
            }
        }

        // ConvertBack from numeric angle -> bool
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double angle;

            if (value is double d) angle = d;
            else if (value is float f) angle = f;
            else if (value is int i) angle = i;
            else if (value is string s && double.TryParse(s, NumberStyles.Any, culture, out var pd)) angle = pd;
            else
                return DependencyProperty.UnsetValue;

            return Math.Abs(angle - 180.0) < 0.1;
        }
    }
}