using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services;
using TestCaseEditorApp.MVVM.Models;
using ValidationResult = TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult;

namespace TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Mediators
{
    /// <summary>
    /// Mediator for Training Data Validation domain.
    /// Handles cross-domain communication and workflow coordination.
    /// </summary>
    public class TrainingDataValidationMediator
    {
        private readonly ILogger<TrainingDataValidationMediator> _logger;
        private readonly ITrainingDataValidationService _validationService;
        
        public TrainingDataValidationMediator()
        {
            _logger = App.ServiceProvider?.GetService<ILogger<TrainingDataValidationMediator>>();
            _validationService = App.ServiceProvider?.GetService<ITrainingDataValidationService>();
        }

        #region Events

        /// <summary>
        /// Fired when validation workflow state changes
        /// </summary>
        public event EventHandler<ValidationWorkflowStateChangedEventArgs>? WorkflowStateChanged;

        /// <summary>
        /// Fired when an example validation is completed
        /// </summary>
        public event EventHandler<ExampleValidatedEventArgs>? ExampleValidated;

        /// <summary>
        /// Fired when validation session is saved or loaded
        /// </summary>
        public event EventHandler<ValidationSessionEventArgs>? SessionEvent;

        /// <summary>
        /// Fired when validation metrics are updated
        /// </summary>
        public event EventHandler<ValidationMetricsEventArgs>? MetricsUpdated;

        #endregion

        #region Workflow Management

        /// <summary>
        /// Initiates a new training data validation workflow
        /// </summary>
        public async Task<bool> StartValidationWorkflowAsync(ValidationWorkflowRequest request)
        {
            try
            {
                _logger?.LogInformation("Starting validation workflow with {ExampleCount} examples", 
                    request.ExamplesToValidate.Count);

                // Notify state change
                OnWorkflowStateChanged(ValidationWorkflowState.Generating, ValidationWorkflowState.Validating);

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to start validation workflow");
                OnWorkflowStateChanged(ValidationWorkflowState.Validating, ValidationWorkflowState.Error);
                return false;
            }
        }

        /// <summary>
        /// Pauses the current validation workflow
        /// </summary>
        public async Task<bool> PauseValidationWorkflowAsync()
        {
            try
            {
                _logger?.LogInformation("Pausing validation workflow");
                OnWorkflowStateChanged(ValidationWorkflowState.Validating, ValidationWorkflowState.Paused);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to pause validation workflow");
                return false;
            }
        }

        /// <summary>
        /// Resumes a paused validation workflow
        /// </summary>
        public async Task<bool> ResumeValidationWorkflowAsync()
        {
            try
            {
                _logger?.LogInformation("Resuming validation workflow");
                OnWorkflowStateChanged(ValidationWorkflowState.Paused, ValidationWorkflowState.Validating);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to resume validation workflow");
                return false;
            }
        }

