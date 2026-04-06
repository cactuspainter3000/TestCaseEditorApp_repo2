using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using TestCaseEditorApp.MVVM.Models.DataDrivenMenu;

namespace TestCaseEditorApp.MVVM.Controls.DataDrivenMenu;

/// <summary>
/// Converts integer values to boolean based on whether they are greater than zero.
/// Used to determine if a collection has items for visibility logic.
/// </summary>
public class GreaterThanZeroConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue > 0;
        }
        
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Checks if any items in a collection have sub-items, determining if dropdown mode is needed.
/// Used to implement user's logic: "if items have sub-items, then there should be a dropdown"
/// </summary>
public class HasExpandableItemsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IEnumerable items)
        {
            return items.Cast<MenuContentItem>().Any(item => 
                item is ConditionalGroup group && group.Children.Any() ||
                item is ComplexAction // ComplexActions might be expandable in the future
            );
        }
        
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}