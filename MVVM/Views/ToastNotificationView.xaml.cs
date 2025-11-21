using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class ToastNotificationView : UserControl
    {
        public event EventHandler? Dismissed;

        public ToastNotificationView()
        {
            InitializeComponent();
            Loaded += ToastNotificationView_Loaded;
            DataContextChanged += ToastNotificationView_DataContextChanged;
        }

        private void ToastNotificationView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old toast
            if (e.OldValue is ToastNotification oldToast)
            {
                oldToast.RequestDismiss -= OnRequestDismiss;
            }
            
            // Subscribe to new toast
            if (e.NewValue is ToastNotification newToast)
            {
                newToast.RequestDismiss += OnRequestDismiss;
            }
        }

        private void OnRequestDismiss(object? sender, EventArgs e)
        {
            if (DataContext is ToastNotification toast)
            {
                System.Diagnostics.Debug.WriteLine($"[TOAST VIEW] OnRequestDismiss for: '{toast.Message}' (ID: {toast.Id})");
            }
            Dismiss();
        }

        private void ToastNotificationView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ToastNotification toast)
            {
                System.Diagnostics.Debug.WriteLine($"[TOAST VIEW] View loaded for: '{toast.Message}' (ID: {toast.Id})");
            }
            var storyboard = (Storyboard)Resources["SlideInStoryboard"];
            storyboard.Begin(this);
        }

        public void Dismiss()
        {
            if (DataContext is ToastNotification toast)
            {
                System.Diagnostics.Debug.WriteLine($"[TOAST VIEW] Dismiss() called, starting slide-out animation for: '{toast.Message}' (ID: {toast.Id})");
            }
            var storyboard = (Storyboard)Resources["SlideOutStoryboard"];
            storyboard.Begin(this);
        }

        private void SlideOut_Completed(object? sender, EventArgs e)
        {
            if (DataContext is ToastNotification toast)
            {
                System.Diagnostics.Debug.WriteLine($"[TOAST VIEW] SlideOut animation completed for: '{toast.Message}' (ID: {toast.Id})");
            }
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Dismiss();
        }
    }
}
