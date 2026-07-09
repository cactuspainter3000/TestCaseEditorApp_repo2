using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using TestCaseEditorApp.MVVM.Domains.Startup.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators;
using TestCaseEditorApp.MVVM.Domains.Title.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels;
using TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels;
using TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Startup.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Shared.ViewModels;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services.DependencyInjection
{
    /// <summary>
    /// ViewModel registrations: UI models for all application domains.
    /// Most are TRANSIENT (per-instance) unless they maintain persistent state (SINGLETON).
    /// 
    /// SINGLETON ViewModels: Require persistent state across UI operations
    /// - TitleViewModel, SideMenuViewModel, workspace singletons (maintain selection/scroll state)
    /// 
    /// TRANSIENT ViewModels: New instance per use, no shared state required
    /// - Dialog ViewModels, temporary workspace ViewModels
    /// </summary>
    public static class ViewModelExtensions
    {
        public static IServiceCollection AddViewModels(this IServiceCollection services)
        {
            // Settings ViewModel (TRANSIENT - dialog instance)
            services.AddTransient<UserSettingsViewModel>();

            // Title ViewModel (SINGLETON - maintains application title state)
            services.AddSingleton<TitleViewModel>();

            // Side Menu ViewModel (SINGLETON - maintains navigation state and recent files)
            services.AddSingleton<SideMenuViewModel>(provider =>
            {
                var newProjectMediator = provider.GetRequiredService<INewProjectMediator>();
                var openProjectMediator = provider.GetRequiredService<IOpenProjectMediator>();
                var navigationMediator = provider.GetRequiredService<INavigationMediator>();
                var testCaseGenerationMediator = provider.GetRequiredService<ITestCaseGenerationMediator>();
                var requirementsMediator = provider.GetRequiredService<IRequirementsMediator>();
                var testCaseAnythingLLMService = provider.GetRequiredService<TestCaseAnythingLLMService>();
                var jamaConnectService = provider.GetRequiredService<JamaConnectService>();
                var requirementService = provider.GetRequiredService<IRequirementService>();
                var jamaTestCaseConversionService = provider.GetRequiredService<IJamaTestCaseConversionService>();
                var userSettingsService = provider.GetRequiredService<IUserSettingsService>();
                var settingsDialogService = provider.GetRequiredService<ISettingsDialogService>();
                var logger = provider.GetRequiredService<ILogger<SideMenuViewModel>>();

                return new SideMenuViewModel(newProjectMediator, openProjectMediator, navigationMediator,
                    testCaseGenerationMediator, requirementsMediator, testCaseAnythingLLMService, jamaConnectService, 
                    requirementService, jamaTestCaseConversionService, userSettingsService, settingsDialogService, logger);
            });

            // Workspace Header ViewModel (SINGLETON - shared across all workspaces)
            services.AddSingleton<WorkspaceHeaderViewModel>();

            // Main ViewModel (TRANSIENT - matches existing app lifecycle)
            services.AddTransient<MainViewModel>(provider =>
            {
                var viewAreaCoordinator = provider.GetRequiredService<IViewAreaCoordinator>();
                var navigationService = provider.GetRequiredService<INavigationService>();
                var titleViewModel = provider.GetRequiredService<TitleViewModel>();
                var logger = provider.GetService<ILogger<MainViewModel>>();

                return new MainViewModel(viewAreaCoordinator, navigationService, titleViewModel, logger);
            });

            // === REQUIREMENTS DOMAIN VIEWMODELS ===
            // Unified Requirements Main (SINGLETON - maintains requirement list and selection)
            services.AddSingleton<UnifiedRequirementsMainViewModel>(provider =>
            {
                var reqMediator = provider.GetRequiredService<IRequirementsMediator>();
                var persistence = provider.GetRequiredService<IPersistenceService>();
                var textEditingService = provider.GetRequiredService<ITextEditingDialogService>();
                var logger = provider.GetRequiredService<ILogger<UnifiedRequirementsMainViewModel>>();
                var requirementsSearchAttachmentsViewModel = provider.GetRequiredService<RequirementsSearchAttachmentsViewModel>();
                var navigationMediator = provider.GetRequiredService<INavigationMediator>();
                var workspaceDiagnosticsService = provider.GetRequiredService<IWorkspaceDiagnosticsService>();
                var analysisService = provider.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService>();

                return new UnifiedRequirementsMainViewModel(
                    reqMediator, logger, persistence, textEditingService, requirementsSearchAttachmentsViewModel, navigationMediator, workspaceDiagnosticsService, analysisService);
            });

            // Requirements Header (SINGLETON)
            services.AddSingleton<Requirements_HeaderViewModel>(provider =>
            {
                var reqMediator = provider.GetRequiredService<IRequirementsMediator>();
                var workspaceContext = provider.GetRequiredService<IWorkspaceContext>();
                var logger = provider.GetRequiredService<ILogger<Requirements_HeaderViewModel>>();
                return new Requirements_HeaderViewModel(reqMediator, workspaceContext, logger);
            });

            // Requirements Index (SINGLETON - requirement list state)
            services.AddSingleton<RequirementsIndexViewModel>(provider =>
            {
                var reqMediator = provider.GetRequiredService<IRequirementsMediator>() as RequirementsMediator;
                var logger = provider.GetRequiredService<ILogger<RequirementsIndexViewModel>>();

                if (reqMediator == null)
                    throw new InvalidOperationException("RequirementsMediator implementation not found");

                return new RequirementsIndexViewModel(
                    requirements: reqMediator.Requirements,
                    getCurrentRequirement: () => reqMediator.CurrentRequirement,
                    setCurrentRequirement: (req) => reqMediator.CurrentRequirement = req,
                    commitPendingEdits: null,
                    logger: logger);
            });

            // Requirements Navigation (SINGLETON - wraps index for navigation dropdown)
            services.AddSingleton<Requirements_NavigationViewModel>(provider =>
            {
                var requirementsIndexVM = provider.GetRequiredService<RequirementsIndexViewModel>();
                var logger = provider.GetRequiredService<ILogger<Requirements_NavigationViewModel>>();
                return new Requirements_NavigationViewModel(requirementsIndexVM, logger);
            });

            // Requirements Search in Attachments (SINGLETON - Jama document parsing state)
            services.AddSingleton<RequirementsSearchAttachmentsViewModel>(provider =>
            {
                var reqMediator = provider.GetRequiredService<IRequirementsMediator>();
                var workspaceContext = provider.GetRequiredService<IWorkspaceContext>();
                var logger = provider.GetRequiredService<ILogger<RequirementsSearchAttachmentsViewModel>>();
                var ollamaProcessManager = provider.GetService<IOllamaProcessManager>();
                return new RequirementsSearchAttachmentsViewModel(reqMediator, workspaceContext, logger, ollamaProcessManager);
            });

            // Cleanup ViewModel (SINGLETON - maintains requirement editing workspace state)
            services.AddSingleton<CleanupViewModel>(provider =>
            {
                var editSessionService = provider.GetRequiredService<RequirementEditSessionService>();
                var jamaService = provider.GetRequiredService<JamaConnectService>();
                var reqMediator = provider.GetRequiredService<IRequirementsMediator>();
                var logger = provider.GetRequiredService<ILogger<CleanupViewModel>>();
                var analysisService = provider.GetService<IRequirementAnalysisService>(); // Optional
                return new CleanupViewModel(editSessionService, jamaService, reqMediator, logger, analysisService);
            });

            // Requirements Tab Selector (SINGLETON - manages workspace tab switching)
            services.AddSingleton<RequirementsTabSelectorViewModel>(provider =>
            {
                var mainVM = provider.GetRequiredService<UnifiedRequirementsMainViewModel>();
                var cleanupVM = provider.GetRequiredService<CleanupViewModel>();
                var attachmentsVM = provider.GetRequiredService<RequirementsSearchAttachmentsViewModel>();
                var utilitiesVM = provider.GetRequiredService<RequirementsUtilitiesViewModel>();
                var logger = provider.GetRequiredService<ILogger<RequirementsTabSelectorViewModel>>();
                return new RequirementsTabSelectorViewModel(mainVM, cleanupVM, attachmentsVM, utilitiesVM, logger);
            });

            // Requirements Utilities (SINGLETON - utilities dashboard)
            services.AddSingleton<RequirementsUtilitiesViewModel>(provider =>
            {
                var reqMediator = provider.GetRequiredService<IRequirementsMediator>();
                var jamaService = provider.GetRequiredService<JamaConnectService>();
                var logger = provider.GetRequiredService<ILogger<RequirementsUtilitiesViewModel>>();
                return new RequirementsUtilitiesViewModel(reqMediator, jamaService, logger);
            });

            // Document Scraper (TRANSIENT - per-use instance)
            services.AddTransient<DocumentScraperViewModel>();

            // === TEST CASE CREATION DOMAIN VIEWMODELS ===
            // LLM Test Case Generator (SINGLETON - maintains generation state)
            services.AddSingleton<LLMTestCaseGeneratorViewModel>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<LLMTestCaseGeneratorViewModel>>();
                var generationService = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services.ITestCaseGenerationService>();
                var deduplicationService = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services.ITestCaseDeduplicationService>();
                var requirementsMediator = provider.GetRequiredService<IRequirementsMediator>();
                var newProjectMediator = provider.GetRequiredService<INewProjectMediator>();
                var openProjectMediator = provider.GetRequiredService<IOpenProjectMediator>();
                var promptDiagnostics = provider.GetRequiredService<PromptDiagnosticsViewModel>();

                return new LLMTestCaseGeneratorViewModel(
                    logger, generationService, deduplicationService, requirementsMediator, newProjectMediator, openProjectMediator, promptDiagnostics);
            });

            // Test Case Creation Main (TRANSIENT)
            services.AddTransient<TestCaseCreationMainVM>(provider =>
            {
                var mediator = provider.GetRequiredService<ITestCaseCreationMediator>();
                var logger = provider.GetRequiredService<ILogger<TestCaseCreationMainVM>>();
                return new TestCaseCreationMainVM(mediator, logger);
            });

            // Test Case Creation Navigation (SINGLETON - wraps main VM)
            services.AddSingleton<TestCaseCreation_NavigationViewModel>(provider =>
            {
                var mainVM = provider.GetRequiredService<LLMTestCaseGeneratorViewModel>();
                return new TestCaseCreation_NavigationViewModel(mainVM);
            });

            // Prompt Diagnostics (SINGLETON - debugging state)
            services.AddSingleton<PromptDiagnosticsViewModel>();

            // === NEW PROJECT DOMAIN VIEWMODELS ===
            services.AddTransient<NewProjectWorkflowViewModel>(provider =>
            {
                var newProjectMediator = provider.GetRequiredService<INewProjectMediator>();
                var logger = provider.GetRequiredService<ILogger<NewProjectWorkflowViewModel>>();
                var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();

                return new NewProjectWorkflowViewModel(
                    newProjectMediator, logger, anythingLLMService);
            });
            services.AddTransient<NewProjectHeaderViewModel>();
            services.AddTransient<DummyNewProjectTitleViewModel>();
            services.AddTransient<DummyNewProjectNavigationViewModel>();

            // === OPEN PROJECT DOMAIN VIEWMODELS ===
            services.AddTransient<OpenProjectWorkflowViewModel>(provider =>
            {
                var mediator = provider.GetRequiredService<IOpenProjectMediator>();
                var persistenceService = provider.GetRequiredService<IPersistenceService>();
                var recentFilesService = provider.GetRequiredService<RecentFilesService>();
                var jamaConnectService = provider.GetRequiredService<IJamaConnectService>();
                var workspaceContext = provider.GetRequiredService<IWorkspaceContext>();
                var navigationMediator = provider.GetRequiredService<INavigationMediator>();
                var logger = provider.GetRequiredService<ILogger<OpenProjectWorkflowViewModel>>();
                return new OpenProjectWorkflowViewModel(mediator, persistenceService, recentFilesService, jamaConnectService, workspaceContext, navigationMediator, logger);
            });
            services.AddTransient<OpenProject_TitleViewModel>();
            services.AddTransient<OpenProject_HeaderViewModel>();
            services.AddTransient<OpenProject_NavigationViewModel>();

            // === STARTUP DOMAIN VIEWMODELS ===
            services.AddTransient<StartUp_MainViewModel>(provider =>
            {
                var mediator = provider.GetRequiredService<IStartupMediator>();
                var navigationMediator = provider.GetRequiredService<INavigationMediator>();
                var recentFilesService = provider.GetRequiredService<RecentFilesService>();
                var workspaceContext = provider.GetRequiredService<IWorkspaceContext>();
                var jamaConnectService = provider.GetRequiredService<IJamaConnectService>();
                var anythingLlmService = provider.GetRequiredService<AnythingLLMService>();
                var newProjectMediator = provider.GetRequiredService<INewProjectMediator>();
                var openProjectMediator = provider.GetRequiredService<IOpenProjectMediator>();
                var requirementsMediator = provider.GetRequiredService<IRequirementsMediator>();
                var workspaceDiagnosticsService = provider.GetRequiredService<IWorkspaceDiagnosticsService>();
                var logger = provider.GetRequiredService<ILogger<StartUp_MainViewModel>>();
                return new StartUp_MainViewModel(
                    mediator,
                    navigationMediator,
                    recentFilesService,
                    workspaceContext,
                    jamaConnectService,
                    anythingLlmService,
                    newProjectMediator,
                    openProjectMediator,
                    requirementsMediator,
                    workspaceDiagnosticsService,
                    logger);
            });
            services.AddTransient<StartUp_HeaderViewModel>();
            services.AddTransient<StartUp_NavigationViewModel>();
            services.AddTransient<StartUp_TitleViewModel>();

            // === DUMMY DOMAIN VIEWMODELS (FOR TESTING) ===
            services.AddTransient<Dummy_MainViewModel>();
            services.AddTransient<Dummy_HeaderViewModel>();
            services.AddTransient<Dummy_NavigationViewModel>();
            services.AddTransient<Dummy_TitleViewModel>();

            // === TEST CASE GENERATOR MODE VIEWMODELS ===
            services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGenerator_Mode.ViewModels.TestCaseGeneratorMode_MainVM>();

            // === TRAINING DATA VALIDATION DOMAIN VIEWMODELS ===
            services.AddTransient<TrainingDataValidationViewModel>();

            return services;
        }
    }
}
