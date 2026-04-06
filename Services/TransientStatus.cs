using System;
using System.Windows;

namespace TestCaseEditorApp.Services
{
    internal static class TransientStatus
    {
        public static void Show(ToastNotificationService toastService, string message, int seconds = 3, bool blockingError = false)
        {
            if (blockingError)
            {
                // Use NotificationService if available, otherwise fallback to MessageBox
                var notificationService = new NotificationService(toastService);
                notificationService.ShowCriticalError(message);
                return;
            }

            var toastType = TestCaseEditorApp.MVVM.Models.ToastType.Info;
            var lowerMsg = message?.ToLowerInvariant() ?? string.Empty;
            if (lowerMsg.Contains("fail") || lowerMsg.Contains("error"))
            {
                toastType = TestCaseEditorApp.MVVM.Models.ToastType.Error;
            }
            else if (lowerMsg.Contains("cancel"))
            {
                toastType = TestCaseEditorApp.MVVM.Models.ToastType.Warning;
            }
            else if (lowerMsg.Contains("saved") || lowerMsg.Contains("complete") || lowerMsg.Contains("created") || lowerMsg.Contains("opened"))
            {
                toastType = TestCaseEditorApp.MVVM.Models.ToastType.Success;
            }

            // Use the existing toast service to show the notification
            toastService?.ShowToast(message ?? string.Empty, seconds, toastType);
        }
    }
}
