using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Views
{
    /// <summary>
    /// Interaction logic for RequirementsSearchAttachmentsView.xaml
    /// View for searching and extracting requirements from Jama Connect attachments.
    /// Follows Architectural Guide AI patterns for Requirements domain views.
    /// DataContext is provided by parent ViewModel via DataTemplate mapping.
    /// </summary>
    public partial class RequirementsSearchAttachmentsView : UserControl
    {
        private DoubleAnimation? _tracerAnimation;
        private RotateTransform? _tracerRotateTransform;

        public RequirementsSearchAttachmentsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old ViewModel
            if (e.OldValue is RequirementsSearchAttachmentsViewModel oldViewModel)
            {
                oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            // Subscribe to new ViewModel
            if (e.NewValue is RequirementsSearchAttachmentsViewModel newViewModel)
            {
                newViewModel.PropertyChanged += OnViewModelPropertyChanged;
                SetupTracerAnimation();
                
                // Refresh project state when view becomes active
                newViewModel.RefreshCurrentProjectState();
            }
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RequirementsSearchAttachmentsViewModel.IsParsing))
            {
                if (DataContext is RequirementsSearchAttachmentsViewModel viewModel)
                {
                    if (viewModel.IsParsing)
                    {
                        StartTracerAnimation();
                    }
                    else
                    {
                        StopTracerAnimation();
                    }
                }
            }
        }

        private void SetupTracerAnimation()
        {
            // ScrapeTracerBorder removed - smart button implementation
            /*
            if (ScrapeTracerBorder == null) return;
            
            // Create the animated border brush
            var gradientBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            
            gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 136, 0), 0.0));
            gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(136, 136, 136), 0.25));
            gradientBrush.GradientStops.Add(new GradientStop(Colors.White, 0.5));
            gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(136, 136, 136), 0.75));
            gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 136, 0), 1.0));

            _tracerRotateTransform = new RotateTransform
            {
                CenterX = 0.5,
                CenterY = 0.5,
                Angle = 0
            };
            gradientBrush.RelativeTransform = _tracerRotateTransform;

            // Apply brush to the tracer border
            ScrapeTracerBorder.BorderBrush = gradientBrush;

            // Create the animation
            _tracerAnimation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(3.5),
                RepeatBehavior = RepeatBehavior.Forever
            };
            */
        }

        private void StartTracerAnimation()
        {
            // ScrapeTracerBorder removed - smart button implementation
            /*
            if (ScrapeTracerBorder == null) return;
            
            // Setup animation if not already done
            if (_tracerAnimation == null)
            {
                SetupTracerAnimation();
            }
            
            // Show the tracer border and start animation
            ScrapeTracerBorder.Visibility = Visibility.Visible;
            
            if (_tracerRotateTransform != null && _tracerAnimation != null)
            {
                _tracerRotateTransform.BeginAnimation(RotateTransform.AngleProperty, _tracerAnimation);
            }
            */
        }

        private void StopTracerAnimation()
        {
            // ScrapeTracerBorder removed - smart button implementation
            /*
            if (ScrapeTracerBorder != null)
            {
                // Hide the tracer border
                ScrapeTracerBorder.Visibility = Visibility.Collapsed;
            }
            
            if (_tracerRotateTransform != null)
            {
                // Stop the animation
                _tracerRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            }
            */
        }
    }
}