using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Views
{
    /// <summary>
    /// View for LLM-based test case generation with intelligent requirement coverage
    /// </summary>
    public partial class LLMTestCaseGeneratorView : UserControl
    {
        private DoubleAnimation? _tracerAnimation;
        private RotateTransform? _tracerRotateTransform;

        public LLMTestCaseGeneratorView()
        {
            InitializeComponent();
            Loaded += LLMTestCaseGeneratorView_Loaded;
            Unloaded += LLMTestCaseGeneratorView_Unloaded;
        }

        private void LLMTestCaseGeneratorView_Loaded(object sender, RoutedEventArgs e)
        {
            SetupTracerAnimation();

            var propertyDescriptor = DependencyPropertyDescriptor.FromProperty(
                FrameworkElement.DataContextProperty, typeof(FrameworkElement));
            propertyDescriptor?.AddValueChanged(this, OnDataContextChanged);

            SetupIsGeneratingBinding();
        }

        private void LLMTestCaseGeneratorView_Unloaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is LLMTestCaseGeneratorViewModel viewModel)
            {
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            SetupIsGeneratingBinding();
        }

        private void SetupIsGeneratingBinding()
        {
            if (DataContext is LLMTestCaseGeneratorViewModel previousViewModel)
            {
                previousViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (DataContext is LLMTestCaseGeneratorViewModel currentViewModel)
            {
                currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
                UpdateAnimationState(currentViewModel.IsGenerating);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LLMTestCaseGeneratorViewModel.IsGenerating) &&
                sender is LLMTestCaseGeneratorViewModel vm)
            {
                UpdateAnimationState(vm.IsGenerating);
            }
        }

        private void UpdateAnimationState(bool isGenerating)
        {
            if (isGenerating)
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
            if (GenerationTracerBorder == null) return;

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

            GenerationTracerBorder.BorderBrush = gradientBrush;

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
            if (GenerationTracerBorder == null) return;

            if (_tracerAnimation == null)
            {
                SetupTracerAnimation();
            }

            GenerationTracerBorder.Visibility = Visibility.Visible;

            if (_tracerRotateTransform != null && _tracerAnimation != null)
            {
                _tracerRotateTransform.BeginAnimation(RotateTransform.AngleProperty, _tracerAnimation);
            }
        }

        private void StopTracerAnimation()
        {
            if (GenerationTracerBorder != null)
            {
                GenerationTracerBorder.Visibility = Visibility.Collapsed;
            }

            if (_tracerRotateTransform != null)
            {
                _tracerRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }
    }
}
