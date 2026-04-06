using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace TestCaseEditorApp.MVVM.Extensions
{
    /// <summary>
    /// Contract for domain extensions that provide specialized domain logic and services.
    /// Allows extending the application with new domains beyond TestCaseGeneration and TestFlow.
    /// </summary>
    public interface IDomainExtension : IExtensionContract
    {
        /// <summary>
        /// The domain namespace this extension handles
        /// </summary>
        string DomainName { get; }
        
        /// <summary>
        /// Main menu items this domain should add to the application
        /// </summary>
        IReadOnlyList<DomainMenuItemDescriptor> MenuItems { get; }
        
        /// <summary>
        /// Register domain-specific services and mediators
        /// </summary>
        Task RegisterDomainServicesAsync(IDomainServiceRegistry registry);
        
        /// <summary>
        /// Create the domain mediator for this extension
        /// </summary>
        object CreateDomainMediator(IServiceProvider serviceProvider);
        
        /// <summary>
        /// Validate that this domain extension can coexist with others
        /// </summary>
        Task<DomainValidationResult> ValidateCompatibilityAsync(IReadOnlyList<IDomainExtension> existingDomains);
    }
    
    /// <summary>
    /// Descriptor for menu items a domain wants to add
    /// </summary>
    public class DomainMenuItemDescriptor
    {
        public string MenuId { get; init; } = string.Empty;
        public string DisplayText { get; init; } = string.Empty;
        public string IconResource { get; init; } = string.Empty;
        public int SortOrder { get; init; }
        public string NavigationTarget { get; init; } = string.Empty;
        public List<DomainMenuItemDescriptor> SubItems { get; init; } = new();
    }
    
    /// <summary>
    /// Result of domain compatibility validation
    /// </summary>
    public class DomainValidationResult
    {
        public bool IsCompatible { get; init; }
        public List<string> Conflicts { get; init; } = new();
        public List<string> Warnings { get; init; } = new();
        
        public static DomainValidationResult Compatible() => new() { IsCompatible = true };
        public static DomainValidationResult Incompatible(params string[] conflicts) => 
            new() { IsCompatible = false, Conflicts = new List<string>(conflicts) };
    }
    
    /// <summary>
    /// Registry for domain-specific services
    /// </summary>
    public interface IDomainServiceRegistry
    {
        /// <summary>
        /// Register a service that this domain provides
        /// </summary>
        void RegisterService<TInterface, TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TImplementation : class, TInterface
            where TInterface : class;
            
        /// <summary>
        /// Register a singleton instance
        /// </summary>
        void RegisterInstance<TInterface>(TInterface instance) where TInterface : class;
        
        /// <summary>
        /// Register a factory function for creating services
        /// </summary>
        void RegisterFactory<TInterface>(Func<IServiceProvider, TInterface> factory) 
            where TInterface : class;
    }
}