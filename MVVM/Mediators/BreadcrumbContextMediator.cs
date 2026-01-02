using System;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Events;

namespace TestCaseEditorApp.MVVM.Mediators
{
    /// <summary>
    /// Mediator for context-level breadcrumb (specific requirement, test case, etc.)
    /// Handles the third level of navigation context.
    /// </summary>
    public class BreadcrumbContextMediator
    {
        private readonly ILogger<BreadcrumbContextMediator>? _logger;
        private string? _currentContext;

        public BreadcrumbContextMediator(ILogger<BreadcrumbContextMediator>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Event fired when context changes
        /// </summary>
        public event Action<BreadcrumbEvents.ContextChanged>? ContextChanged;

        /// <summary>
        /// Current context (requirement ID, test case, etc.)
        /// </summary>
        public string? CurrentContext => _currentContext;

        /// <summary>
        /// Set the current context and notify subscribers
        /// </summary>
        public void SetContext(string? context)
        {
            var displayName = NormalizeContext(context);
            
            if (_currentContext != displayName)
            {
                _currentContext = displayName;
                var contextChangedEvent = new BreadcrumbEvents.ContextChanged 
                { 
                    Context = displayName 
                };
                ContextChanged?.Invoke(contextChangedEvent);
                
                _logger?.LogDebug("Breadcrumb context changed to: {Context}", displayName ?? "<none>");
            }
        }

        /// <summary>
        /// Clear the current context
        /// </summary>
        public void ClearContext()
        {
            SetContext(null);
        }

        /// <summary>
        /// Normalize context names for display
        /// </summary>
        private static string? NormalizeContext(string? context)
        {
            if (string.IsNullOrWhiteSpace(context))
                return null;

            var trimmed = context.Trim();
            
            // Limit length for display
            if (trimmed.Length > 50)
            {
                return trimmed.Substring(0, 47) + "...";
            }
            
            return trimmed;
        }
    }
}