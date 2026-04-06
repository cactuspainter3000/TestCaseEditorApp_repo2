using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service discovery interface for finding services provided by extensions or domains.
    /// Enables loose coupling between domains and dynamic service resolution.
    /// </summary>
    public interface IServiceDiscovery
    {
        /// <summary>
        /// Find all services that implement a specific interface
        /// </summary>
        IReadOnlyList<T> FindServices<T>() where T : class;
        
        /// <summary>
        /// Find a specific service by name and type
        /// </summary>
        T? FindService<T>(string serviceName) where T : class;
        
        /// <summary>
        /// Find services provided by a specific domain
        /// </summary>
        IReadOnlyList<T> FindServicesByDomain<T>(string domainName) where T : class;
        
        /// <summary>
        /// Check if a service type is available
        /// </summary>
        bool IsServiceAvailable<T>() where T : class;
        
        /// <summary>
        /// Get metadata about available services
        /// </summary>
        IReadOnlyList<ServiceMetadata> GetServiceMetadata();
        
        /// <summary>
        /// Subscribe to notifications when new services are registered
        /// </summary>
        void SubscribeToServiceRegistrations<T>(Action<T> onServiceRegistered) where T : class;
    }
    
    /// <summary>
    /// Metadata about a registered service
    /// </summary>
    public class ServiceMetadata
    {
        public Type ServiceType { get; init; } = typeof(object);
        public Type ImplementationType { get; init; } = typeof(object);
        public string ServiceName { get; init; } = string.Empty;
        public string DomainName { get; init; } = string.Empty;
        public ServiceLifetime Lifetime { get; init; }
        public bool IsExtensionProvided { get; init; }
        public Dictionary<string, object> Properties { get; init; } = new();
    }
    
    /// <summary>
    /// Implementation of service discovery using the DI container and extension metadata.
    /// </summary>
    public class ServiceDiscovery : IServiceDiscovery
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ServiceDiscovery> _logger;
        private readonly Dictionary<Type, List<ServiceRegistrationInfo>> _serviceRegistrations = new();
        private readonly Dictionary<Type, List<Action<object>>> _subscriptions = new();
        
        public ServiceDiscovery(IServiceProvider serviceProvider, ILogger<ServiceDiscovery> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Find all services that implement a specific interface
        /// </summary>
        public IReadOnlyList<T> FindServices<T>() where T : class
        {
            var services = new List<T>();
            
            try
            {
                var allServices = _serviceProvider.GetServices<T>();
                services.AddRange(allServices);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error finding services of type {ServiceType}", typeof(T).Name);
            }
            
            return services;
        }
        
        /// <summary>
        /// Find a specific service by name and type
        /// </summary>
        public T? FindService<T>(string serviceName) where T : class
        {
            if (!_serviceRegistrations.TryGetValue(typeof(T), out var registrations))
                return null;
                
            var registration = registrations.FirstOrDefault(r => r.ServiceName == serviceName);
            if (registration == null)
                return null;
                
            try
            {
                return _serviceProvider.GetService<T>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error retrieving service {ServiceName} of type {ServiceType}", 
                    serviceName, typeof(T).Name);
                return null;
            }
        }
        
        /// <summary>
        /// Find services provided by a specific domain
        /// </summary>
        public IReadOnlyList<T> FindServicesByDomain<T>(string domainName) where T : class
        {
            if (!_serviceRegistrations.TryGetValue(typeof(T), out var registrations))
                return Array.Empty<T>();
                
            var domainRegistrations = registrations.Where(r => r.DomainName == domainName).ToList();
            var services = new List<T>();
            
            foreach (var registration in domainRegistrations)
            {
                try
                {
                    var service = _serviceProvider.GetService<T>();
                    if (service != null)
                        services.Add(service);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error retrieving domain service {ServiceName} from {Domain}", 
                        registration.ServiceName, domainName);
                }
            }
            
            return services;
        }
        
        /// <summary>
        /// Check if a service type is available
        /// </summary>
        public bool IsServiceAvailable<T>() where T : class
        {
            try
            {
                var service = _serviceProvider.GetService<T>();
                return service != null;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Get metadata about available services
        /// </summary>
        public IReadOnlyList<ServiceMetadata> GetServiceMetadata()
        {
            var metadata = new List<ServiceMetadata>();
            
            foreach (var kvp in _serviceRegistrations)
            {
                foreach (var registration in kvp.Value)
                {
                    metadata.Add(new ServiceMetadata
                    {
                        ServiceType = kvp.Key,
                        ImplementationType = registration.ImplementationType,
                        ServiceName = registration.ServiceName,
                        DomainName = registration.DomainName,
                        Lifetime = registration.Lifetime,
                        IsExtensionProvided = registration.IsExtensionProvided,
                        Properties = new Dictionary<string, object>(registration.Properties)
                    });
                }
            }
            
            return metadata;
        }
        
        /// <summary>
        /// Subscribe to notifications when new services are registered
        /// </summary>
        public void SubscribeToServiceRegistrations<T>(Action<T> onServiceRegistered) where T : class
        {
            var serviceType = typeof(T);
            
            if (!_subscriptions.ContainsKey(serviceType))
                _subscriptions[serviceType] = new List<Action<object>>();
                
            _subscriptions[serviceType].Add(service => onServiceRegistered((T)service));
        }
        
        /// <summary>
        /// Register a service and notify subscribers
        /// </summary>
        public void RegisterService<T>(string serviceName, string domainName, Type implementationType, 
            ServiceLifetime lifetime, bool isExtensionProvided = false, Dictionary<string, object>? properties = null)
            where T : class
        {
            var serviceType = typeof(T);
            
            if (!_serviceRegistrations.ContainsKey(serviceType))
                _serviceRegistrations[serviceType] = new List<ServiceRegistrationInfo>();
                
            var registration = new ServiceRegistrationInfo
            {
                ServiceName = serviceName,
                DomainName = domainName,
                ImplementationType = implementationType,
                Lifetime = lifetime,
                IsExtensionProvided = isExtensionProvided,
                Properties = properties ?? new Dictionary<string, object>()
            };
            
            _serviceRegistrations[serviceType].Add(registration);
            
            // Notify subscribers
            if (_subscriptions.TryGetValue(serviceType, out var subscribers))
            {
                try
                {
                    var service = _serviceProvider.GetService<T>();
                    if (service != null)
                    {
                        foreach (var subscriber in subscribers)
                        {
                            try
                            {
                                subscriber(service);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error notifying service registration subscriber for {ServiceType}", 
                                    serviceType.Name);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error retrieving registered service {ServiceType} for notification", 
                        serviceType.Name);
                }
            }
            
            _logger.LogDebug("Registered service {ServiceType} as {ServiceName} from domain {Domain}", 
                serviceType.Name, serviceName, domainName);
        }
        
        private class ServiceRegistrationInfo
        {
            public string ServiceName { get; init; } = string.Empty;
            public string DomainName { get; init; } = string.Empty;
            public Type ImplementationType { get; init; } = typeof(object);
            public ServiceLifetime Lifetime { get; init; }
            public bool IsExtensionProvided { get; init; }
            public Dictionary<string, object> Properties { get; init; } = new();
        }
    }
}