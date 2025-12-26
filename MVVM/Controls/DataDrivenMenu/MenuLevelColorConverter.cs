using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TestCaseEditorApp.MVVM.Controls.DataDrivenMenu;

/// <summary>
/// Converts menu item level to appropriate color based on hierarchy level.
/// Level 0 items use HeaderAccentBrush (dark orange).
/// Level 1+ items use light grey color.
/// </summary>
public class MenuLevelColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int level)
        {
            // Main level items (level 0): Use dark orange
            if (level == 0)
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("DarkOrange"));
            }
        }
        
        // Sub-level items (level 1+): Use light grey
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}