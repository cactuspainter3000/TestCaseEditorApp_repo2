using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for requirement description editor modal.
    /// Provides text editing capabilities with analysis integration.
    /// </summary>
    public partial class RequirementDescriptionEditorViewModel : ObservableObject
    {
        private readonly Requirement _requirement;
        private readonly NotificationService _notificationService;

        [ObservableProperty]
        private string _editedDescription = string.Empty;

        [ObservableProperty]
        private string _windowTitle = "Edit Requirement Description";

        [ObservableProperty]
        private bool _isAnalyzing = false;

        /// <summary>
        /// Event raised when the user saves the changes
        /// </summary>
        public event EventHandler<RequirementEditedEventArgs>? RequirementEdited;

        /// <summary>
        /// Event raised when the user cancels the operation
        /// </summary>
        public event EventHandler? Cancelled;

        /// <summary>
        /// Event raised when the user requests re-analysis
        /// </summary>
        public event EventHandler<RequirementAnalysisRequestedEventArgs>? AnalysisRequested;

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ReAnalyzeCommand { get; }
        public ICommand InspectPromptCommand { get; }

        public RequirementDescriptionEditorViewModel(Requirement requirement, NotificationService notificationService)
        {
            _requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            // Initialize with current values
            EditedDescription = requirement.Description ?? string.Empty;
            
            // Set dynamic window title with requirement info
            var requirementId = !string.IsNullOrEmpty(requirement.Item) ? requirement.Item : "Unknown";
            var requirementName = !string.IsNullOrEmpty(requirement.Name) ? requirement.Name : "Untitled";
            WindowTitle = $"Edit Requirement: {requirementId} - {requirementName}";

            // Initialize commands
            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(Cancel);
            ReAnalyzeCommand = new AsyncRelayCommand(ReAnalyzeAsync, () => !IsAnalyzing);
            InspectPromptCommand = new RelayCommand(InspectPrompt);
        }

        private void Save()
        {
            try
            {
                // Update the requirement
                _requirement.Description = EditedDescription;

                // Notify success
                _notificationService.ShowSuccess($"Updated requirement description");
                
                // Raise event with the updated requirement
                RequirementEdited?.Invoke(this, new RequirementEditedEventArgs(_requirement));
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[RequirementEditor] Error saving requirement");
                _notificationService.ShowError($"Failed to save requirement: {ex.Message}");
            }
        }

        private void Cancel()
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }

        private System.Threading.Tasks.Task ReAnalyzeAsync()
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    IsAnalyzing = true;
                    
                    // Update requirement description from editor
                    _requirement.Description = EditedDescription;
                    
                    var preview = _requirement.Description?.Length > 50 
                        ? _requirement.Description.Substring(0, 50) + "..." 
                        : _requirement.Description ?? "";
                    
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementEditor] Re-analyzing requirement: {preview}");
                    
                    // Raise event to request analysis
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        AnalysisRequested?.Invoke(this, new RequirementAnalysisRequestedEventArgs(_requirement));
                    });
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, "[RequirementEditor] Error during re-analysis");
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        _notificationService.ShowError($"Failed to re-analyze requirement: {ex.Message}");
                    });
                }
                finally
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsAnalyzing = false;
                        ((AsyncRelayCommand)ReAnalyzeCommand).NotifyCanExecuteChanged();
                    });
                }
            });
        }

        private void InspectPrompt()
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[RequirementEditor] Inspect Prompt clicked");
                
                // Create a basic prompt inspection for now
                var promptInspection = $"Requirement Analysis Prompt Inspection\n" +
                                     $"Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n" +
                                     $"Requirement ID: {_requirement.Item}\n" +
                                     $"Requirement Name: {_requirement.Name}\n" +
                                     $"Description Length: {_requirement.Description?.Length ?? 0} characters\n\n" +
                                     $"Description:\n{_requirement.Description ?? "No description"}";

                // Write to a desktop file for easier viewing
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var fileName = $"LLM_Analysis_Prompt_Inspection_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
                var filePath = System.IO.Path.Combine(desktopPath, fileName);

                System.IO.File.WriteAllText(filePath, promptInspection);

                _notificationService.ShowSuccess($"Prompt inspection saved to desktop: {fileName}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[RequirementEditor] Error inspecting prompt");
                _notificationService.ShowError($"Failed to inspect prompt: {ex.Message}");
            }
        }

        partial void OnIsAnalyzingChanged(bool value)
        {
            ((AsyncRelayCommand)ReAnalyzeCommand).NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Event args for requirement edited event
    /// </summary>
    public class RequirementEditedEventArgs : EventArgs
    {
        public Requirement Requirement { get; }

        public RequirementEditedEventArgs(Requirement requirement)
        {
            Requirement = requirement;
        }
    }

    /// <summary>
    /// Event args for analysis requested event
    /// </summary>
    public class RequirementAnalysisRequestedEventArgs : EventArgs
    {
        public Requirement Requirement { get; }

        public RequirementAnalysisRequestedEventArgs(Requirement requirement)
        {
            Requirement = requirement;
        }
    }
}