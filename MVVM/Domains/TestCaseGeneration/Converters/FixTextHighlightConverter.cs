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

            // Always add fix section - either with LLM-provided fix or placeholder
            textBlock.Inlines.Add(new LineBreak());

            // Add the fix label in dull red
            textBlock.Inlines.Add(new Run("Fix: ") { Foreground = new SolidColorBrush(Color.FromRgb(139, 69, 69)) });

            // Add fix content or generate meaningful default
            string fixText = !string.IsNullOrEmpty(issue.Fix) ? issue.Fix : GetDefaultFix(issue.Category);
            textBlock.Inlines.Add(new Run(fixText) { Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)) });

            return textBlock;
        }

        private string GetDefaultFix(string category)
        {
            return category.ToLower() switch
            {
                "clarity" => "Clarified ambiguous terminology",
                "testability" => "Added specific acceptance criteria",
                "completeness" => "Specified missing details",
                "consistency" => "Aligned terminology and format",
                "feasibility" => "Defined realistic constraints",
                _ => "Addressed identified concerns"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}