using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Default notification ViewModel shown when no specific domain is active
    /// Shows a clean, empty state for the notification area
    /// </summary>
    public partial class DefaultNotificationViewModel : ObservableObject
    {
        private readonly ILogger<DefaultNotificationViewModel>? _logger;

        /// <summary>
        /// Display text for the default state
        /// </summary>
        [ObservableProperty]
        private string defaultText = "Ready";

        /// <summary>
        /// Whether to show any content in the notification area
        /// </summary>
        [ObservableProperty]
        private bool isVisible = true;

        public DefaultNotificationViewModel(ILogger<DefaultNotificationViewModel>? logger = null)
        {
            _logger = logger;
            _logger?.LogInformation("DefaultNotificationViewModel initialized");
        }
    }
}