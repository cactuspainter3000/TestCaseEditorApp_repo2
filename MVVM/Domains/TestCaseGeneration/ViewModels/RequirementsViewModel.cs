using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// Dedicated ViewModel for requirements import and export management.
    /// Handles all requirements I/O operations and related workflows.
    /// </summary>
    public partial class RequirementsViewModel : ObservableObject
    {
        // Service dependencies
        private readonly IRequirementService _requirementService;
        private readonly IFileDialogService _fileDialogService;
        private readonly ChatGptExportService _chatGptExportService;
        private readonly NotificationService _notificationService;
        private readonly INavigationMediator _navigationMediator;
        private readonly ILogger<RequirementsViewModel>? _logger;
        
        // Legacy support for existing RequirementsView
        public TestCaseGenerator_VM TestCaseGeneratorVM { get; private set; }
        
        // Shared requirements collection (reference to MainViewModel's collection)
        public ObservableCollection<Requirement> Requirements { get; }

        // Import/Export Settings
        [ObservableProperty]
        private bool _autoAnalyzeOnImport = true;
        
        [ObservableProperty]
        private bool _autoExportForChatGpt = false;
        
        [ObservableProperty]
        private string? _lastChatGptExportFilePath;

        // UI Properties
        [ObservableProperty]
        private string title = "Requirements Management";
        
        [ObservableProperty]
        private string description = "Import requirements from various sources and export for analysis.";
        
        [ObservableProperty]
        private Requirement? _currentRequirement;

        // Commands
        public ICommand ImportFromWordCommand { get; }
        public ICommand ImportFromJamaCommand { get; }
        public ICommand ExportForChatGptCommand { get; }
        public ICommand ExportSelectedForChatGptCommand { get; }
        public ICommand ExportAllToJamaCommand { get; }
        public ICommand ToggleAutoExportCommand { get; }
        public ICommand OpenChatGptExportCommand { get; }

        /// <summary>
        /// Enhanced constructor with dependency injection for full functionality
        /// </summary>
        public RequirementsViewModel(
            IRequirementService requirementService,
            IFileDialogService fileDialogService,
            ChatGptExportService chatGptExportService,
            NotificationService notificationService,
            INavigationMediator navigationMediator,
            ObservableCollection<Requirement> requirements,
            IPersistenceService persistence,
            ITestCaseGenerationMediator testCaseGenerationMediator,
            object? testCaseGenerator = null,
            ILogger<RequirementsViewModel>? logger = null)
        {
            // Store dependencies
            _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            _chatGptExportService = chatGptExportService ?? throw new ArgumentNullException(nameof(chatGptExportService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            Requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            _logger = logger;

            // Legacy support - create TestCaseGenerator_VM for existing RequirementsView
            var testCaseGeneratorVMLogger = new LoggerFactory().CreateLogger<TestCaseGenerator_VM>();
            TestCaseGeneratorVM = new TestCaseGenerator_VM(testCaseGenerationMediator, persistence, testCaseGeneratorVMLogger);
            if (testCaseGenerator is TestCaseGenerator_CoreVM coreVm)
            {
                TestCaseGeneratorVM.TestCaseGenerator = coreVm;
            }

            TestCaseEditorApp.Services.Logging.Log.Info("[RequirementsViewModel] Enhanced constructor called with full dependencies");

            // Initialize commands
            ImportFromWordCommand = new AsyncRelayCommand(ImportFromWordAsync);
            ImportFromJamaCommand = new AsyncRelayCommand(ImportFromJamaAsync);
            ExportForChatGptCommand = new RelayCommand(ExportCurrentRequirementForChatGpt, () => CurrentRequirement != null);
            ExportSelectedForChatGptCommand = new RelayCommand(ExportSelectedRequirementsForChatGpt);
            ExportAllToJamaCommand = new RelayCommand(TryInvokeExportAllToJama);
            ToggleAutoExportCommand = new RelayCommand(() => AutoExportForChatGpt = !AutoExportForChatGpt);
            OpenChatGptExportCommand = new RelayCommand(OpenChatGptExportFile, () => !string.IsNullOrEmpty(LastChatGptExportFilePath) && File.Exists(LastChatGptExportFilePath));

            // Monitor property changes
            PropertyChanged += OnPropertyChanged;
            Requirements.CollectionChanged += (s, e) => 
            {
                // Update command states when requirements collection changes
                ((RelayCommand)ExportForChatGptCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ExportSelectedForChatGptCommand).NotifyCanExecuteChanged();
            };
        }

        /// <summary>
        /// Legacy constructor for compatibility (minimal functionality)
        /// </summary>
        public RequirementsViewModel(IPersistenceService persistence, ITestCaseGenerationMediator mediator, object? testCaseGenerator) 
            : this(
                new StubRequirementService(),
                new StubFileDialogService(),
                CreateStubChatGptExportService(),
                new StubNotificationService(),
                new StubNavigationMediator(),
                new ObservableCollection<Requirement>(),
                persistence,
                mediator,
                testCaseGenerator,
                null)
        {
            Title = "Requirements";
            Description = "View requirement details, tables, and supplemental information.";
            TestCaseEditorApp.Services.Logging.Log.Info("[RequirementsViewModel] Legacy constructor called - limited functionality");
        }

        private static ChatGptExportService CreateStubChatGptExportService()
        {
            try
            {
                return new ChatGptExportService();
            }
            catch
            {
                // If we can't create the service, we'll need to handle this gracefully
                throw new InvalidOperationException("Cannot create ChatGptExportService in design-time mode");
            }
        }

        private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(CurrentRequirement):
                    ((RelayCommand)ExportForChatGptCommand).NotifyCanExecuteChanged();
                    break;
                case nameof(LastChatGptExportFilePath):
                    ((RelayCommand)OpenChatGptExportCommand).NotifyCanExecuteChanged();
                    break;
            }
        }

        /// <summary>
        /// Import requirements from Word document
        /// </summary>
        private async Task ImportFromWordAsync()
        {
            try
            {
                var path = _fileDialogService.ShowOpenFile(
                    title: "Import Requirements from Word Document",
                    filter: "Word Documents|*.docx;*.doc|All Files|*.*");

                if (string.IsNullOrEmpty(path))
                    return;

                _logger?.LogInformation("Importing requirements from Word: {Path}", path);
                _notificationService.ShowInfo("Importing requirements...");

                var importedRequirements = await Task.Run(() => ImportRequirementsFromFile(path));
                await ProcessImportedRequirements(importedRequirements, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to import requirements from Word");
                _notificationService.ShowError($"Import failed: {ex.Message}", 8);
            }
        }

        /// <summary>
        /// Import requirements from Jama All Data document
        /// </summary>
        private async Task ImportFromJamaAsync()
        {
            try
            {
                var path = _fileDialogService.ShowOpenFile(
                    title: "Import Requirements from Jama All Data",
                    filter: "Word Documents|*.docx;*.doc|All Files|*.*");

                if (string.IsNullOrEmpty(path))
                    return;

                _logger?.LogInformation("Importing requirements from Jama: {Path}", path);
                _notificationService.ShowInfo("Importing requirements from Jama...");

                var importedRequirements = await Task.Run(() => ImportRequirementsFromFile(path, preferJamaParser: true));
                await ProcessImportedRequirements(importedRequirements, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to import requirements from Jama");
                _notificationService.ShowError($"Jama import failed: {ex.Message}", 8);
            }
        }

        /// <summary>
        /// Core import logic that tries both parsers and returns results
        /// </summary>
        private List<Requirement> ImportRequirementsFromFile(string path, bool preferJamaParser = false)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] ImportRequirementsFromFile called with path: {path}, preferJamaParser: {preferJamaParser}");

                if (_requirementService == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn("[Import] _requirementService is null - cannot import");
                    return new List<Requirement>();
                }

                List<Requirement> results;

                if (preferJamaParser)
                {
                    // Try Jama parser first
                    TestCaseEditorApp.Services.Logging.Log.Info("[Import] Trying Jama All Data parser...");
                    results = _requirementService.ImportRequirementsFromJamaAllDataDocx(path);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Jama parser returned {results.Count} requirements");

                    if (results.Count > 0)
                        return results;

                    // Fall back to Word parser
                    TestCaseEditorApp.Services.Logging.Log.Info("[Import] Trying regular Word parser as fallback...");
                    results = _requirementService.ImportRequirementsFromWord(path);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Word parser returned {results.Count} requirements");
                }
                else
                {
                    // Try Word parser first
                    TestCaseEditorApp.Services.Logging.Log.Info("[Import] Trying regular Word parser...");
                    results = _requirementService.ImportRequirementsFromWord(path);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Word parser returned {results.Count} requirements");

                    if (results.Count > 0)
                        return results;

                    // Fall back to Jama parser
                    TestCaseEditorApp.Services.Logging.Log.Info("[Import] Trying Jama All Data parser as fallback...");
                    results = _requirementService.ImportRequirementsFromJamaAllDataDocx(path);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Jama parser returned {results.Count} requirements");
                }

                if (results.Count == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[Import] No requirements found with either parser for file: {path}");
                }

                return results;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[Import] Error during import: {ex.Message}");
                return new List<Requirement>();
            }
        }

        /// <summary>
        /// Process imported requirements and add to collection
        /// </summary>
        private async Task ProcessImportedRequirements(List<Requirement> importedRequirements, string fileName)
        {
            if (importedRequirements.Count == 0)
            {
                _notificationService.ShowWarning("No requirements found in the selected file.", 5);
                return;
            }

            // Clear existing requirements and add new ones
            Requirements.Clear();
            foreach (var requirement in importedRequirements)
            {
                Requirements.Add(requirement);
            }

            _notificationService.ShowSuccess($"Successfully imported {importedRequirements.Count} requirements from {fileName}!", 6);
            _logger?.LogInformation("Successfully imported {Count} requirements from {FileName}", importedRequirements.Count, fileName);

            // Trigger auto-export if enabled
            if (AutoExportForChatGpt && Requirements.Any())
            {
                await Task.Delay(1000); // Brief delay to let UI update
                ExportSelectedRequirementsForChatGpt();
            }

            // Publish import event via mediator
            _navigationMediator.Publish(new RequirementsEvents.RequirementsImported(importedRequirements, fileName));
        }

        /// <summary>
        /// Export current requirement for ChatGPT analysis
        /// </summary>
        private void ExportCurrentRequirementForChatGpt()
        {
            try
            {
                if (CurrentRequirement == null)
                {
                    _notificationService.ShowWarning("No requirement selected for export.", 4);
                    return;
                }

                // Export to clipboard
                bool clipboardSuccess = _chatGptExportService.ExportAndCopy(CurrentRequirement, includeAnalysisRequest: true);

                // Also save to file
                string formattedText = _chatGptExportService.ExportSingleRequirement(CurrentRequirement, includeAnalysisRequest: true);
                string fileName = $"Requirement_{CurrentRequirement.Item?.Replace("/", "_").Replace("\\", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.md";
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

                try
                {
                    File.WriteAllText(filePath, formattedText);
                    LastChatGptExportFilePath = filePath;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Saved single requirement to file: {filePath}");
                }
                catch (Exception fileEx)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[CHATGPT EXPORT] Failed to save single requirement file: {fileEx.Message}");
                }

                if (clipboardSuccess)
                {
                    _notificationService.ShowSuccess($"✅ Requirement {CurrentRequirement.Item} exported to clipboard and saved to {fileName}!", 6);
                }
                else
                {
                    _notificationService.ShowWarning($"⚠️ Export saved to {fileName} but clipboard copy failed.", 5);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to export current requirement for ChatGPT");
                _notificationService.ShowError("Error exporting requirement for ChatGPT.", 5);
            }
        }

        /// <summary>
        /// Export selected (or all) requirements for ChatGPT analysis
        /// </summary>
        private void ExportSelectedRequirementsForChatGpt()
        {
            try
            {
                var requirementsToExport = Requirements.ToList();

                if (!requirementsToExport.Any())
                {
                    _notificationService.ShowWarning("No requirements available for export.", 4);
                    return;
                }

                bool success = _chatGptExportService.ExportAndCopyMultiple(requirementsToExport, includeAnalysisRequest: true);

                if (success)
                {
                    _notificationService.ShowSuccess($"{requirementsToExport.Count} requirements exported to clipboard for ChatGPT analysis!", 5);
                }
                else
                {
                    _notificationService.ShowError("Failed to export requirements to clipboard.", 5);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to export requirements for ChatGPT");
                _notificationService.ShowError("Error exporting requirements for ChatGPT.", 5);
            }
        }

        /// <summary>
        /// Export all requirements to Jama (reflection-based for compatibility)
        /// </summary>
        private void TryInvokeExportAllToJama()
        {
            try
            {
                // Try to find and invoke ExportAllToJama method via reflection (for compatibility with existing implementations)
                var exportMethod = this.GetType().GetMethod("ExportAllToJama", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (exportMethod != null)
                {
                    exportMethod.Invoke(this, Array.Empty<object>());
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "TryInvokeExportAllToJama failed");
            }

            _notificationService.ShowWarning("Export to Jama is not available.", 4);
        }

        /// <summary>
        /// Open the last exported ChatGPT file
        /// </summary>
        private void OpenChatGptExportFile()
        {
            try
            {
                if (!string.IsNullOrEmpty(LastChatGptExportFilePath) && File.Exists(LastChatGptExportFilePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = LastChatGptExportFilePath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    _notificationService.ShowWarning("Export file not found or path is invalid.", 4);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to open ChatGPT export file");
                _notificationService.ShowError("Failed to open export file.", 4);
            }
        }

        #region Stub Services (for legacy constructor compatibility)

        private class StubRequirementService : IRequirementService
        {
            public List<Requirement> ImportRequirementsFromJamaAllDataDocx(string path) => new List<Requirement>();
            public List<Requirement> ImportRequirementsFromWord(string path) => new List<Requirement>();
            public string ExportAllGeneratedTestCasesToCsv(IEnumerable<Requirement> requirements, string folderPath, string filePrefix, string extra) => string.Empty;
            public void ExportAllGeneratedTestCasesToExcel(IEnumerable<Requirement> requirements, string outputPath) { }
        }

        private class StubFileDialogService : IFileDialogService
        {
            public string? ShowOpenFile(string title, string filter, string? initialDirectory = null) => null;
            public string? ShowSaveFile(string title, string suggestedFileName, string filter, string defaultExt, string? initialDirectory = null) => null;
            public string? ShowFolderDialog(string title, string? initialDirectory = null) => null;
        }

        private class StubNotificationService : NotificationService
        {
            private static ToastNotificationService CreateStubToastService()
            {
                try
                {
                    return new ToastNotificationService(System.Windows.Threading.Dispatcher.CurrentDispatcher);
                }
                catch
                {
                    return null!;
                }
            }

            public StubNotificationService() : base(CreateStubToastService()!)
            {
            }

            // Override with no-op implementations for design-time safety
            public new void ShowSuccess(string message, int durationSeconds = 4) { }
            public new void ShowError(string message, int durationSeconds = 8) { }
            public new void ShowWarning(string message, int durationSeconds = 6) { }
            public new void ShowInfo(string message, int durationSeconds = 4) { }
        }

        private class StubNavigationMediator : INavigationMediator
        {
            public string? CurrentSection => null;
            public object? CurrentHeader => null;
            public object? CurrentContent => null;
            public void NavigateToSection(string sectionName, object? context = null) { }
            public void NavigateToStep(string stepId, object? context = null) { }
            public void SetActiveHeader(object? headerViewModel) { }
            public void SetMainContent(object? contentViewModel) { }
            public void ClearNavigationState() { }
            public void Subscribe<T>(Action<T> handler) where T : class { }
            public void Unsubscribe<T>(Action<T> handler) where T : class { }
            public void Publish<T>(T navigationEvent) where T : class { }
        }

        #endregion
    }

    /// <summary>
    /// Requirements-related events for the navigation mediator
    /// </summary>
    public static class RequirementsEvents
    {
        public class RequirementsImported
        {
            public List<Requirement> Requirements { get; }
            public string FileName { get; }

            public RequirementsImported(List<Requirement> requirements, string fileName)
            {
                Requirements = requirements;
                FileName = fileName;
            }
        }

        public class RequirementExported
        {
            public Requirement Requirement { get; }
            public string ExportType { get; }
            public string? FilePath { get; }

            public RequirementExported(Requirement requirement, string exportType, string? filePath = null)
            {
                Requirement = requirement;
                ExportType = exportType;
                FilePath = filePath;
            }
        }
    }
}