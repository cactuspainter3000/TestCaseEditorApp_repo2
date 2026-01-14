using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Converters
{
    /// <summary>
    /// Converter that displays issue description with a highlighted fix section
    /// Domain-specific converter for TestCaseGeneration requirement analysis fixes
    /// </summary>
    public class FixTextHighlightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not AnalysisIssue issue)
                return new TextBlock();

            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            };

            // Add the issue description in normal color
            if (!string.IsNullOrEmpty(issue.Description))
            {
                textBlock.Inlines.Add(new Run(issue.Description) { Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)) });
            }

            // Only add fix section if LLM actually provided a fix - never manufacture one
            if (!string.IsNullOrWhiteSpace(issue.Fix))
            {
                textBlock.Inlines.Add(new LineBreak());
                
                // Add the fix label in dull red
                textBlock.Inlines.Add(new Run("Fix: ") { Foreground = new SolidColorBrush(Color.FromRgb(139, 69, 69)) });
                
                // Add the actual LLM-provided fix content
                textBlock.Inlines.Add(new Run(issue.Fix) { Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)) });
            }

            return textBlock;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}