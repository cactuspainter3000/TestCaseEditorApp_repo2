using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TestCaseEditorApp.Converters
{
    /// <summary>
    /// Converts IsMainMenuItem boolean to appropriate Thickness for margin
    /// Main menu items: 0,6,0,0 (standard top margin)
    /// Sub menu items: 8,4,0,0 (indented with smaller top margin)
    /// </summary>
    public class IsMainMenuItemToMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isMainMenuItem)
            {
                return isMainMenuItem 
                    ? new Thickness(8, 6, 0, 0)   // Main menu item - base indent (Space.SM)
                    : new Thickness(24, 4, 0, 0); // Sub menu item - larger indent (Space.XL)
            }
            return new Thickness(8, 6, 0, 0); // Default to main menu margin
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("ConvertBack not supported for IsMainMenuItemToMarginConverter");
        }
    }
}