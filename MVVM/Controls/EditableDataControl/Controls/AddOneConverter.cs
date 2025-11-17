using System.Globalization;
using System.Windows.Data;

namespace EditableDataControl.Controls
{
    // Adds 1 to an integer (used for row header badges)
    public sealed class AddOneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i) return i + 1;
            if (value is long l) return l + 1;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}



