using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TestCaseEditorApp.Converters
{
    public class LevelToBackgroundConverter : IValueConverter
    {
        public static readonly LevelToBackgroundConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int level)
            {
                return level switch
                {
                    1 => new SolidColorBrush(Color.FromRgb(0, 122, 204)), // #007acc
                    2 => new SolidColorBrush(Color.FromRgb(32, 144, 224)), // Lighter blue
                    3 => new SolidColorBrush(Color.FromRgb(64, 166, 244)), // Even lighter blue  
                    4 => new SolidColorBrush(Color.FromRgb(96, 188, 255)), // Lightest blue
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class LevelToMarginConverter : IValueConverter
    {
        public static readonly LevelToMarginConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int level)
            {
                var leftMargin = (level - 1) * 16;
                return new Thickness(leftMargin + 8, 4, 8, 4);
            }
            return new Thickness(8, 4, 8, 4);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class LevelToFontSizeConverter : IValueConverter
    {
        public static readonly LevelToFontSizeConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int level)
            {
                return level switch
                {
                    1 => 16.0,
                    2 => 14.0,
                    3 => 12.0,
                    4 => 11.0,
                    _ => 12.0
                };
            }
            return 12.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}