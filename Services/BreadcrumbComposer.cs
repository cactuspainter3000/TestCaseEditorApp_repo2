using System;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Mediators;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Composes the final breadcrumb from all three levels and publishes complete breadcrumb events.
    /// Subscribes to section, project, and context mediators to build the full navigation path.
    /// </summary>
    public class BreadcrumbComposer : IDisposable
    {
        private readonly BreadcrumbSectionMediator _sectionMediator;
        private readonly BreadcrumbProjectMediator _projectMediator;
        private readonly BreadcrumbContextMediator _contextMediator;
        private readonly ILogger<BreadcrumbComposer>? _logger;

        private string _section = string.Empty;
        private string? _project;
        private string? _context;

        /// <summary>
        /// Event fired when the complete breadcrumb changes
        /// </summary>
        public event Action<string>? BreadcrumbChanged;

        public BreadcrumbComposer(
            BreadcrumbSectionMediator sectionMediator,
            BreadcrumbProjectMediator projectMediator, 
            BreadcrumbContextMediator contextMediator,
            ILogger<BreadcrumbComposer>? logger = null)
        {
            _sectionMediator = sectionMediator ?? throw new ArgumentNullException(nameof(sectionMediator));
            _projectMediator = projectMediator ?? throw new ArgumentNullException(nameof(projectMediator));
            _contextMediator = contextMediator ?? throw new ArgumentNullException(nameof(contextMediator));
            _logger = logger;

            // Subscribe to all three levels
            _sectionMediator.SectionChanged += OnSectionChanged;
            _projectMediator.ProjectChanged += OnProjectChanged;
            _contextMediator.ContextChanged += OnContextChanged;

            // Initialize current state
            _section = _sectionMediator.CurrentSection;
            _project = _projectMediator.CurrentProject;
            _context = _contextMediator.CurrentContext;

            _logger?.LogDebug("BreadcrumbComposer initialized");
        }

        /// <summary>
        /// Current complete breadcrumb
        /// </summary>
        public string FullBreadcrumb => ComposeBreadcrumb();

        private void OnSectionChanged(BreadcrumbEvents.SectionChanged sectionChanged)
        {
            _section = sectionChanged.Section;
            NotifyBreadcrumbChanged();
        }

        private void OnProjectChanged(BreadcrumbEvents.ProjectChanged projectChanged)
        {
            _project = projectChanged.Project;
            NotifyBreadcrumbChanged();
        }

        private void OnContextChanged(BreadcrumbEvents.ContextChanged contextChanged)
        {
            _context = contextChanged.Context;
            NotifyBreadcrumbChanged();
        }

        private string ComposeBreadcrumb()
        {
            var breadcrumb = "Systems App";
            
            if (!string.IsNullOrEmpty(_section))
            {
                breadcrumb += $" → {_section}";
            }
            
            if (!string.IsNullOrEmpty(_project))
            {
                breadcrumb += $" → {_project}";
            }
            
            if (!string.IsNullOrEmpty(_context))
            {
                breadcrumb += $" → {_context}";
            }
            
            return breadcrumb;
        }

        private void NotifyBreadcrumbChanged()
        {
            var fullBreadcrumb = ComposeBreadcrumb();
            BreadcrumbChanged?.Invoke(fullBreadcrumb);
            
            _logger?.LogDebug("Complete breadcrumb: {Breadcrumb}", fullBreadcrumb);
        }

        public void Dispose()
        {
            _sectionMediator.SectionChanged -= OnSectionChanged;
            _projectMediator.ProjectChanged -= OnProjectChanged;
            _contextMediator.ContextChanged -= OnContextChanged;
            
            _logger?.LogDebug("BreadcrumbComposer disposed");
        }
    }
}