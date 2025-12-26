using System;
using System.Globalization;
using System.Windows.Data;

namespace TestCaseEditorApp.MVVM.Controls.DataDrivenMenu;

/// <summary>
/// Converts menu item level to appropriate font size for hierarchy visualization.
/// Level 0: 14px (main level)
/// Level 1: 13px (sub level) 
/// Level 2: 12px (sub-sub level)
/// </summary>
public class MenuLevelFontSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int level)
        {
            return level switch
            {
                0 => 14.0,  // Main level
                1 => 13.0,  // Sub level
                _ => 12.0   // Sub-sub level and below
            };
        }
        
        return 13.0; // Default font size
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}