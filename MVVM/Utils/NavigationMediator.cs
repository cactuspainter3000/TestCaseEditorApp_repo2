using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Utils
{
    /// <summary>
    /// Navigation mediator implementation using message-based communication.
    /// Eliminates circular dependencies by decoupling navigation requests from direct ViewModel management.
    /// </summary>
    public class NavigationMediator : INavigationMediator
    {
        private readonly ConcurrentDictionary<Type, List<object>> _subscribers = new();
        private readonly ILogger? _logger;
        
        // Current state tracking
        private string? _currentSection;
        private object? _currentHeader;
        private object? _currentContent;
        
        public NavigationMediator(ILogger? logger = null)
        {
            _logger = logger;
        }
        
        public void NavigateToSection(string sectionName, object? context = null)
        {
            var previousSection = _currentSection;
            // DON'T update _currentSection yet - let the coordinator decide if navigation should proceed
            
            System.Diagnostics.Debug.WriteLine($"*** NavigationMediator: NavigateToSection('{sectionName}') called ***");
            Console.WriteLine($"*** NavigationMediator: NavigateToSection('{sectionName}') called ***");
            
            // Write to log file for easier debugging
            System.IO.File.AppendAllText(@"c:\temp\navigation-debug.log", 
                $"[{DateTime.Now:HH:mm:ss}] NavigationMediator: NavigateToSection('{sectionName}') called\n");
            
            _logger?.LogDebug("Navigation request: {PreviousSection} -> {NewSection}", 
                previousSection, sectionName);
            
            // Publish section change request - coordinator will decide if it should proceed
            System.Diagnostics.Debug.WriteLine($"*** NavigationMediator: Publishing SectionChangeRequested ***");
            Console.WriteLine($"*** NavigationMediator: Publishing SectionChangeRequested for '{sectionName}' ***");
            
            System.IO.File.AppendAllText(@"c:\temp\navigation-debug.log", 
                $"[{DateTime.Now:HH:mm:ss}] NavigationMediator: Publishing SectionChangeRequested for '{sectionName}'\n");
                
            Publish(new NavigationEvents.SectionChangeRequested(sectionName, context));
        }
        
        /// <summary>
        /// Internal method for ViewAreaCoordinator to confirm navigation completion and update current section
        /// </summary>
        internal void CompleteNavigation(string sectionName)
        {
            var previousSection = _currentSection;
            _currentSection = sectionName;
            
            System.Diagnostics.Debug.WriteLine($"*** NavigationMediator: CompleteNavigation - Publishing SectionChanged('{previousSection}' -> '{sectionName}') ***");
            _logger?.LogDebug("Navigation completed: {PreviousSection} -> {NewSection}", 
                previousSection, sectionName);
                
            // Publish section changed notification
            Publish(new NavigationEvents.SectionChanged(previousSection, sectionName));
        }
        
        public void NavigateToStep(string stepId, object? context = null)
        {
            _logger?.LogDebug("Step navigation request: {StepId}", stepId);
            
            Publish(new NavigationEvents.StepChangeRequested(stepId, context));
        }
        
        public void SetActiveHeader(object? headerViewModel)
        {
            if (_currentHeader != headerViewModel)
            {
                _currentHeader = headerViewModel;
                
                _logger?.LogDebug("Header changed to: {HeaderType}", 
                    headerViewModel?.GetType().Name ?? "<null>");
                
                Publish(new NavigationEvents.HeaderChanged(headerViewModel));
            }
        }
        
        public void SetMainContent(object? contentViewModel)
        {
            if (_currentContent != contentViewModel)
            {
                _currentContent = contentViewModel;
                
                _logger?.LogDebug("Content changed to: {ContentType}", 
                    contentViewModel?.GetType().Name ?? "<null>");
                
                Publish(new NavigationEvents.ContentChanged(contentViewModel));
            }
        }
        
        /// <summary>
        /// Clear all navigation state and return to initial state
        /// </summary>
        public void ClearNavigationState()
        {
            _logger?.LogInformation("Clearing navigation state");
            
            var previousSection = _currentSection;
            _currentSection = null;
            _currentHeader = null;
            _currentContent = null;
            
            // Publish clear events
            Publish(new NavigationEvents.NavigationCleared(previousSection));
            Publish(new NavigationEvents.HeaderChanged(null));
            Publish(new NavigationEvents.ContentChanged(null));
            Publish(new NavigationEvents.SectionChanged(previousSection, null));
        }
        
        public void Subscribe<T>(Action<T> handler) where T : class
        {
            var eventType = typeof(T);
            System.Diagnostics.Debug.WriteLine($"*** NavigationMediator.Subscribe<{eventType.Name}>: Subscribing handler ***");
            
            var handlers = _subscribers.GetOrAdd(eventType, _ => new List<object>());
            
            lock (handlers)
            {
                handlers.Add(handler);
            }
            
            System.Diagnostics.Debug.WriteLine($"*** NavigationMediator.Subscribe<{eventType.Name}>: Total handlers now: {handlers.Count} ***");
            
            _logger?.LogTrace("Subscribed to {EventType}, total handlers: {Count}", 
                eventType.Name, handlers.Count);
        }
        
        public void Unsubscribe<T>(Action<T> handler) where T : class
        {
            var eventType = typeof(T);
            
            if (_subscribers.TryGetValue(eventType, out var handlers))
            {
                lock (handlers)
                {
                    handlers.Remove(handler);
                }
                
                _logger?.LogTrace("Unsubscribed from {EventType}, remaining handlers: {Count}", 
                    eventType.Name, handlers.Count);
            }
        }
        
        public void Publish<T>(T navigationEvent) where T : class
        {
            var eventType = typeof(T);
            
            // DEBUG: Add detailed diagnostic logging
            System.Diagnostics.Debug.WriteLine($"*** NavigationMediator.Publish<{eventType.Name}>: Checking for handlers ***");
            
            if (!_subscribers.TryGetValue(eventType, out var handlers))
            {
                _logger?.LogTrace("No handlers for {EventType}", eventType.Name);
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"*** NavigationMediator.Publish<{eventType.Name}>: Found {handlers.Count} handlers ***");
            
            System.Diagnostics.Debug.WriteLine($"*** NavigationMediator.Publish<{eventType.Name}>: Found {handlers.Count} handlers ***");

            List<object> handlersCopy;
            lock (handlers)
            {
                handlersCopy = new List<object>(handlers);
            }

            _logger?.LogTrace("Publishing {EventType} to {Count} handlers", 
                eventType.Name, handlersCopy.Count);

            System.Diagnostics.Debug.WriteLine($"*** NavigationMediator.Publish<{eventType.Name}>: Invoking {handlersCopy.Count} handlers ***");
            
            foreach (var handler in handlersCopy)
            {
                try
                {
                    if (handler is Action<T> typedHandler)
                    {
                        System.Diagnostics.Debug.WriteLine($"*** NavigationMediator.Publish<{eventType.Name}>: Invoking handler ***");
                        typedHandler(navigationEvent);
                        System.Diagnostics.Debug.WriteLine($"*** NavigationMediator.Publish<{eventType.Name}>: Handler completed ***");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"*** NavigationMediator.Publish<{eventType.Name}>: Handler type mismatch! ***");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"*** NavigationMediator.Publish<{eventType.Name}>: Handler threw exception: {ex.Message} ***");
                    _logger?.LogError(ex, "Error handling {EventType}", eventType.Name);
                }
            }
        }
        
        // Public accessors for current state (read-only)
        public string? CurrentSection => _currentSection;
        public object? CurrentHeader => _currentHeader;
        public object? CurrentContent => _currentContent;
    }
}