using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Extensions
{
    /// <summary>
    /// Manages the discovery, loading, and lifecycle of application extensions.
    /// Provides hot-loading capabilities and dependency resolution for extensions.
    /// </summary>
    public class ExtensionManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ExtensionManager> _logger;
        private readonly Dictionary<string, IExtensionContract> _loadedExtensions = new();
        private readonly Dictionary<string, IDomainExtension> _domainExtensions = new();
        private readonly List<string> _extensionDirectories = new();
        
        public ExtensionManager(IServiceProvider serviceProvider, ILogger<ExtensionManager> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Default extension directories
            _extensionDirectories.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Extensions"));
            _extensionDirectories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                "TestCaseEditor", "Extensions"));
        }
        
        /// <summary>
        /// Currently loaded extensions
        /// </summary>
        public IReadOnlyDictionary<string, IExtensionContract> LoadedExtensions => _loadedExtensions;
        
        /// <summary>
        /// Currently loaded domain extensions
        /// </summary>
        public IReadOnlyDictionary<string, IDomainExtension> DomainExtensions => _domainExtensions;
        
        /// <summary>
        /// Add a directory to scan for extensions
        /// </summary>
        public void AddExtensionDirectory(string path)
        {
            if (Directory.Exists(path) && !_extensionDirectories.Contains(path))
            {
                _extensionDirectories.Add(path);
                _logger.LogInformation("Added extension directory: {Path}", path);
            }
        }
        
        /// <summary>
        /// Discover and load all extensions from registered directories
        /// </summary>
        public async Task<ExtensionDiscoveryResult> DiscoverAndLoadExtensionsAsync()
        {
            var result = new ExtensionDiscoveryResult();
            
            foreach (var directory in _extensionDirectories.Where(Directory.Exists))
            {
                try
                {
                    var extensionsInDir = await DiscoverExtensionsInDirectoryAsync(directory);
                    result.DiscoveredExtensions.AddRange(extensionsInDir);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error discovering extensions in directory: {Directory}", directory);
                    result.Errors.Add($"Directory {directory}: {ex.Message}");
                }
            }
            
            // Load extensions in dependency order
            var sortedExtensions = SortExtensionsByDependencies(result.DiscoveredExtensions);
            
            foreach (var extensionInfo in sortedExtensions)
            {
                try
                {
                    var loadResult = await LoadExtensionAsync(extensionInfo);
                    if (loadResult.Success)
                    {
                        result.LoadedExtensions.Add(loadResult.Extension!);
                        _logger.LogInformation("Successfully loaded extension: {ExtensionId}", loadResult.Extension?.ExtensionId);
                    }
                    else
                    {
                        result.Errors.Add($"Failed to load {extensionInfo.ExtensionId}: {loadResult.ErrorMessage}");
                        _logger.LogError("Failed to load extension {ExtensionId}: {Error}", 
                            extensionInfo.ExtensionId, loadResult.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error loading extension: {ExtensionId}", extensionInfo.ExtensionId);
                    result.Errors.Add($"Unexpected error loading {extensionInfo.ExtensionId}: {ex.Message}");
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Load a specific extension instance
        /// </summary>
        public async Task<ExtensionLoadResult> LoadExtensionAsync(ExtensionInfo extensionInfo)
        {
            try
            {
                // Load the assembly
                var assembly = Assembly.LoadFrom(extensionInfo.AssemblyPath);
                var extensionType = assembly.GetType(extensionInfo.TypeName);
                
                if (extensionType == null)
                {
                    return ExtensionLoadResult.Failed($"Extension type {extensionInfo.TypeName} not found in assembly");
                }
                
                // Create instance
                var extension = Activator.CreateInstance(extensionType) as IExtensionContract;
                if (extension == null)
                {
                    return ExtensionLoadResult.Failed($"Type {extensionInfo.TypeName} does not implement IExtensionContract");
                }
                
                // Check dependencies
                var dependencyResult = await ValidateExtensionDependenciesAsync(extension);
                if (!dependencyResult.Success)
                {
                    return ExtensionLoadResult.Failed($"Dependency validation failed: {dependencyResult.ErrorMessage}");
                }
                
                // Initialize extension
                var initResult = await extension.InitializeAsync(_serviceProvider, _logger);
                if (!initResult.Success)
                {
                    return ExtensionLoadResult.Failed($"Initialization failed: {initResult.ErrorMessage}");
                }
                
                // Register in our collections
                _loadedExtensions[extension.ExtensionId] = extension;
                
                if (extension is IDomainExtension domainExtension)
                {
                    _domainExtensions[domainExtension.DomainName] = domainExtension;
                    
                    // Validate domain compatibility
                    var compatibilityResult = await domainExtension.ValidateCompatibilityAsync(_domainExtensions.Values.ToList());
                    if (!compatibilityResult.IsCompatible)
                    {
                        await UnloadExtensionAsync(extension.ExtensionId);
                        return ExtensionLoadResult.Failed($"Domain compatibility failed: {string.Join(", ", compatibilityResult.Conflicts)}");
                    }
                }
                
                return ExtensionLoadResult.Successful(extension);
            }
            catch (Exception ex)
            {
                return ExtensionLoadResult.Failed($"Exception during load: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Unload a specific extension
        /// </summary>
        public async Task<bool> UnloadExtensionAsync(string extensionId)
        {
            if (!_loadedExtensions.TryGetValue(extensionId, out var extension))
                return false;
                
            try
            {
                await extension.ShutdownAsync();
                _loadedExtensions.Remove(extensionId);
                
                if (extension is IDomainExtension domainExtension)
                {
                    _domainExtensions.Remove(domainExtension.DomainName);
                }
                
                _logger.LogInformation("Successfully unloaded extension: {ExtensionId}", extensionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unloading extension: {ExtensionId}", extensionId);
                return false;
            }
        }
        
        private async Task<List<ExtensionInfo>> DiscoverExtensionsInDirectoryAsync(string directory)
        {
            await Task.CompletedTask;
            var extensions = new List<ExtensionInfo>();
            
            foreach (var file in Directory.GetFiles(directory, "*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(file);
                    var extensionTypes = assembly.GetTypes()
                        .Where(t => t.IsClass && !t.IsAbstract && typeof(IExtensionContract).IsAssignableFrom(t))
                        .ToList();
                    
                    foreach (var type in extensionTypes)
                    {
                        extensions.Add(new ExtensionInfo
                        {
                            ExtensionId = type.FullName ?? type.Name,
                            AssemblyPath = file,
                            TypeName = type.FullName!,
                            AssemblyName = assembly.FullName ?? "Unknown"
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not inspect assembly {File} for extensions: {Error}", file, ex.Message);
                }
            }
            
            return extensions;
        }
        
        private List<ExtensionInfo> SortExtensionsByDependencies(List<ExtensionInfo> extensions)
        {
            // Simple topological sort would go here
            // For Phase 7, we'll use simple ordering
            return extensions.OrderBy(e => e.ExtensionId).ToList();
        }
        
        private async Task<ValidationResult> ValidateExtensionDependenciesAsync(IExtensionContract extension)
        {
            await Task.CompletedTask;
            foreach (var dependency in extension.Dependencies)
            {
                if (!_loadedExtensions.ContainsKey(dependency))
                {
                    return ValidationResult.Failed($"Missing dependency: {dependency}");
                }
            }
            
            return ValidationResult.Successful();
        }
    }
    
    // Supporting classes
    public class ExtensionInfo
    {
        public string ExtensionId { get; init; } = string.Empty;
        public string AssemblyPath { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public string AssemblyName { get; init; } = string.Empty;
    }
    
    public class ExtensionDiscoveryResult
    {
        public List<ExtensionInfo> DiscoveredExtensions { get; } = new();
        public List<IExtensionContract> LoadedExtensions { get; } = new();
        public List<string> Errors { get; } = new();
    }
    
    public class ExtensionLoadResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public IExtensionContract? Extension { get; init; }
        
        public static ExtensionLoadResult Successful(IExtensionContract extension) =>
            new() { Success = true, Extension = extension };
            
        public static ExtensionLoadResult Failed(string error) =>
            new() { Success = false, ErrorMessage = error };
    }
    
    public class ValidationResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        
        public static ValidationResult Successful() => new() { Success = true };
        public static ValidationResult Failed(string error) => new() { Success = false, ErrorMessage = error };
    }
}