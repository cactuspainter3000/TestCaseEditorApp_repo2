using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Extensions
{
    /// <summary>
    /// Implementation of domain service registry for extension-provided services.
    /// Allows extensions to register services dynamically at runtime.
    /// </summary>
    public class DomainServiceRegistry : IDomainServiceRegistry
    {
        private readonly IServiceCollection _services;
        private readonly ILogger<DomainServiceRegistry> _logger;
        private readonly string _domainName;
        private readonly Dictionary<Type, ServiceDescriptor> _registeredServices = new();
        
        public DomainServiceRegistry(IServiceCollection services, string domainName, ILogger<DomainServiceRegistry> logger)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _domainName = domainName ?? throw new ArgumentNullException(nameof(domainName));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Services registered by this domain
        /// </summary>
        public IReadOnlyDictionary<Type, ServiceDescriptor> RegisteredServices => _registeredServices;
        
        /// <summary>
        /// The domain this registry belongs to
        /// </summary>
        public string DomainName => _domainName;
        
        /// <summary>
        /// Register a service that this domain provides
        /// </summary>
        public void RegisterService<TInterface, TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TImplementation : class, TInterface
            where TInterface : class
        {
            var serviceType = typeof(TInterface);
            var implementationType = typeof(TImplementation);
            
            if (_registeredServices.ContainsKey(serviceType))
            {
                _logger.LogWarning("Domain {Domain} is re-registering service {ServiceType}", _domainName, serviceType.Name);
            }
            
            var descriptor = ServiceDescriptor.Describe(serviceType, implementationType, lifetime);
            _services.Add(descriptor);
            _registeredServices[serviceType] = descriptor;
            
            _logger.LogDebug("Domain {Domain} registered service {ServiceType} -> {ImplementationType} ({Lifetime})", 
                _domainName, serviceType.Name, implementationType.Name, lifetime);
        }
        
        /// <summary>
        /// Register a singleton instance
        /// </summary>
        public void RegisterInstance<TInterface>(TInterface instance) where TInterface : class
        {
            var serviceType = typeof(TInterface);
            
            if (_registeredServices.ContainsKey(serviceType))
            {
                _logger.LogWarning("Domain {Domain} is re-registering service instance {ServiceType}", _domainName, serviceType.Name);
            }
            
            var descriptor = ServiceDescriptor.Singleton<TInterface>(instance);
            _services.Add(descriptor);
            _registeredServices[serviceType] = descriptor;
            
            _logger.LogDebug("Domain {Domain} registered singleton instance {ServiceType}", _domainName, serviceType.Name);
        }
        
        /// <summary>
        /// Register a factory function for creating services
        /// </summary>
        public void RegisterFactory<TInterface>(Func<IServiceProvider, TInterface> factory) 
            where TInterface : class
        {
            var serviceType = typeof(TInterface);
            
            if (_registeredServices.ContainsKey(serviceType))
            {
                _logger.LogWarning("Domain {Domain} is re-registering service factory {ServiceType}", _domainName, serviceType.Name);
            }
            
            var descriptor = ServiceDescriptor.Scoped<TInterface>(factory);
            _services.Add(descriptor);
            _registeredServices[serviceType] = descriptor;
            
            _logger.LogDebug("Domain {Domain} registered factory for {ServiceType}", _domainName, serviceType.Name);
        }
        
        /// <summary>
        /// Remove all services registered by this domain
        /// </summary>
        public void UnregisterAllServices()
        {
            foreach (var kvp in _registeredServices)
            {
                _services.Remove(kvp.Value);
                _logger.LogDebug("Domain {Domain} unregistered service {ServiceType}", _domainName, kvp.Key.Name);
            }
            
            _registeredServices.Clear();
        }
    }
}