using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace TestCaseEditorApp.Converters
{
    /// <summary>
    /// Converter that formats status text to display pipes (|) in dark orange while keeping the rest in light grey
    /// </summary>
    public class StatusTextWithOrangePipesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string text || string.IsNullOrEmpty(text))
                return new TextBlock();

            var textBlock = new TextBlock
            {
                FontWeight = FontWeights.Medium,
                FontSize = 14, // Approximate H3 size
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // Split the text by pipes to create alternating segments
            var segments = text.Split('|');
            
            for (int i = 0; i < segments.Length; i++)
            {
                // Add the text segment in light grey
                if (!string.IsNullOrEmpty(segments[i]))
                {
                    var textRun = new Run(segments[i])
                    {
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC")) // Light grey
                    };
                    textBlock.Inlines.Add(textRun);
                }
                
                // Add the pipe separator in dark orange (except after the last segment)
                if (i < segments.Length - 1)
                {
                    var pipeRun = new Run(" | ")
                    {
                        Foreground = new SolidColorBrush(Colors.DarkOrange)
                    };
                    textBlock.Inlines.Add(pipeRun);
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