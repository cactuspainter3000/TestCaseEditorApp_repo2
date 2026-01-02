using System;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Events;

namespace TestCaseEditorApp.MVVM.Mediators
{
    /// <summary>
    /// Mediator for section-level breadcrumb (Test Case Generator, Requirements, etc.)
    /// Handles the first level of navigation context.
    /// </summary>
    public class BreadcrumbSectionMediator
    {
        private readonly ILogger<BreadcrumbSectionMediator>? _logger;
        private string _currentSection = string.Empty;

        public BreadcrumbSectionMediator(ILogger<BreadcrumbSectionMediator>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Event fired when section changes
        /// </summary>
        public event Action<BreadcrumbEvents.SectionChanged>? SectionChanged;

        /// <summary>
        /// Current section name
        /// </summary>
        public string CurrentSection => _currentSection;

        /// <summary>
        /// Set the current section and notify subscribers
        /// </summary>
        public void SetSection(string section)
        {
            var displayName = NormalizeSection(section);
            
            if (_currentSection != displayName)
            {
                _currentSection = displayName;
                var sectionChangedEvent = new BreadcrumbEvents.SectionChanged 
                { 
                    Section = displayName 
                };
                SectionChanged?.Invoke(sectionChangedEvent);
                
                _logger?.LogDebug("Breadcrumb section changed to: {Section}", displayName);
            }
        }

        /// <summary>
        /// Clear the current section
        /// </summary>
        public void ClearSection()
        {
            SetSection(string.Empty);
        }

        /// <summary>
        /// Normalize section names for display
        /// </summary>
        private static string NormalizeSection(string section)
        {
            return section?.ToLowerInvariant() switch
            {
                "testcase" or "testcasegenerator" or "test case generator" => "Test Case Generator",
                "requirements" => "Requirements",
                "project" => "Project",
                "testflow" or "test flow" => "Test Flow",
                "import" => "Import",
                "newproject" or "new project" => "New Project",
                "" or null => string.Empty,
                _ => section ?? string.Empty
            };
        }
    }
}