using System.Globalization;
using System.Windows;
using System.Windows.Data;
using EditableDataControl.ViewModels;

namespace TestCaseEditorApp.Converters
{

    /// <summary>Inverts a boolean value.</summary>
    public sealed class InvertBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : value;
        }
    }

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
        public bool CollapseWhenFalse { get; set; } = true;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isVisible = value is bool b && b;
            
            // Check for inversion from property or parameter
            bool shouldInvert = Invert;
            if (parameter != null && parameter.ToString()?.Equals("Inverted", StringComparison.OrdinalIgnoreCase) == true)
            {
                shouldInvert = true;
            }
            
            if (shouldInvert) isVisible = !isVisible;
            
            if (isVisible) return Visibility.Visible;
            return CollapseWhenFalse ? Visibility.Collapsed : Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Inverted bool to visibility converter - true=hidden, false=visible</summary>
    public class InvertedBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not bool isVisible) return Visibility.Collapsed;
            return !isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Converts zero/non-zero values to visibility - zero=collapsed, non-zero=visible</summary>
    public class ZeroToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isZero = false;
            
            if (value is int intVal)
                isZero = intVal == 0;
            else if (value is double doubleVal)
                isZero = Math.Abs(doubleVal) < 0.001;
            else if (value is long longVal)
                isZero = longVal == 0;
            else if (value is decimal decimalVal)
                isZero = decimalVal == 0;
            else if (value == null)
                isZero = true;
            else
                isZero = false;
                
            var shouldShow = !isZero;
            if (Invert) shouldShow = !shouldShow;
            
            return shouldShow ? Visibility.Visible : Visibility.Collapsed;
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
            var result = visible ? Visibility.Visible : Visibility.Collapsed;
            
            // Enhanced debug logging for SuggestedEdit binding issues
            if (value is string str && (str.Contains("DECAGON") || str.Length > 20))
            {
                System.Diagnostics.Debug.WriteLine($"[NullOrEmptyConverter] SuggestedEdit - Value='{str?.Substring(0, Math.Min(80, str?.Length ?? 0))}...', IsNullOrEmpty={isNullOrEmpty}, Result={result}, Invert={Invert}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[NullOrEmptyConverter] SuggestedEdit visibility: {result} for content length {str?.Length}");
            }
            
            return result;
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
            var result = visible ? Visibility.Visible : Visibility.Collapsed;
            System.Diagnostics.Debug.WriteLine($"[NullToVisibilityConverter] value={value ?? "NULL"}, isNull={isNull}, visible={visible}, result={result}");
            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>String not empty to visibility converter - shows when string has content</summary>
    public class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var hasContent = value != null && !string.IsNullOrWhiteSpace(value.ToString());
            return hasContent ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>String empty to visibility converter - shows when string is null/empty</summary>
    public class StringEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isEmpty = value == null || string.IsNullOrWhiteSpace(value.ToString());
            return isEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts bool to string based on parameter format: "TrueValue|FalseValue"
    /// </summary>
    public class BoolToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is string param && param.Contains("|"))
            {
                var parts = param.Split('|');
                var trueValue = parts[0];
                var falseValue = parts.Length > 1 ? parts[1] : string.Empty;
                
                return value is bool b && b ? trueValue : falseValue;
            }
            
            return value is bool boolValue && boolValue ? "True" : "False";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Inverts a boolean value
    /// </summary>
    public class BoolToInvertedBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : false;
        }
    }
}