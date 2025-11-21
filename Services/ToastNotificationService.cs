using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Views;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for managing toast notifications
    /// </summary>
    public class ToastNotificationService
    {
        private readonly ObservableCollection<ToastNotification> _toasts;
        private readonly Dispatcher _dispatcher;
        private readonly HashSet<Guid> _dismissingToasts = new HashSet<Guid>();
        private readonly Dictionary<Guid, DispatcherTimer> _removalTimers = new Dictionary<Guid, DispatcherTimer>();

        public ObservableCollection<ToastNotification> Toasts => _toasts;

        public ToastNotificationService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _toasts = new ObservableCollection<ToastNotification>();
        }

        /// <summary>
        /// Show a toast notification
        /// </summary>
        /// <param name="message">Message to display</param>
        /// <param name="durationSeconds">Duration in seconds. 0 = no auto-dismiss (user must close manually)</param>
        /// <param name="type">Type of notification (Info, Success, Warning, Error)</param>
        public void ShowToast(string message, int durationSeconds = 4, ToastType type = ToastType.Info)
        {
            _dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"[TOAST] ShowToast: '{message}' (duration: {durationSeconds}s, type: {type})");
                
                // Trigger fade-out of existing toasts that have been visible for at least 0.5 seconds
                var existingToasts = _toasts.ToList();
                var now = DateTime.Now;
                var toastsToFade = existingToasts.Where(t => (now - t.CreatedAt).TotalSeconds >= 0.5).ToList();
                
                System.Diagnostics.Debug.WriteLine($"[TOAST] Found {existingToasts.Count} existing toast(s), {toastsToFade.Count} old enough to dismiss");
                foreach (var existingToast in toastsToFade)
                {
                    System.Diagnostics.Debug.WriteLine($"[TOAST]   - Dismissing: '{existingToast.Message}' (ID: {existingToast.Id}, age: {(now - existingToast.CreatedAt).TotalSeconds:F1}s)");
                    DismissToast(existingToast);
                }
                
                var toast = new ToastNotification
                {
                    Message = message,
                    Duration = durationSeconds,
                    Type = type,
                    IsVisible = true
                };

                _toasts.Add(toast);
                System.Diagnostics.Debug.WriteLine($"[TOAST] Added new toast (ID: {toast.Id}). Total toasts: {_toasts.Count}");

                // Auto-dismiss if duration is specified
                if (durationSeconds > 0)
                {
                    var timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(durationSeconds)
                    };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        System.Diagnostics.Debug.WriteLine($"[TOAST] Auto-dismiss timer fired for: '{toast.Message}' (ID: {toast.Id})");
                        DismissToast(toast);
                    };
                    timer.Start();
                }
            });
        }

        /// <summary>
        /// Dismiss a specific toast
        /// </summary>
        public void DismissToast(ToastNotification toast)
        {
            _dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"[TOAST] DismissToast called for: '{toast.Message}' (ID: {toast.Id})");
                
                // Prevent multiple dismissals of the same toast
                if (_dismissingToasts.Contains(toast.Id))
                {
                    System.Diagnostics.Debug.WriteLine($"[TOAST]   - Already dismissing, skipping");
                    return;
                }
                
                if (_toasts.Contains(toast))
                {
                    _dismissingToasts.Add(toast.Id);
                    System.Diagnostics.Debug.WriteLine($"[TOAST]   - Starting dismiss animation");
                    
                    // Trigger the dismiss animation in the view
                    toast.TriggerDismiss();
                    
                    // Cancel any existing removal timer for this toast
                    if (_removalTimers.ContainsKey(toast.Id))
                    {
                        System.Diagnostics.Debug.WriteLine($"[TOAST]   - Cancelling existing removal timer");
                        _removalTimers[toast.Id].Stop();
                        _removalTimers.Remove(toast.Id);
                    }
                    
                    // Give time for slide-out animation before removing (0.5s fade + 0.3s slide)
                    var removalTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(800)
                    };
                    
                    var toastToRemove = toast; // Capture the specific toast in closure
                    removalTimer.Tick += (s, e) =>
                    {
                        removalTimer.Stop();
                        _removalTimers.Remove(toastToRemove.Id);
                        System.Diagnostics.Debug.WriteLine($"[TOAST] Removal timer fired for: '{toastToRemove.Message}' (ID: {toastToRemove.Id})");
                        if (_toasts.Contains(toastToRemove))
                        {
                            _toasts.Remove(toastToRemove);
                            System.Diagnostics.Debug.WriteLine($"[TOAST]   - Removed from collection. Remaining toasts: {_toasts.Count}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[TOAST]   - Toast already removed from collection");
                        }
                        _dismissingToasts.Remove(toastToRemove.Id);
                    };
                    
                    _removalTimers[toast.Id] = removalTimer;
                    removalTimer.Start();
                    System.Diagnostics.Debug.WriteLine($"[TOAST]   - Removal timer started (800ms)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[TOAST]   - Toast not in collection, skipping");
                }
            });
        }

        /// <summary>
        /// Clear all toasts immediately
        /// </summary>
        public void ClearAll()
        {
            _dispatcher.Invoke(() => _toasts.Clear());
        }
    }
}
