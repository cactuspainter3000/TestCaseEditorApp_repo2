using System;
using System.Collections.Generic;

namespace TestCaseEditorApp.MVVM.Utils
{
    /// <summary>
    /// Navigation events for the mediator pattern
    /// </summary>
    public static class NavigationEvents
    {
        public class SectionChangeRequested
        {
            public string SectionName { get; }
            public object? Context { get; }
            
            public SectionChangeRequested(string sectionName, object? context = null)
            {
                SectionName = sectionName;
                Context = context;
            }
        }
        
        public class StepChangeRequested
        {
            public string StepId { get; }
            public object? Context { get; }
            
            public StepChangeRequested(string stepId, object? context = null)
            {
                StepId = stepId;
                Context = context;
            }
        }
        
        public class HeaderChanged
        {
            public object? HeaderViewModel { get; }
            
            public HeaderChanged(object? headerViewModel)
            {
                HeaderViewModel = headerViewModel;
            }
        }
        
        public class ContentChanged
        {
            public object? ContentViewModel { get; }
            
            public ContentChanged(object? contentViewModel)
            {
                ContentViewModel = contentViewModel;
            }
        }
        
        public class SectionChanged
        {
            public string? PreviousSection { get; }
            public string? NewSection { get; }
            
            public SectionChanged(string? previousSection, string? newSection)
            {
                PreviousSection = previousSection;
                NewSection = newSection;
            }
        }
        
        public class NavigationCleared
        {
            public string? PreviousSection { get; }
            
            public NavigationCleared(string? previousSection)
            {
                PreviousSection = previousSection;
            }
        }
    }
}