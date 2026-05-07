using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Views
{
    /// <summary>
    /// Main view for Requirements domain.
    /// Provides comprehensive requirements management UI.
    /// </summary>
    public partial class RequirementsMainView : UserControl
    {
        private DoubleAnimation? _tracerAnimation;
        private RotateTransform? _tracerRotateTransform;

        public RequirementsMainView()
        {
            InitializeComponent();
            Unloaded += RequirementsMainView_Unloaded;
        }

        private void RequirementsMainView_Unloaded(object? sender, RoutedEventArgs e)
        {
            // Clean up property change subscription - type-safe cleanup
            if (DataContext is TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.UnifiedRequirementsMainViewModel viewModel &&
                viewModel.RequirementAnalysisVM != null)
            {
                viewModel.RequirementAnalysisVM.PropertyChanged -= OnAnalysisViewModelPropertyChanged;
            }
        }

        private void AnalysisButton_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton button)
            {
                SetupTracerAnimation();
                
                // Subscribe to DataContext property changes for ViewModel binding
                var propertyDescriptor = DependencyPropertyDescriptor.FromProperty(
                    FrameworkElement.DataContextProperty, typeof(FrameworkElement));
                propertyDescriptor?.AddValueChanged(this, OnDataContextChanged);
                    
                SetupIsAnalyzingBinding();
            }
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            SetupIsAnalyzingBinding();
        }

        private void SetupIsAnalyzingBinding()
        {
            // Clean up previous subscription - type-safe cleanup
            if (DataContext is TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.UnifiedRequirementsMainViewModel previousViewModel &&
                previousViewModel.RequirementAnalysisVM != null)
            {
                previousViewModel.RequirementAnalysisVM.PropertyChanged -= OnAnalysisViewModelPropertyChanged;
            }
            
            // Subscribe to new ViewModel's property changes with type safety
            if (DataContext is TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.UnifiedRequirementsMainViewModel currentViewModel &&
                currentViewModel.RequirementAnalysisVM != null)
            {
                currentViewModel.RequirementAnalysisVM.PropertyChanged += OnAnalysisViewModelPropertyChanged;
                
                // Get initial state - type-safe access
                UpdateAnimationState(currentViewModel.RequirementAnalysisVM.IsAnalyzing);
            }
        }

        private void OnAnalysisViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.RequirementAnalysisViewModel.IsAnalyzing) && 
                sender is TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.RequirementAnalysisViewModel analysisViewModel)
            {
                UpdateAnimationState(analysisViewModel.IsAnalyzing);
            }
        }
        
        private void UpdateAnimationState(bool isAnalyzing)
        {
            if (isAnalyzing)
            {
                StartTracerAnimation();
            }
            else
            {
                StopTracerAnimation();
            }
        }

        private void SetupTracerAnimation()
        {
            if (AnalysisTracerBorder == null) return;
            
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

            // Apply brush to the separate border
            AnalysisTracerBorder.BorderBrush = gradientBrush;

            // Create the animation
            _tracerAnimation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(3.5),
                RepeatBehavior = RepeatBehavior.Forever
            };
        }

        private void StartTracerAnimation()
        {
            if (AnalysisTracerBorder == null) return;
            
            // Setup animation if not already done
            if (_tracerAnimation == null)
            {
                SetupTracerAnimation();
            }
            
            // Show the tracer border and start animation
            AnalysisTracerBorder.Visibility = Visibility.Visible;
            
            if (_tracerRotateTransform != null && _tracerAnimation != null)
            {
                _tracerRotateTransform.BeginAnimation(RotateTransform.AngleProperty, _tracerAnimation);
            }
        }

        private void StopTracerAnimation()
        {
            if (AnalysisTracerBorder != null)
            {
                // Hide the tracer border
                AnalysisTracerBorder.Visibility = Visibility.Collapsed;
            }
            
            if (_tracerRotateTransform != null)
            {
                // Stop the animation
                _tracerRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }


    }
}