        /// <summary>
        /// Completes the validation workflow and processes results
        /// </summary>
        public async Task<ValidationWorkflowResult> CompleteValidationWorkflowAsync()
        {
            try
            {
                _logger?.LogInformation("Completing validation workflow");

                var metrics = await _validationService?.GetValidationMetricsAsync();
                var analysis = await _validationService?.AnalyzeValidationPatternsAsync();

                var result = new ValidationWorkflowResult
                {
                    CompletedAt = DateTime.UtcNow,
                    Metrics = metrics,
                    Analysis = analysis,
                    IsSuccessful = true
                };

                OnWorkflowStateChanged(ValidationWorkflowState.Validating, ValidationWorkflowState.Complete);
                OnMetricsUpdated(metrics);

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to complete validation workflow");
                OnWorkflowStateChanged(ValidationWorkflowState.Validating, ValidationWorkflowState.Error);
                
                return new ValidationWorkflowResult
                {
                    CompletedAt = DateTime.UtcNow,
                    IsSuccessful = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        #endregion

        #region Validation Management

        /// <summary>
        /// Records a validation decision and coordinates follow-up actions
        /// </summary>
        public async Task<bool> RecordValidationDecisionAsync(ValidationDecisionRequest request)
        {
            try
            {
                _logger?.LogInformation("Recording validation decision for example {ExampleId}: {Decision}", 
                    request.ExampleId, request.Decision);

                var validationResult = new ValidationResult
                {
                    ExampleId = request.ExampleId,
                    Decision = request.Decision,
                    Reason = request.Reason,
                    ConfidenceScore = request.ConfidenceScore,
                    Tags = request.Tags,
                    Metadata = request.Metadata
                };

                await _validationService?.RecordValidationAsync(validationResult);

                // Fire validation completed event
                OnExampleValidated(validationResult);

                // Update metrics
                var metrics = await _validationService?.GetValidationMetricsAsync();
                OnMetricsUpdated(metrics);

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to record validation decision for example {ExampleId}", request.ExampleId);
                return false;
            }
        }

        /// <summary>
        /// Requests quality assessment for a training example
        /// </summary>
        public async Task<QualityAssessment?> RequestQualityAssessmentAsync(SyntheticTrainingExample example)
        {
            try
            {
                _logger?.LogInformation("Requesting quality assessment for example {ExampleId}", example.ExampleId);
                return await _validationService?.AssessExampleQualityAsync(example);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to assess example quality for {ExampleId}", example.ExampleId);
                return null;
            }
        }

        #endregion

        #region Session Management

        /// <summary>
        /// Saves the current validation session
        /// </summary>
        public async Task<bool> SaveValidationSessionAsync(ValidationSession session)
        {
            try
            {
                _logger?.LogInformation("Saving validation session {SessionId}", session.Id);
                await _validationService?.SaveValidationSessionAsync(session);
                
                OnSessionEvent(new ValidationSessionEventArgs
                {
                    SessionId = session.Id,
                    EventType = ValidationSessionEventType.Saved,
                    Session = session
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save validation session {SessionId}", session.Id);
                return false;
            }
        }

        /// <summary>
        /// Loads a previously saved validation session
        /// </summary>
        public async Task<ValidationSession?> LoadValidationSessionAsync(string sessionId)
        {
            try
            {
                _logger?.LogInformation("Loading validation session {SessionId}", sessionId);
                var session = await _validationService?.LoadValidationSessionAsync(sessionId);
                
                if (session != null)
                {
                    OnSessionEvent(new ValidationSessionEventArgs
                    {
                        SessionId = sessionId,
                        EventType = ValidationSessionEventType.Loaded,
                        Session = session
                    });
                }

                return session;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load validation session {SessionId}", sessionId);
                return null;
            }
        }

        #endregion

        #region Cross-Domain Communication

        /// <summary>
        /// Notifies other domains that training data validation has been completed
        /// </summary>
        public async Task NotifyTrainingDataReadyAsync(TrainingDataReadyNotification notification)
        {
            try
            {
                _logger?.LogInformation("Notifying domains that {Count} validated examples are ready", 
                    notification.ValidatedExamples.Count);

                // This would trigger notifications to other domains that might need the validated data
                // E.g., TestCaseGeneration domain could use this data to improve its capabilities

                // For now, just log the notification
                _logger?.LogInformation("Training data notification sent: {ApprovedCount} approved examples ready", 
                    notification.ValidatedExamples.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to notify domains about ready training data");
            }
        }

        /// <summary>
        /// Handles requests from other domains for validation metrics
        /// </summary>
        public async Task<ValidationMetrics?> HandleMetricsRequestAsync()
        {
            try
            {
                return await _validationService?.GetValidationMetricsAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to handle metrics request");
                return null;
            }
        }

        #endregion

        #region Event Handlers

        protected virtual void OnWorkflowStateChanged(ValidationWorkflowState oldState, ValidationWorkflowState newState)
        {
            WorkflowStateChanged?.Invoke(this, new ValidationWorkflowStateChangedEventArgs
            {
                OldState = oldState,
                NewState = newState,
                Timestamp = DateTime.UtcNow
            });
        }

        protected virtual void OnExampleValidated(ValidationResult result)
        {
            ExampleValidated?.Invoke(this, new ExampleValidatedEventArgs
            {
                ValidationResult = result,
                Timestamp = DateTime.UtcNow
            });
        }

        protected virtual void OnSessionEvent(ValidationSessionEventArgs args)
        {
            SessionEvent?.Invoke(this, args);
        }

        protected virtual void OnMetricsUpdated(ValidationMetrics? metrics)
        {
            if (metrics != null)
            {
                MetricsUpdated?.Invoke(this, new ValidationMetricsEventArgs
                {
                    Metrics = metrics,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        #endregion
    }

    #region Event Args and Related Classes

    public class ValidationWorkflowStateChangedEventArgs : EventArgs
    {
        public ValidationWorkflowState OldState { get; set; }
        public ValidationWorkflowState NewState { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ExampleValidatedEventArgs : EventArgs
    {
        public ValidationResult ValidationResult { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ValidationSessionEventArgs : EventArgs
    {
        public string SessionId { get; set; } = string.Empty;
        public ValidationSessionEventType EventType { get; set; }
        public ValidationSession? Session { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class ValidationMetricsEventArgs : EventArgs
    {
        public ValidationMetrics Metrics { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum ValidationSessionEventType
    {
        Created,
        Saved,
        Loaded,
        Deleted,
        Completed
    }

    public class ValidationWorkflowRequest
    {
        public List<SyntheticTrainingExample> ExamplesToValidate { get; set; } = new();
        public ValidationWorkflowOptions Options { get; set; } = new();
        public string RequestedBy { get; set; } = Environment.UserName;
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    }

    public class ValidationWorkflowOptions
    {
        public bool AutoSave { get; set; } = true;
        public TimeSpan AutoSaveInterval { get; set; } = TimeSpan.FromMinutes(5);
        public bool RequireQualityAssessment { get; set; } = true;
        public double MinimumQualityThreshold { get; set; } = 0.7;
        public bool EnableHotkeys { get; set; } = true;
    }

    public class ValidationWorkflowResult
    {
        public DateTime CompletedAt { get; set; }
        public bool IsSuccessful { get; set; }
        public ValidationMetrics? Metrics { get; set; }
        public ValidationAnalysis? Analysis { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class ValidationDecisionRequest
    {
        public string ExampleId { get; set; } = string.Empty;
        public ValidationDecision Decision { get; set; }
        public string Reason { get; set; } = string.Empty;
        public double ConfidenceScore { get; set; }
        public List<string> Tags { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public class TrainingDataReadyNotification
    {
        public List<SyntheticTrainingExample> ValidatedExamples { get; set; } = new();
        public ValidationMetrics Metrics { get; set; }
        public DateTime NotificationTime { get; set; } = DateTime.UtcNow;
        public string WorkflowId { get; set; } = string.Empty;
    }

    #endregion
}