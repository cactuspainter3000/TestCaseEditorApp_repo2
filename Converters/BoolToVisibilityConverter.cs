using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TestCaseEditorApp.MVVM.Converters
{
    /// <summary>
    /// Converts bool/nullable bool to Visibility.
    /// Set CollapseWhenFalse = false if you want Hidden instead of Collapsed.
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool CollapseWhenFalse { get; set; } = true;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Boxed Nullable<bool> with a value is boxed as bool, and a null Nullable<bool> is boxed as null.
            // So checking "is bool b" covers both plain bool and nullable bool with a value.
            bool isVisible = false;
            if (value is bool b)
            {
                isVisible = b;
            }
            else
            {
                // Any non-bool (including null) is treated as false.
                isVisible = false;
            }

            if (isVisible) return Visibility.Visible;
            return CollapseWhenFalse ? Visibility.Collapsed : Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v) return v == Visibility.Visible;
            return false;
        }
    }
}