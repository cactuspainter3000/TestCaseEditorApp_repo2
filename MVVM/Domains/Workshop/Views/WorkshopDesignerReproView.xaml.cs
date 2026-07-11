using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Globalization;
using TestCaseEditorApp.MVVM.Domains.Workshop.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Workshop.Views
{
    public partial class WorkshopDesignerReproView : UserControl
    {
        public WorkshopDesignerReproView()
        {
            InitializeComponent();
            this.DataContextChanged += (s, e) =>
            {
                if (DataContext is WorkshopReproViewModel vm)
                {
                    vm.PropertyChanged += (sender, args) =>
                    {
                        if (args.PropertyName == nameof(vm.AnalysisResults))
                        {
                            // Show results scroll, hide loading
                            if (vm.AnalysisResults != null && !vm.IsAnalyzing)
                            {
                                LoadingStack.Visibility = Visibility.Collapsed;
                                ResultsScroll.Visibility = Visibility.Visible;
                            }
                        }
                        else if (args.PropertyName == nameof(vm.IsAnalyzing))
                        {
                            // Show loading when analysis starts
                            if (vm.IsAnalyzing)
                            {
                                LoadingStack.Visibility = Visibility.Visible;
                                ResultsScroll.Visibility = Visibility.Collapsed;
                            }
                        }
                    };
                }
            };
        }

        // Close modal when clicking on semi-transparent backdrop
        private void AnalysisModalOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border && e.Source == sender)
            {
                if (DataContext is WorkshopReproViewModel vm)
                {
                    vm.CloseAnalysisModalCommand.Execute(null);
                }
            }
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
