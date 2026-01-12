using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Converters
{
    /// <summary>
    /// Converter that highlights [bracketed] fill-in-the-blank sections in requirement recommendations
    /// Shows helpful elaboration sections with distinct highlighting
    /// </summary>
    public class BracketHighlightConverter : IValueConverter
    {
        private static readonly Regex BracketPattern = new Regex(@"\[([^\]]+)\]", RegexOptions.Compiled);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not AnalysisRecommendation recommendation)
                return new TextBlock();

            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                LineHeight = 18
            };

            // Add the recommendation description
            if (!string.IsNullOrEmpty(recommendation.Description))
            {
                textBlock.Inlines.Add(new Run(recommendation.Description) 
                { 
                    Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
                    FontWeight = FontWeights.SemiBold
                });
                textBlock.Inlines.Add(new LineBreak());
                textBlock.Inlines.Add(new LineBreak());
            }

            // Add the suggested edit with bracket highlighting
            if (!string.IsNullOrEmpty(recommendation.SuggestedEdit))
            {
                textBlock.Inlines.Add(new Run("📝 Suggested Edit:") 
                { 
                    Foreground = new SolidColorBrush(Color.FromRgb(139, 195, 74)),
                    FontWeight = FontWeights.SemiBold
                });
                textBlock.Inlines.Add(new LineBreak());

                // Parse the text and highlight [bracketed] sections
                string text = recommendation.SuggestedEdit;
                int lastIndex = 0;

                foreach (Match match in BracketPattern.Matches(text))
                {
                    // Add text before the bracket
                    if (match.Index > lastIndex)
                    {
                        string beforeText = text.Substring(lastIndex, match.Index - lastIndex);
                        textBlock.Inlines.Add(new Run(beforeText) 
                        { 
                            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220))
                        });
                    }

                    // Add the bracketed text with highlighting
                    textBlock.Inlines.Add(new Run(match.Value)
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 235, 59)),
                        Background = new SolidColorBrush(Color.FromArgb(40, 255, 235, 59)),
                        FontWeight = FontWeights.SemiBold
                    });

                    lastIndex = match.Index + match.Length;
                }

                // Add any remaining text after the last bracket
                if (lastIndex < text.Length)
                {
                    string afterText = text.Substring(lastIndex);
                    textBlock.Inlines.Add(new Run(afterText) 
                    { 
                        Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220))
                    });
                }
            }

            return textBlock;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}