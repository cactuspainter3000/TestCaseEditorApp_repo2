using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Available recommendations from the analysis
        /// </summary>
        public List<AnalysisRecommendation> Recommendations 
        { 
            get 
            {
                try 
                {
                    // Safe access to analysis data
                    var analysis = _requirement?.Analysis;
                    if (analysis?.Recommendations != null)
                    {
                        return analysis.Recommendations;
                    }
                    return new List<AnalysisRecommendation>();
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementEditor] Error accessing recommendations: {ex.Message}");
                    return new List<AnalysisRecommendation>();
                }
            }
        }

        /// <summary>
        /// Whether analysis recommendations are available
        /// </summary>
        public bool HasRecommendations 
        { 
            get 
            {
                try 
                {
                    var recommendations = Recommendations;
                    if (recommendations != null && recommendations.Count > 0)
                    {
                        return recommendations.Any(r => !string.IsNullOrEmpty(r?.SuggestedEdit));
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementEditor] Error checking HasRecommendations: {ex.Message}");
                    return false;
                }
            }
        }

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
        public ICommand ApplySuggestedEditCommand { get; }
        // Temporarily keep SendToLearningRepositoryCommand commented out
        // public ICommand SendToLearningRepositoryCommand { get; }

        public RequirementDescriptionEditorViewModel(Requirement requirement, NotificationService notificationService)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementEditor] Constructor called for {requirement?.Item}");
                
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
                ApplySuggestedEditCommand = new RelayCommand<AnalysisRecommendation>(ApplySuggestedEdit);
                // Temporarily keep SendToLearningRepositoryCommand commented out
                // SendToLearningRepositoryCommand = new RelayCommand(SendToLearningRepository);
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[RequirementEditor] Constructor completed successfully");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[RequirementEditor] Error in constructor");
                throw;
            }
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
                        // ObservableProperty automatically notifies command CanExecute changes
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

        private void ApplySuggestedEdit(AnalysisRecommendation? recommendation)
        {
            if (recommendation?.SuggestedEdit == null) 
            {
                _notificationService?.ShowWarning("No suggested edit available for this recommendation");
                return;
            }
            
            try
            {
                EditedDescription = recommendation.SuggestedEdit;
                _notificationService?.ShowSuccess($"Applied suggested edit from '{recommendation.Category}' recommendation");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[RequirementEditor] Error applying suggested edit");
                _notificationService?.ShowError($"Failed to apply suggested edit: {ex.Message}");
            }
        }

        private void SendToLearningRepository()
        {
            try
            {
                // Create learning data payload with original and edited text
                var learningData = new
                {
                    RequirementId = _requirement.Item,
                    RequirementName = _requirement.Name,
                    OriginalDescription = _requirement.Description,
                    EditedDescription = EditedDescription,
                    Timestamp = DateTime.UtcNow,
                    OriginalQualityScore = _requirement.Analysis?.OriginalQualityScore,
                    Issues = _requirement.Analysis?.Issues?.Select(i => new { i.Category, i.Description, i.Severity }),
                    Recommendations = _requirement.Analysis?.Recommendations?.Select(r => new { r.Category, r.Description, r.SuggestedEdit })
                };

                // For now, save to a learning repository file (could be enhanced to send to API endpoint)
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var learningDir = System.IO.Path.Combine(appDataPath, "TestCaseEditorApp", "LearningRepository");
                System.IO.Directory.CreateDirectory(learningDir);
                
                var fileName = $"learning_data_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}_{_requirement.Item?.Replace(":", "-")}.json";
                var filePath = System.IO.Path.Combine(learningDir, fileName);
                
                var json = System.Text.Json.JsonSerializer.Serialize(learningData, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                System.IO.File.WriteAllText(filePath, json);
                
                _notificationService.ShowSuccess($"Requirement edit sent to learning repository: {fileName}");
                TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementEditor] Learning data saved to: {filePath}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[RequirementEditor] Error sending to learning repository");
                _notificationService.ShowError($"Failed to send to learning repository: {ex.Message}");
            }
        }

        partial void OnIsAnalyzingChanged(bool value)
        {
            // ObservableProperty automatically notifies command CanExecute changes
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