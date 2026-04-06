using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Extensions
{
    /// <summary>
    /// Base contract for all extensions that can be loaded into the Test Case Editor.
    /// Provides standardized lifecycle management and integration points.
    /// </summary>
    public interface IExtensionContract
    {
        /// <summary>
        /// Unique identifier for this extension
        /// </summary>
        string ExtensionId { get; }
        
        /// <summary>
        /// Human-readable name for this extension
        /// </summary>
        string DisplayName { get; }
        
        /// <summary>
        /// Version of this extension
        /// </summary>
        Version Version { get; }
        
        /// <summary>
        /// Description of what this extension provides
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// Extensions this extension depends on
        /// </summary>
        IReadOnlyList<string> Dependencies { get; }
        
        /// <summary>
        /// Initialize the extension with the application's service provider
        /// </summary>
        Task<ExtensionInitializationResult> InitializeAsync(IServiceProvider serviceProvider, ILogger logger);
        
        /// <summary>
        /// Configure services that this extension provides
        /// </summary>
        void ConfigureServices(IServiceCollection services);
        
        /// <summary>
        /// Clean up resources when the extension is being unloaded
        /// </summary>
        Task ShutdownAsync();
    }
    
    /// <summary>
    /// Result of extension initialization
    /// </summary>
    public class ExtensionInitializationResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public ExtensionCapabilities Capabilities { get; init; } = new();
        
        public static ExtensionInitializationResult Successful(ExtensionCapabilities? capabilities = null) =>
            new() { Success = true, Capabilities = capabilities ?? new() };
            
        public static ExtensionInitializationResult Failed(string errorMessage) =>
            new() { Success = false, ErrorMessage = errorMessage };
    }
    
    /// <summary>
    /// Capabilities provided by an extension
    /// </summary>
    public class ExtensionCapabilities
    {
        public bool ProvidesDomainServices { get; init; }
        public bool ProvidesUIComponents { get; init; }
        public bool ProvidesImportExportHandlers { get; init; }
        public bool ProvidesVerificationMethods { get; init; }
        public bool ProvidesAnalysisEngines { get; init; }
        public List<string> SupportedFileTypes { get; init; } = new();
        public List<string> ProvidedServiceTypes { get; init; } = new();
    }
}