using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service facade interface that consolidates core application services.
    /// This reduces MainViewModel constructor complexity and provides a clean abstraction layer.
    /// </summary>
    public interface IApplicationServices
    {
        // Core business services
        IRequirementService RequirementService { get; }
        IPersistenceService PersistenceService { get; }
        IFileDialogService FileDialogService { get; }
        ITextEditingDialogService TextEditingDialogService { get; }
        
        // Notification and UI services
        ToastNotificationService ToastService { get; }
        NotificationService NotificationService { get; }
        
        // AI/LLM services
        AnythingLLMService AnythingLLMService { get; }
        ChatGptExportService ChatGptExportService { get; }
        IRequirementAnalysisService RequirementAnalysisService { get; }
        
        // External integration services
        JamaConnectService JamaConnectService { get; }
        
        // Logging factory for creating typed loggers
        ILoggerFactory? LoggerFactory { get; }
    }
}