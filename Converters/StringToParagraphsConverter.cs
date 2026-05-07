using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace TestCaseEditorApp.Converters
{
    /// <summary>
    /// Converts a string containing multiple paragraphs (separated by line breaks) 
    /// into a collection of strings for better display formatting.
    /// </summary>
    public class StringToParagraphsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string text || string.IsNullOrWhiteSpace(text))
                return new List<string>();

            // Split on various line break patterns
            var separators = new[] { "\r\n\r\n", "\n\n", "\r\r" }; // Double line breaks for paragraphs
            var paragraphs = text.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(p => p.Trim())
                                 .Where(p => !string.IsNullOrWhiteSpace(p))
                                 .ToList();

            // If no double line breaks found, try single line breaks
            if (paragraphs.Count <= 1 && text.Contains('\n'))
            {
                var singleBreakSeparators = new[] { "\r\n", "\n", "\r" };
                paragraphs = text.Split(singleBreakSeparators, StringSplitOptions.RemoveEmptyEntries)
                                .Select(p => p.Trim())
                                .Where(p => !string.IsNullOrWhiteSpace(p))
                                .ToList();
            }

            // If still no splits, return the original text as a single paragraph
            if (paragraphs.Count == 0)
                paragraphs.Add(text.Trim());

            return paragraphs;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IEnumerable<string> paragraphs)
                return string.Join("\n\n", paragraphs);
            
            return string.Empty;
        }
    }
}