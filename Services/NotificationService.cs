using System;
using System.Threading.Tasks;
using System.Windows;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Centralized service for handling all user notifications and messages.
    /// Replaces MessageBox.Show with toast notifications and provides modal support.
    /// </summary>
    public class NotificationService
    {
        private readonly ToastNotificationService _toastService;

        public NotificationService(ToastNotificationService toastService)
        {
            _toastService = toastService ?? throw new ArgumentNullException(nameof(toastService));
        }

        /// <summary>
        /// Show an informational message as a toast notification
        /// </summary>
        public void ShowInfo(string message, int durationSeconds = 4)
        {
            _toastService.ShowToast(message, durationSeconds, ToastType.Info);
        }

        /// <summary>
        /// Show a success message as a toast notification
        /// </summary>
        public void ShowSuccess(string message, int durationSeconds = 4)
        {
            _toastService.ShowToast(message, durationSeconds, ToastType.Success);
        }

        /// <summary>
        /// Show a warning message as a toast notification
        /// </summary>
        public void ShowWarning(string message, int durationSeconds = 6)
        {
            _toastService.ShowToast(message, durationSeconds, ToastType.Warning);
        }

        /// <summary>
        /// Show an error message as a toast notification
        /// </summary>
        public void ShowError(string message, int durationSeconds = 8)
        {
            _toastService.ShowToast(message, durationSeconds, ToastType.Error);
        }

        /// <summary>
        /// Show a persistent error that requires user acknowledgment.
        /// Falls back to MessageBox for critical errors until modal panel system is implemented.
        /// </summary>
        public void ShowCriticalError(string message, string title = "Error")
        {
            // For now, use MessageBox for critical errors that need acknowledgment
            // TODO: Replace with in-app modal panel when implemented
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>
        /// Show a confirmation dialog. 
        /// Falls back to MessageBox until modal panel system is implemented.
        /// </summary>
        public bool ShowConfirmation(string message, string title = "Confirm")
        {
            // TODO: Replace with in-app modal panel when implemented
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Show a question with Yes/No/Cancel options.
        /// Falls back to MessageBox until modal panel system is implemented.
        /// </summary>
        public MessageBoxResult ShowQuestion(string message, string title = "Question")
        {
            // TODO: Replace with in-app modal panel when implemented
            return MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        }

        /// <summary>
        /// Clear all existing toast notifications
        /// </summary>
        public void ClearAll()
        {
            _toastService.ClearAll();
        }
    }
}