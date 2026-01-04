using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Views
{
    public partial class TestCaseGeneratorRequirements_View : UserControl
    {
        public TestCaseGeneratorRequirements_View()
        {
            InitializeComponent();

            // keep unloaded hookup if needed for cleanup; not strictly required now
            Unloaded += TestCaseGeneratorRequirements_View_Unloaded;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // kept minimal: attach to window resize if any future sizing logic is needed
            var w = Window.GetWindow(this);
            if (w != null)
                w.SizeChanged += HostWindow_SizeChanged;
        }

        private void TestCaseGeneratorRequirements_View_Unloaded(object? sender, RoutedEventArgs e)
        {
            var w = Window.GetWindow(this);
            if (w != null)
                w.SizeChanged -= HostWindow_SizeChanged;
        }

        private void HostWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            // no-op: popup/description sizing moved to the workspace header
        }

        private void RequirementsParagraphsControl_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void AnalysisTracerBorder_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border border) return;
            
            // Get the LinearGradientBrush from the border's BorderBrush
            if (border.BorderBrush is LinearGradientBrush brush && 
                brush.RelativeTransform is RotateTransform rotateTransform)
            {
                // Animate the gradient rotation - slower and smoother
                var animation = new DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = TimeSpan.FromSeconds(3.5),
                    RepeatBehavior = RepeatBehavior.Forever
                };

                rotateTransform.BeginAnimation(RotateTransform.AngleProperty, animation);
            }
        }

        private void AnalysisTracerBorder_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not Border border) return;

            if (border.Visibility == Visibility.Visible)
            {
                // Get the LinearGradientBrush from the border's BorderBrush
                if (border.BorderBrush is LinearGradientBrush brush && 
                    brush.RelativeTransform is RotateTransform rotateTransform)
                {
                    var animation = new DoubleAnimation
                    {
                        From = 0,
                        To = 360,
                        Duration = TimeSpan.FromSeconds(3.5),
                        RepeatBehavior = RepeatBehavior.Forever
                    };

                    rotateTransform.BeginAnimation(RotateTransform.AngleProperty, animation);
                }
            }
        }
    }
}