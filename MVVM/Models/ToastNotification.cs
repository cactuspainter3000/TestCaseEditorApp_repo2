using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Represents a toast notification message
    /// </summary>
    public partial class ToastNotification : ObservableObject
    {
        [ObservableProperty]
        private string _message = string.Empty;

        [ObservableProperty]
        private ToastType _type = ToastType.Info;

        [ObservableProperty]
        private bool _isVisible = true;

        /// <summary>
        /// Duration in seconds. 0 = no auto-dismiss (user must close)
        /// </summary>
        public int Duration { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Guid Id { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// Event raised when this toast should be dismissed with animation
        /// </summary>
        public event EventHandler? RequestDismiss;
        
        /// <summary>
        /// Trigger the dismiss animation
        /// </summary>
        public void TriggerDismiss()
        {
            RequestDismiss?.Invoke(this, EventArgs.Empty);
        }
    }

    public enum ToastType
    {
        Info,
        Success,
        Warning,
        Error
    }
}
