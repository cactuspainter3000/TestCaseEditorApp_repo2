using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TestCaseEditorApp.Converters
{
    public class BadgeToVisibilityConverter : IValueConverter
    {
        // If true, show Collapsed when badge is null/empty/zero; otherwise Visible.
        // Keep this simple and robust to different input types (string, int, long).
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // Null -> Collapsed
            if (value is null) return Visibility.Collapsed;

            // If string: consider whitespace-only as empty as well
            if (value is string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return Visibility.Collapsed;

                // If string contains a number, treat "0" or "0.0" as empty
                if (decimal.TryParse(s.Trim(), NumberStyles.Number, culture, out var num))
                {
                    return num == 0m ? Visibility.Collapsed : Visibility.Visible;
                }

                // Non-numeric non-empty string -> Visible
                return Visibility.Visible;
            }

            // If numeric types: 0 -> Collapsed, otherwise Visible
            if (value is int i) return i == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (value is long l) return l == 0L ? Visibility.Collapsed : Visibility.Visible;
            if (value is short sh) return sh == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (value is byte b) return b == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (value is decimal dec) return dec == 0m ? Visibility.Collapsed : Visibility.Visible;
            if (value is double d) return d == 0.0 ? Visibility.Collapsed : Visibility.Visible;
            if (value is float f) return f == 0f ? Visibility.Collapsed : Visibility.Visible;

            // Fallback: treat other non-null values as Visible
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}