using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.Globalization;
using TestCaseEditorApp.MVVM.Domains.Workshop.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Workshop.Views
{
    public partial class WorkshopDesignerReproView : UserControl
    {
        private const double AnalysisPanelWidth = 380; // Width when analysis panel is open
        private const int AnimationDurationMs = 300;
        private const int AnimationFrameMs = 16; // ~60fps
        private DispatcherTimer _animationTimer;
        private double _currentWidth = 0;
        private double _targetWidth = 0;
        private DateTime _animationStart;

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
                    };
                }
            };
        }

        /// <summary>
        /// Animates the analysis panel column width in/out smoothly using easing function
        /// </summary>
        private void AnimateAnalysisPanelWidth(bool isOpen)
        {
            _targetWidth = isOpen ? AnalysisPanelWidth : 0;
            _currentWidth = AnalysisPanelColumn.Width.Value;
            _animationStart = DateTime.Now;

            if (_animationTimer == null)
            {
                _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AnimationFrameMs) };
                _animationTimer.Tick += (s, e) => UpdateColumnWidth();
            }

            _animationTimer.Start();
        }

        /// <summary>
        /// Updates column width during animation with easing
        /// </summary>
        private void UpdateColumnWidth()
        {
            var elapsed = (DateTime.Now - _animationStart).TotalMilliseconds;
            var progress = Math.Min(elapsed / AnimationDurationMs, 1.0); // 0 to 1

            // CubicEase.EaseInOut formula
            progress = progress < 0.5
                ? 4 * progress * progress * progress
                : 1 - Math.Pow(-2 * progress + 2, 3) / 2;

            var newWidth = _currentWidth + (_targetWidth - _currentWidth) * progress;
            AnalysisPanelColumn.Width = new GridLength(newWidth);

            if (progress >= 1.0)
            {
                _animationTimer.Stop();
                AnalysisPanelColumn.Width = new GridLength(_targetWidth);
            }
        }

        private void WorkshopNavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Close the popup immediately after a user selection for faster navigation flow.
            if (WorkshopNavDropdownButton?.IsChecked == true && e.AddedItems.Count > 0)
            {
                WorkshopNavDropdownButton.IsChecked = false;
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

    /// <summary>
    /// Converter that returns Visible if RequirementAnalysis is not null, otherwise Collapsed
    /// </summary>
    public class HasAnalysisResultsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter that returns the inverse of a boolean as Visibility
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class LifecycleStageToDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "Unknown";
            
            // Get the type name to check dynamically since it's from another namespace
            var typeFullName = value.GetType().FullName ?? "";
            if (typeFullName.Contains("RequirementLifecycleStage"))
            {
                var stageValue = (int)value;
                return stageValue switch
                {
                    0 => "Unsaved",      // Edit
                    1 => "Staged",       // StagedForCommit
                    2 => "Committed",    // Committed
                    _ => "Unknown"
                };
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class LifecycleStageToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // This converter is not currently used in the UI, but kept for future enhancement
            return -1;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

