using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Globalization;
using TestCaseEditorApp.MVVM.Domains.Workshop.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Workshop.Views
{
    public partial class WorkshopDesignerReproView : UserControl
    {
        private const double AnalysisPanelWidth = 380; // Width when analysis panel is open

        public WorkshopDesignerReproView()
        {
            InitializeComponent();
            this.DataContextChanged += (s, e) =>
            {
                if (DataContext is WorkshopReproViewModel vm)
                {
                    vm.PropertyChanged += (sender, args) =>
                    {
                        if (args.PropertyName == nameof(vm.IsAnalysisModalOpen))
                        {
                            AnimateAnalysisPanelWidth(vm.IsAnalysisModalOpen);
                        }
                        else if (args.PropertyName == nameof(vm.AnalysisResults))
                        {
                            // Show results scroll, hide loading
                            if (vm.AnalysisResults != null && !vm.IsAnalyzing)
                            {
                                AnalysisLoadingStack.Visibility = Visibility.Collapsed;
                                AnalysisResultsScroll.Visibility = Visibility.Visible;
                            }
                        }
                        else if (args.PropertyName == nameof(vm.IsAnalyzing))
                        {
                            // Show loading when analysis starts
                            if (vm.IsAnalyzing)
                            {
                                AnalysisLoadingStack.Visibility = Visibility.Visible;
                                AnalysisResultsScroll.Visibility = Visibility.Collapsed;
                            }
                        }
                    };
                }
            };
        }

        /// <summary>
        /// Animates the analysis panel column width in/out smoothly
        /// </summary>
        private void AnimateAnalysisPanelWidth(bool isOpen)
        {
            double targetWidth = isOpen ? AnalysisPanelWidth : 0;
            
            var animation = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            AnalysisPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
        }
    }

    /// <summary>
    /// Converter that displays "Hide LLM Analysis Panel" when modal is open, "Show LLM Analysis Panel" when closed
    /// </summary>
    public class BoolToAnalysisButtonTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isOpen)
            {
                return isOpen ? "Hide LLM Analysis Panel" : "Show LLM Analysis Panel";
            }
            return "Show LLM Analysis Panel";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
