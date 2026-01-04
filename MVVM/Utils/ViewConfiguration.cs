using System;

namespace TestCaseEditorApp.MVVM.Utils
{
    /// <summary>
    /// Complete view configuration for a section.
    /// Contains all the views that should be active when navigating to a specific section.
    /// </summary>
    public class ViewConfiguration
    {
        public string SectionName { get; }
        public object? HeaderViewModel { get; }
        public object? ContentViewModel { get; }
        public object? NavigationViewModel { get; }
        public object? NotificationViewModel { get; }
        public object? Context { get; }

        public ViewConfiguration(
            string sectionName, 
            object? headerViewModel = null,
            object? contentViewModel = null, 
            object? navigationViewModel = null,
            object? notificationViewModel = null,
            object? context = null)
        {
            SectionName = sectionName ?? throw new ArgumentNullException(nameof(sectionName));
            HeaderViewModel = headerViewModel;
            ContentViewModel = contentViewModel;
            NavigationViewModel = navigationViewModel;
            NotificationViewModel = notificationViewModel;
            Context = context;
        }

        /// <summary>
        /// Check if this configuration is equivalent to another configuration.
        /// Used by view areas to determine if they need to update.
        /// </summary>
        public bool IsEquivalentTo(ViewConfiguration? other)
        {
            if (other == null) return false;
            
            return string.Equals(SectionName, other.SectionName, StringComparison.OrdinalIgnoreCase) &&
                   ReferenceEquals(HeaderViewModel, other.HeaderViewModel) &&
                   ReferenceEquals(ContentViewModel, other.ContentViewModel) &&
                   ReferenceEquals(NavigationViewModel, other.NavigationViewModel) &&
                   ReferenceEquals(NotificationViewModel, other.NotificationViewModel);
        }

        public override string ToString()
        {
            return $"ViewConfiguration[{SectionName}]: Header={HeaderViewModel?.GetType().Name}, Content={ContentViewModel?.GetType().Name}";
        }
    }
}