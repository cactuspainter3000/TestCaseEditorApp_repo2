using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service responsible for creating complete view configurations for each section.
    /// Single source of truth for what views should be active in each section.
    /// </summary>
    public interface IViewConfigurationService
    {
        /// <summary>
        /// Get the complete view configuration for a specific section
        /// </summary>
        ViewConfiguration GetConfigurationForSection(string sectionName, object? context = null);
        
        /// <summary>
        /// Apply a view configuration by broadcasting it to all view areas
        /// </summary>
        void ApplyConfiguration(string sectionName, object? context = null);
        
        /// <summary>
        /// Get the current active configuration
        /// </summary>
        ViewConfiguration? CurrentConfiguration { get; }
    }
}