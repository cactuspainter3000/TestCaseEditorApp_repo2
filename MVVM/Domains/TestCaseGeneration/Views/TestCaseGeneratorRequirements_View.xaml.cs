using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Views
{
    public partial class TestCaseGeneratorRequirements_View : UserControl
    {
        private DoubleAnimation? _tracerAnimation;
        private RotateTransform? _tracerRotateTransform;

        public TestCaseGeneratorRequirements_View()
        {
            InitializeComponent();
            Unloaded += TestCaseGeneratorRequirements_View_Unloaded;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            var w = Window.GetWindow(this);
            if (w != null)
                w.SizeChanged += HostWindow_SizeChanged;
        }

        private void TestCaseGeneratorRequirements_View_Unloaded(object? sender, RoutedEventArgs e)
        {
            var w = Window.GetWindow(this);
            if (w != null)
                w.SizeChanged -= HostWindow_SizeChanged;
                
            // Clean up property change subscription - type-safe cleanup
            if (DataContext is TestCaseGeneration.ViewModels.TestCaseGenerator_VM viewModel)
            {
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
        }

        private void HostWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            // no-op: popup/description sizing moved to the workspace header
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
            if (DataContext is TestCaseGeneration.ViewModels.TestCaseGenerator_VM previousViewModel)
            {
                previousViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
            
            // Subscribe to new ViewModel's property changes with type safety
            if (DataContext is TestCaseGeneration.ViewModels.TestCaseGenerator_VM currentViewModel)
            {
                currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
                
                // Get initial state - type-safe access
                UpdateAnimationState(currentViewModel.IsAnalyzing);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TestCaseGeneration.ViewModels.TestCaseGenerator_VM.IsAnalyzing) && 
                sender is TestCaseGeneration.ViewModels.TestCaseGenerator_VM viewModel)
            {
                UpdateAnimationState(viewModel.IsAnalyzing);
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