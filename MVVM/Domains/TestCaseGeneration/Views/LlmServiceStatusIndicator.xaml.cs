using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;
using static TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.LlmServiceHealthMonitor;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Views
{
    /// <summary>
    /// UserControl for displaying LLM service health status with visual indicators.
    /// Shows service type, health status, response time, and fallback state.
    /// </summary>
    public partial class LlmServiceStatusIndicator : UserControl, INotifyPropertyChanged
    {
        public static readonly DependencyProperty HealthReportProperty =
            DependencyProperty.Register(nameof(HealthReport), typeof(HealthReport), typeof(LlmServiceStatusIndicator),
                new PropertyMetadata(null, OnHealthReportChanged));

        public HealthReport? HealthReport
        {
            get => (HealthReport?)GetValue(HealthReportProperty);
            set => SetValue(HealthReportProperty, value);
        }

        public string StatusText { get; private set; } = "LLM Status: Unknown";
        public Brush StatusColor { get; private set; } = Brushes.Gray;
        public string ServiceInfo { get; private set; } = "";
        public Visibility FallbackIndicatorVisibility { get; private set; } = Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LlmServiceStatusIndicator()
        {
            InitializeComponent();
            DataContext = this;
        }

        private static void OnHealthReportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LlmServiceStatusIndicator indicator)
            {
                indicator.UpdateStatusDisplay();
            }
        }

        private void UpdateStatusDisplay()
        {
            var report = HealthReport;
            if (report == null)
            {
                StatusText = "LLM Status: Unknown";
                StatusColor = Brushes.Gray;
                ServiceInfo = "";
                FallbackIndicatorVisibility = Visibility.Collapsed;
            }
            else
            {
                // Update status text and color based on health
                (StatusText, StatusColor) = report.Status switch
                {
                    HealthStatus.Healthy => ("LLM Status: Healthy", Brushes.Green),
                    HealthStatus.Degraded => ("LLM Status: Slow", Brushes.Orange),
                    HealthStatus.Unavailable => report.IsUsingFallback ? 
                        ("LLM Status: Fallback Active", Brushes.Red) : 
                        ("LLM Status: Unavailable", Brushes.Red),
                    _ => ("LLM Status: Unknown", Brushes.Gray)
                };

                // Service information with response time
                ServiceInfo = report.Status switch
                {
                    HealthStatus.Healthy or HealthStatus.Degraded => 
                        $"{report.ServiceType} ({report.ResponseTime.TotalMilliseconds:F0}ms)",
                    HealthStatus.Unavailable when report.IsUsingFallback => 
                        $"{report.ServiceType} failed - Using fallback",
                    HealthStatus.Unavailable => 
                        $"{report.ServiceType} - {report.LastError ?? "Connection failed"}",
                    _ => report.ServiceType
                };

                // Show fallback indicator
                FallbackIndicatorVisibility = report.IsUsingFallback ? Visibility.Visible : Visibility.Collapsed;
            }

            // Notify UI of changes
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(ServiceInfo));
            OnPropertyChanged(nameof(FallbackIndicatorVisibility));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}