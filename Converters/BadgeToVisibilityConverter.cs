using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TestCaseEditorApp.Converters
{
    /// <summary>
    /// Converts a badge value (string or numeric) to Visibility.
    /// Collapses when null/empty or equal to "0" or numeric zero.
    /// </summary>
    public class BadgeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Visibility.Collapsed;

            // numeric types
            if (value is int i) return i > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is long l) return l > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is short s) return s > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is byte b) return b > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is decimal dec) return dec > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is double dbl) return dbl > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is float f) return f > 0 ? Visibility.Visible : Visibility.Collapsed;

            // strings: treat "0" (or whitespace/empty) as not visible
            if (value is string str)
            {
                if (string.IsNullOrWhiteSpace(str)) return Visibility.Collapsed;
                if (str.Trim() == "0") return Visibility.Collapsed;
                return Visibility.Visible;
            }

            // fallback: show for other object types
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}