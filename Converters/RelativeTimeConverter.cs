using System;
using System.Globalization;
using System.Windows.Data;

namespace TestCaseEditorApp.Converters
{
    public class RelativeTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            if (value is DateTime dt)
            {
                var span = DateTime.UtcNow - dt.ToUniversalTime();

                if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds}s ago";
                if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
                if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
                if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
                if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";
                return $"{(int)(span.TotalDays / 365)}y ago";
            }

            // If it's nullable DateTime (DateTime?) passed boxed
            if (value is DateTime?)
            {
                var ndt = (DateTime?)value;
                if (!ndt.HasValue) return string.Empty;
                return Convert(ndt.Value, targetType, parameter, culture);
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}