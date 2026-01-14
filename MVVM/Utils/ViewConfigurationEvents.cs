using System;

namespace TestCaseEditorApp.MVVM.Utils
{
    /// <summary>
    /// Navigation events for the proper view configuration pattern
    /// </summary>
    public static class ViewConfigurationEvents
    {
        /// <summary>
        /// Published when a complete view configuration should be applied
        /// </summary>
        public class ApplyViewConfiguration
        {
            public ViewConfiguration Configuration { get; }
            
            public ApplyViewConfiguration(ViewConfiguration configuration)
            {
                Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            }
        }

        /// <summary>
        /// Published when view configuration has been successfully applied
        /// </summary>
        public class ViewConfigurationApplied
        {
            public ViewConfiguration Configuration { get; }
            public string ViewAreaName { get; }
            public bool WasChanged { get; }
            
            public ViewConfigurationApplied(ViewConfiguration configuration, string viewAreaName, bool wasChanged)
            {
                Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
                ViewAreaName = viewAreaName ?? throw new ArgumentNullException(nameof(viewAreaName));
                WasChanged = wasChanged;
            }
        }
    }
}