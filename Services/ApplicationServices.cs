using System;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Default implementation of application services facade.
    /// Consolidates related services to simplify dependency injection and reduce constructor complexity.
    /// </summary>
    public class ApplicationServices : IApplicationServices
    {
        public IRequirementService RequirementService { get; }
        public IPersistenceService PersistenceService { get; }
        public IFileDialogService FileDialogService { get; }
        public ITextEditingDialogService TextEditingDialogService { get; }
        public ToastNotificationService ToastService { get; }
        public NotificationService NotificationService { get; }
        public AnythingLLMService AnythingLLMService { get; }
        public ChatGptExportService ChatGptExportService { get; }
        public IRequirementAnalysisService RequirementAnalysisService { get; }
        public ILoggerFactory? LoggerFactory { get; }
        public JamaConnectService JamaConnectService { get; }

        public ApplicationServices(
            IRequirementService requirementService,
            IPersistenceService persistenceService,
            IFileDialogService fileDialogService,
            ITextEditingDialogService textEditingDialogService,
            ToastNotificationService toastService,
            NotificationService notificationService,
            AnythingLLMService anythingLLMService,
            ChatGptExportService chatGptExportService,
            IRequirementAnalysisService requirementAnalysisService,
            JamaConnectService jamaConnectService,
            ILoggerFactory? loggerFactory = null)
        {
            RequirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            PersistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
            FileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            TextEditingDialogService = textEditingDialogService ?? throw new ArgumentNullException(nameof(textEditingDialogService));
            ToastService = toastService ?? throw new ArgumentNullException(nameof(toastService));
            NotificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            AnythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            ChatGptExportService = chatGptExportService ?? throw new ArgumentNullException(nameof(chatGptExportService));
            RequirementAnalysisService = requirementAnalysisService ?? throw new ArgumentNullException(nameof(requirementAnalysisService));
            JamaConnectService = jamaConnectService ?? throw new ArgumentNullException(nameof(jamaConnectService));
            LoggerFactory = loggerFactory;
        }
    }
}