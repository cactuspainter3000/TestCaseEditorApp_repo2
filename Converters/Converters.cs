using System.Globalization;
using System.Windows;
using System.Windows.Data;
using EditableDataControl.ViewModels;

namespace TestCaseEditorApp.Converters
{

    /// <summary>Returns the cell Value for a given bindingPath key from a TableRowModel.</summary>
    public sealed class RowCellConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // value: TableRowModel ; parameter: string bindingPath/key
            if (value is not TableRowModel row) return string.Empty;
            var key = parameter as string ?? string.Empty;

            var cell = row.Cells?.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal));
            return cell?.Value ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isVisible = value is bool b && b;
            if (Invert) isVisible = !isVisible;
            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class NullOrEmptyToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isNullOrEmpty = value == null || (value is string s && string.IsNullOrWhiteSpace(s));
            var visible = Invert ? isNullOrEmpty : !isNullOrEmpty;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isNull = value == null;
            var visible = Invert ? isNull : !isNull;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}