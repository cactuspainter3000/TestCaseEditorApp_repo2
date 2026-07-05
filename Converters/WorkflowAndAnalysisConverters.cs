using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Converters
{
    /// <summary>
    /// Converter that displays issue description with an optional highlighted fix section.
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

            if (!string.IsNullOrEmpty(issue.Description))
            {
                textBlock.Inlines.Add(new Run(issue.Description) { Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)) });
            }

            if (!string.IsNullOrWhiteSpace(issue.Fix))
            {
                textBlock.Inlines.Add(new LineBreak());
                textBlock.Inlines.Add(new Run("Fix: ") { Foreground = new SolidColorBrush(Color.FromRgb(139, 69, 69)) });
                textBlock.Inlines.Add(new Run(issue.Fix) { Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)) });
            }

            return textBlock;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter that highlights [bracketed] fill-in sections in recommendation edits.
    /// </summary>
    public class BracketHighlightConverter : IValueConverter
    {
        private static readonly Regex BracketPattern = new(@"\[([^\]]+)\]", RegexOptions.Compiled);

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

            if (!string.IsNullOrEmpty(recommendation.SuggestedEdit))
            {
                textBlock.Inlines.Add(new Run("📝 Suggested Edit:")
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(139, 195, 74)),
                    FontWeight = FontWeights.SemiBold
                });
                textBlock.Inlines.Add(new LineBreak());

                var text = recommendation.SuggestedEdit;
                var lastIndex = 0;

                foreach (Match match in BracketPattern.Matches(text))
                {
                    if (match.Index > lastIndex)
                    {
                        var beforeText = text.Substring(lastIndex, match.Index - lastIndex);
                        textBlock.Inlines.Add(new Run(beforeText)
                        {
                            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220))
                        });
                    }

                    textBlock.Inlines.Add(new Run(match.Value)
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 235, 59)),
                        Background = new SolidColorBrush(Color.FromArgb(40, 255, 235, 59)),
                        FontWeight = FontWeights.SemiBold
                    });

                    lastIndex = match.Index + match.Length;
                }

                if (lastIndex < text.Length)
                {
                    var afterText = text.Substring(lastIndex);
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

    /// <summary>
    /// Converter that returns different colors based on whether a value is null/empty.
    /// ConverterParameter format: "ColorIfNotNull|ColorIfNull".
    /// </summary>
    public class NullToColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isNullOrEmpty = value == null ||
                               (value is string str && string.IsNullOrWhiteSpace(str));

            if (parameter is string paramStr && paramStr.Contains('|'))
            {
                var colors = paramStr.Split('|');
                var colorToUse = isNullOrEmpty ? colors[1] : colors[0];

                try
                {
                    return new BrushConverter().ConvertFromString(colorToUse);
                }
                catch
                {
                    // Fall through to defaults.
                }
            }

            return isNullOrEmpty
                ? new SolidColorBrush(Colors.Gray)
                : new SolidColorBrush(Colors.White);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter that inverts a boolean and converts to Visibility.
    /// </summary>
    public class InvertBoolToVisConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Collapsed;
            }
            return false;
        }
    }

    /// <summary>
    /// Placeholder converter for CheckBox styling hook.
    /// </summary>
    public class OrangeCheckBoxStyleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}